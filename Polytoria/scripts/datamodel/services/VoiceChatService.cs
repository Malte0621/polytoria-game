// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.Settings;
using Polytoria.Client.Voice;
using Polytoria.Enums;
using Polytoria.Networking;
using Polytoria.Networking.RateLimiters;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Shared.Settings;
using Polytoria.Shared.Voice;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel.Services;

/// <summary>
/// Spatial voice chat. Exposed to Luau as <c>VoiceChat</c> / <c>game.VoiceChat</c>.
///
/// Pipeline: the local client captures the microphone, encodes 20 ms frames and sends
/// them to the SERVER (RpcId(1, ...)). The server is the moderation chokepoint: it
/// validates the sender, applies server-mute / server-deafen / voice-channel / per-pair
/// rules (and an optional distance cull), then relays each allowed frame to each allowed
/// listener (RpcId(peer, ...)). Receiving clients decode and play the frame through a
/// positional <see cref="VoiceReceiver"/> parented to the speaker, so voice attenuates
/// with 3D distance just like a <see cref="Sound"/>.
///
/// Client members (Enabled / volumes / activation mode / push-to-talk / device /
/// per-player local mute) are local-only and also driven by the persisted Voice
/// settings. Server members (SetServerMuted / SetServerDeafened / SetVoiceChannel /
/// SetCanHear) are server-authoritative and throw if called off the server.
/// </summary>
[Static("VoiceChat"), ExplorerExclude, SaveIgnore]
public sealed partial class VoiceChatService : Instance
{
	// Per-peer cap on inbound voice frames (50 fps nominal + headroom) to stop flooding.
	private const int VoiceFramesPerSecondCap = 75;

	// ---- Script events ----------------------------------------------------------------

	/// <summary>Fires when any player (local or remote) starts/stops speaking: (player, isSpeaking).</summary>
	[ScriptProperty] public PTSignal<Player, bool> PlayerSpeaking { get; private set; } = new();

	/// <summary>Fires when the LOCAL player starts/stops transmitting.</summary>
	[ScriptProperty] public PTSignal<bool> LocalSpeakingChanged { get; private set; } = new();

	/// <summary>Fires when a voice setting/toggle changes.</summary>
	[ScriptProperty] public PTSignal StateChanged { get; private set; } = new();

	// ---- Client configuration (local-only) --------------------------------------------

	private bool _enabled;
	private float _inputVolume = 1f;
	private float _outputVolume = 1f;
	private float _maxDistance = 60f;
	private VoiceActivationModeEnum _activationMode = VoiceActivationModeEnum.VoiceActivity;
	private KeyCodeEnum _pushToTalkKey = KeyCodeEnum.V;
	private float _activationThreshold = 0.05f;
	private string _inputDevice = "Default";

	/// <summary>Master toggle. Turning it off stops capturing AND playback of others.</summary>
	[ScriptProperty]
	public bool Enabled
	{
		get => _enabled;
		set => SetEnabled(value);
	}

	/// <summary>Linear microphone gain (0..4).</summary>
	[ScriptProperty]
	public float InputVolume
	{
		get => _inputVolume;
		set
		{
			_inputVolume = Mathf.Clamp(value, 0f, 4f);
			if (_capture != null)
			{
				_capture.InputGain = _inputVolume;
			}
		}
	}

	/// <summary>Linear output volume for all incoming voice (0..4); applied to the "Voice" bus.</summary>
	[ScriptProperty]
	public float OutputVolume
	{
		get => _outputVolume;
		set
		{
			_outputVolume = Mathf.Clamp(value, 0f, 4f);
			ApplyOutputVolume();
		}
	}

	/// <summary>Distance (studs) at which a speaker's voice fully attenuates on this client.</summary>
	[ScriptProperty]
	public float MaxDistance
	{
		get => _maxDistance;
		set
		{
			_maxDistance = Mathf.Max(1f, value);
			ApplyMaxDistance();
		}
	}

	/// <summary>How the microphone decides to transmit (push-to-talk / voice-activity / open).</summary>
	[ScriptProperty]
	public VoiceActivationModeEnum ActivationMode
	{
		get => _activationMode;
		set => _activationMode = value;
	}

	/// <summary>Key held to transmit when <see cref="ActivationMode"/> is PushToTalk.</summary>
	[ScriptProperty]
	public KeyCodeEnum PushToTalkKey
	{
		get => _pushToTalkKey;
		set => _pushToTalkKey = value;
	}

	/// <summary>RMS loudness (0..1) above which voice-activity opens the mic.</summary>
	[ScriptProperty]
	public float ActivationThreshold
	{
		get => _activationThreshold;
		set => _activationThreshold = Mathf.Clamp(value, 0f, 1f);
	}

	/// <summary>Microphone device name ("Default" for the system default).</summary>
	[ScriptProperty]
	public string InputDevice
	{
		get => _inputDevice;
		set => SetInputDevice(value);
	}

	/// <summary>True while the local player is transmitting.</summary>
	[ScriptProperty] public bool IsLocalSpeaking => _localSpeaking;

	/// <summary>
	/// Server-side: if &gt; 0, the server will not relay a speaker's voice to listeners
	/// further than this many studs away (bandwidth/privacy cull). 0 = relay regardless
	/// of distance and rely on client-side spatial attenuation. Server-only (the setter
	/// throws off the server, like the other moderation members).
	/// </summary>
	[ScriptProperty]
	public float MaxBroadcastDistance
	{
		get => _maxBroadcastDistance;
		set
		{
			EnsureServer();
			_maxBroadcastDistance = value;
		}
	}
	private float _maxBroadcastDistance;

	// ---- Client runtime ---------------------------------------------------------------

	private bool _clientAudio;
	private VoiceCapture? _capture;
	private readonly Dictionary<int, VoiceReceiver> _receivers = new();   // by speaker UserID
	private readonly HashSet<int> _locallyMuted = new();                  // speaker UserIDs muted locally
	// Created lazily so the Opus/Concentus codec is ONLY ever loaded on the client when
	// actually capturing — never during World.Setup() in the creator or on a dedicated
	// server (where voice never runs). This keeps the voice feature from being able to
	// affect non-client startup at all.
	private IVoiceCodec? _codec;
	private IVoiceCodec Codec => _codec ??= VoiceCodecFactory.Create(); // local microphone encoder
	private int _seq;
	private bool _localSpeaking;
	private float _vadHangover;
	private int _captureRetries;
	private bool _touchTransmit; // held by the on-screen push-to-talk HUD button

	// ---- Server runtime ---------------------------------------------------------------

	// listener PeerID -> set of speaker PeerIDs that listener may NOT hear.
	private readonly Dictionary<int, HashSet<int>> _pairBlocks = new();
	private readonly Dictionary<int, SlidingWindowRateLimiter> _voiceRate = new();

	// ---- Lifecycle --------------------------------------------------------------------

	public override void Init()
	{
		_clientAudio = Root != null && Root.Network != null && !Root.Network.IsServer
			&& Globals.GDAvailable && Root.SessionType == World.SessionTypeEnum.Client;

		if (_clientAudio)
		{
			VoiceAudio.EnsureVoiceBus();

			PT.Print("[Voice] Using codec: ", VoiceCodecFactory.ActiveCodecName);

			_capture = new VoiceCapture { Name = "VoiceCapture" };
			GDNode.AddChild(_capture, false, Node.InternalMode.Front);
			_capture.FrameCaptured += OnFrameCaptured;

			if (ClientSettingsService.Instance != null)
			{
				LoadFromSettings();
				ClientSettingsService.Instance.Changed += OnSettingsChanged;
			}

			_capture.InputGain = _inputVolume;
			ApplyOutputVolume();
			SetInputDevice(_inputDevice);

			if (_enabled)
			{
				_captureRetries = 0;
				StartCapture();
			}
		}

		base.Init();
	}

	public override void Ready()
	{
		if (Root?.Players != null)
		{
			Root.Players.PlayerRemoved.Connect(OnPlayerRemoved);
		}
		base.Ready();
	}

	public override void PreDelete()
	{
		if (ClientSettingsService.Instance != null)
		{
			ClientSettingsService.Instance.Changed -= OnSettingsChanged;
		}
		if (Root?.Players != null)
		{
			Root.Players.PlayerRemoved.Disconnect(OnPlayerRemoved);
		}
		if (_capture != null)
		{
			_capture.FrameCaptured -= OnFrameCaptured;
			_capture.Stop();
		}
		ClearReceivers();
		base.PreDelete();
	}

	// ---- Client: settings -------------------------------------------------------------

	private void LoadFromSettings()
	{
		ClientSettingsService s = ClientSettingsService.Instance;
		if (s == null)
		{
			return;
		}

		_enabled = s.Get<bool>(ClientSettingKeys.Voice.Enabled);
		_inputVolume = s.Get<float>(ClientSettingKeys.Voice.InputVolume) / 100f;
		_outputVolume = s.Get<float>(ClientSettingKeys.Voice.OutputVolume) / 100f;
		_maxDistance = Mathf.Max(1f, s.Get<float>(ClientSettingKeys.Voice.MaxDistance));
		_activationMode = s.Get<VoiceActivationModeEnum>(ClientSettingKeys.Voice.ActivationMode);
		_pushToTalkKey = s.Get<KeyCodeEnum>(ClientSettingKeys.Voice.PushToTalkKey);
		_activationThreshold = Mathf.Clamp(s.Get<float>(ClientSettingKeys.Voice.ActivationThreshold), 0f, 1f);
		_inputDevice = s.Get<string>(ClientSettingKeys.Voice.InputDevice);
	}

	private void OnSettingsChanged(SettingChangedEvent e)
	{
		ClientSettingsService s = ClientSettingsService.Instance;
		if (s == null)
		{
			return;
		}

		// Route through the public setters so side effects (capture start/stop, bus
		// volume, receiver distances) are applied.
		switch (e.Key)
		{
			case ClientSettingKeys.Voice.Enabled:
				Enabled = s.Get<bool>(e.Key);
				break;
			case ClientSettingKeys.Voice.InputVolume:
				InputVolume = s.Get<float>(e.Key) / 100f;
				break;
			case ClientSettingKeys.Voice.OutputVolume:
				OutputVolume = s.Get<float>(e.Key) / 100f;
				break;
			case ClientSettingKeys.Voice.MaxDistance:
				MaxDistance = s.Get<float>(e.Key);
				break;
			case ClientSettingKeys.Voice.ActivationMode:
				ActivationMode = s.Get<VoiceActivationModeEnum>(e.Key);
				break;
			case ClientSettingKeys.Voice.PushToTalkKey:
				PushToTalkKey = s.Get<KeyCodeEnum>(e.Key);
				break;
			case ClientSettingKeys.Voice.ActivationThreshold:
				ActivationThreshold = s.Get<float>(e.Key);
				break;
			case ClientSettingKeys.Voice.InputDevice:
				InputDevice = s.Get<string>(e.Key);
				break;
		}
	}

	// ---- Client: enable / capture -----------------------------------------------------

	private void SetEnabled(bool value)
	{
		if (_enabled == value)
		{
			return;
		}
		_enabled = value;

		if (_clientAudio)
		{
			if (value)
			{
				_captureRetries = 0;
				StartCapture();
			}
			else
			{
				StopCapture();
				ClearReceivers();
			}
		}

		StateChanged.Invoke();
	}

	private void StartCapture()
	{
		if (_capture == null)
		{
			return;
		}

		_capture.InputGain = _inputVolume;

		// Android (and other mobile) require a runtime RECORD_AUDIO grant.
		if (Globals.IsMobileBuild && !HasMicPermission())
		{
			RequestMicPermission();
			ScheduleCaptureRetry();
			return;
		}

		if (!_capture.Start())
		{
			PT.PrintWarn("[Voice] Microphone capture failed to start.");
		}
	}

	private void StopCapture()
	{
		_touchTransmit = false;
		_capture?.Stop();
		SetLocalSpeaking(false, Root?.Players?.LocalPlayer);
	}

	private static bool HasMicPermission()
	{
		if (!Globals.IsMobileBuild)
		{
			return true;
		}
		foreach (string p in OS.GetGrantedPermissions())
		{
			if (p.EndsWith("RECORD_AUDIO", StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static void RequestMicPermission()
	{
		OS.RequestPermission("RECORD_AUDIO");
	}

	private void ScheduleCaptureRetry()
	{
		SceneTree? tree = GDNode?.GetTree();
		if (tree == null)
		{
			return;
		}
		SceneTreeTimer timer = tree.CreateTimer(1.5);
		timer.Timeout += OnCaptureRetry;
	}

	private void OnCaptureRetry()
	{
		if (!_enabled || _capture == null || _capture.Capturing)
		{
			return;
		}
		if (HasMicPermission())
		{
			_capture.Start();
			return;
		}
		if (++_captureRetries < 6)
		{
			ScheduleCaptureRetry();
		}
	}

	private void OnFrameCaptured(short[] pcm, float rms)
	{
		if (!_enabled || !_clientAudio)
		{
			return;
		}

		bool transmit;
		switch (_activationMode)
		{
			case VoiceActivationModeEnum.Open:
				transmit = true;
				break;
			case VoiceActivationModeEnum.PushToTalk:
				transmit = Root.Input != null && Root.Input.IsKeyPressed(_pushToTalkKey);
				break;
			default: // VoiceActivity
				if (rms >= _activationThreshold)
				{
					_vadHangover = VoiceConstants.VadHangoverSeconds;
					transmit = true;
				}
				else
				{
					_vadHangover -= VoiceConstants.FrameMs / 1000f;
					transmit = _vadHangover > 0f;
				}
				break;
		}

		// An on-screen push-to-talk button (mobile) forces transmission regardless of mode.
		if (_touchTransmit)
		{
			transmit = true;
		}

		Player? localPlayer = Root.Players?.LocalPlayer;

		// A server-muted player must not transmit at all (saves their upstream too).
		if (localPlayer != null && localPlayer.IsServerMuted)
		{
			transmit = false;
		}

		if (transmit)
		{
			byte[] data = Codec.Encode(pcm);
			if (data.Length > 0)
			{
				RpcId(1, nameof(NetServerRecvVoice), _seq++, data);
			}
		}

		SetLocalSpeaking(transmit, localPlayer);
	}

	private void SetLocalSpeaking(bool value, Player? localPlayer)
	{
		if (value == _localSpeaking)
		{
			return;
		}
		_localSpeaking = value;
		LocalSpeakingChanged.Invoke(value);
		if (localPlayer != null)
		{
			localPlayer.SetSpeaking(value);
			PlayerSpeaking.Invoke(localPlayer, value);
		}
	}

	private void SetInputDevice(string device)
	{
		_inputDevice = device;
		if (_clientAudio && Globals.GDAvailable)
		{
			try
			{
				AudioServer.InputDevice = string.IsNullOrEmpty(device) ? "Default" : device;
			}
			catch (Exception ex)
			{
				PT.PrintWarn("[Voice] Failed to set input device: ", ex.Message);
			}
		}
	}

	private void ApplyOutputVolume()
	{
		if (!_clientAudio)
		{
			return;
		}
		int idx = VoiceAudio.EnsureVoiceBus();
		AudioServer.SetBusVolumeDb(idx, Mathf.LinearToDb(Mathf.Max(_outputVolume, 0.0001f)));
	}

	private void ApplyMaxDistance()
	{
		foreach (VoiceReceiver r in _receivers.Values)
		{
			if (GodotObject.IsInstanceValid(r))
			{
				r.SetMaxDistance(_maxDistance);
			}
		}
	}

	// ---- Client: playback -------------------------------------------------------------

	private VoiceReceiver? GetOrCreateReceiver(Player speaker)
	{
		int uid = speaker.UserID;
		if (_receivers.TryGetValue(uid, out VoiceReceiver? existing))
		{
			if (GodotObject.IsInstanceValid(existing))
			{
				return existing;
			}
			_receivers.Remove(uid);
		}

		Node3D? host = speaker.GDNode3D;
		if (host == null || !GodotObject.IsInstanceValid(host))
		{
			return null;
		}

		VoiceReceiver recv = new() { Name = "VoiceReceiver" };
		recv.SpeakingChanged += speaking => OnReceiverSpeaking(speaker, speaking);
		host.AddChild(recv, false, Node.InternalMode.Back);
		// Each speaker gets its OWN codec instance — the Opus decoder is stateful per stream.
		recv.Setup(VoiceCodecFactory.Create(), _maxDistance, 1f);
		_receivers[uid] = recv;
		return recv;
	}

	private void OnReceiverSpeaking(Player speaker, bool speaking)
	{
		if (!GodotObject.IsInstanceValid(speaker.GDNode))
		{
			return;
		}
		speaker.SetSpeaking(speaking);
		PlayerSpeaking.Invoke(speaker, speaking);
	}

	private void ClearReceivers()
	{
		foreach (VoiceReceiver r in _receivers.Values)
		{
			if (GodotObject.IsInstanceValid(r))
			{
				r.QueueFree();
			}
		}
		_receivers.Clear();
	}

	private void OnPlayerRemoved(Player plr)
	{
		// Server moderation cleanup.
		_pairBlocks.Remove(plr.PeerID);
		foreach (HashSet<int> set in _pairBlocks.Values)
		{
			set.Remove(plr.PeerID);
		}
		_voiceRate.Remove(plr.PeerID);

		// Client playback cleanup.
		if (_receivers.TryGetValue(plr.UserID, out VoiceReceiver? recv))
		{
			_receivers.Remove(plr.UserID);
			if (GodotObject.IsInstanceValid(recv))
			{
				recv.QueueFree();
			}
		}
	}

	// ---- Transport --------------------------------------------------------------------

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Unreliable, TransferChannel = VoiceConstants.NetChannel)]
	private void NetServerRecvVoice(int seq, byte[] data)
	{
		if (Root?.Network == null || !Root.Network.IsServer)
		{
			return;
		}
		if (data == null || data.Length == 0)
		{
			return;
		}

		int peerID = RemoteSenderId;
		Player? speaker = Root.Players.GetPlayerFromPeerID(peerID);
		if (speaker == null)
		{
			return;
		}

		// Anti-flood: cap inbound frames per peer.
		if (!GetVoiceRate(peerID).TryAccept())
		{
			return;
		}

		// Server-mute: drop the speaker's voice for everyone.
		if (speaker.IsServerMuted)
		{
			return;
		}

		int channel = speaker.VoiceChannel;
		Vector3 speakerPos = speaker.Position;
		float maxDist = MaxBroadcastDistance;

		foreach (Player listener in Root.Players.GetPlayers())
		{
			if (listener == speaker)
			{
				continue;
			}
			if (listener.IsServerDeafened)
			{
				continue; // listener hears no one
			}
			if (listener.VoiceChannel != channel)
			{
				continue; // different voice channel
			}
			if (IsPairBlocked(listener.PeerID, peerID))
			{
				continue; // explicit who-can-hear-whom restriction
			}
			if (maxDist > 0f && speakerPos.DistanceTo(listener.Position) > maxDist)
			{
				continue; // out of server broadcast range
			}

			RpcId(listener.PeerID, nameof(NetClientRecvVoice), speaker.UserID, seq, data);
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Unreliable, TransferChannel = VoiceConstants.NetChannel)]
	private void NetClientRecvVoice(int speakerUserID, int seq, byte[] data)
	{
		if (Root?.Network == null || Root.Network.IsServer)
		{
			return;
		}
		if (!_enabled)
		{
			return;
		}
		if (_locallyMuted.Contains(speakerUserID))
		{
			return; // locally muted by this user
		}

		Player? speaker = Root.Players.GetPlayerByID(speakerUserID);
		if (speaker == null || speaker == Root.Players.LocalPlayer)
		{
			return;
		}

		GetOrCreateReceiver(speaker)?.Push(seq, data);
	}

	private SlidingWindowRateLimiter GetVoiceRate(int peerID)
	{
		if (!_voiceRate.TryGetValue(peerID, out SlidingWindowRateLimiter? limiter))
		{
			limiter = new SlidingWindowRateLimiter(VoiceFramesPerSecondCap, TimeSpan.FromSeconds(1));
			_voiceRate[peerID] = limiter;
		}
		return limiter;
	}

	private bool IsPairBlocked(int listenerPeer, int speakerPeer)
	{
		return _pairBlocks.TryGetValue(listenerPeer, out HashSet<int>? set) && set.Contains(speakerPeer);
	}

	// ---- Script API: client methods ---------------------------------------------------

	/// <summary>Toggle the local voice-chat enabled state.</summary>
	[ScriptMethod]
	public void Toggle() => Enabled = !_enabled;

	/// <summary>Hold/release an on-screen push-to-talk button (mobile HUD). Forces transmit while held.</summary>
	[ScriptMethod]
	public void SetPushToTalkActive(bool active) => _touchTransmit = active;

	/// <summary>List available microphone device names.</summary>
	[ScriptMethod]
	public string[] GetInputDevices()
	{
		return Globals.GDAvailable ? AudioServer.GetInputDeviceList() : Array.Empty<string>();
	}

	/// <summary>Locally mute/unmute a player (affects only this client's playback).</summary>
	[ScriptMethod]
	public void SetPlayerMuted(Player player, bool muted)
	{
		ArgumentNullException.ThrowIfNull(player);
		if (muted)
		{
			_locallyMuted.Add(player.UserID);
		}
		else
		{
			_locallyMuted.Remove(player.UserID);
		}
	}

	/// <summary>Whether a player is locally muted on this client.</summary>
	[ScriptMethod]
	public bool IsPlayerMuted(Player player)
	{
		ArgumentNullException.ThrowIfNull(player);
		return _locallyMuted.Contains(player.UserID);
	}

	/// <summary>Whether a player is currently speaking (audible/transmitting).</summary>
	[ScriptMethod]
	public bool IsPlayerSpeaking(Player player)
	{
		ArgumentNullException.ThrowIfNull(player);
		return player.IsSpeaking;
	}

	// ---- Script API: server moderation (server-only) ----------------------------------

	/// <summary>Server-mute a player so NO ONE hears them. Server-only.</summary>
	[ScriptMethod]
	public void SetServerMuted(Player player, bool muted)
	{
		EnsureServer();
		ArgumentNullException.ThrowIfNull(player);
		player.IsServerMuted = muted;
	}

	/// <summary>Whether a player is server-muted.</summary>
	[ScriptMethod]
	public bool IsServerMuted(Player player)
	{
		ArgumentNullException.ThrowIfNull(player);
		return player.IsServerMuted;
	}

	/// <summary>Server-deafen a player so THEY hear no one. Server-only.</summary>
	[ScriptMethod]
	public void SetServerDeafened(Player player, bool deafened)
	{
		EnsureServer();
		ArgumentNullException.ThrowIfNull(player);
		player.IsServerDeafened = deafened;
	}

	/// <summary>Whether a player is server-deafened.</summary>
	[ScriptMethod]
	public bool IsServerDeafened(Player player)
	{
		ArgumentNullException.ThrowIfNull(player);
		return player.IsServerDeafened;
	}

	/// <summary>
	/// Set a player's voice channel. Players only hear others on the SAME channel
	/// (default 0). Use this for team/proximity-group voice. Server-only.
	/// </summary>
	[ScriptMethod]
	public void SetVoiceChannel(Player player, int channel)
	{
		EnsureServer();
		ArgumentNullException.ThrowIfNull(player);
		player.VoiceChannel = channel;
	}

	/// <summary>Get a player's voice channel.</summary>
	[ScriptMethod]
	public int GetVoiceChannel(Player player)
	{
		ArgumentNullException.ThrowIfNull(player);
		return player.VoiceChannel;
	}

	/// <summary>
	/// Explicitly allow/deny whether <paramref name="listener"/> may hear
	/// <paramref name="speaker"/> (overlays on top of channels). Server-only.
	/// </summary>
	[ScriptMethod]
	public void SetCanHear(Player listener, Player speaker, bool canHear)
	{
		EnsureServer();
		ArgumentNullException.ThrowIfNull(listener);
		ArgumentNullException.ThrowIfNull(speaker);

		int lp = listener.PeerID;
		int sp = speaker.PeerID;
		if (canHear)
		{
			if (_pairBlocks.TryGetValue(lp, out HashSet<int>? set))
			{
				set.Remove(sp);
				if (set.Count == 0)
				{
					_pairBlocks.Remove(lp);
				}
			}
		}
		else
		{
			if (!_pairBlocks.TryGetValue(lp, out HashSet<int>? set))
			{
				set = new HashSet<int>();
				_pairBlocks[lp] = set;
			}
			set.Add(sp);
		}
	}

	private void EnsureServer()
	{
		if (Root?.Network == null || !Root.Network.IsServer)
		{
			throw new InvalidOperationException("VoiceChat moderation methods can only be called from the server.");
		}
	}
}
