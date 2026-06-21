// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared.Voice;
using System;

namespace Polytoria.Client.Voice;

/// <summary>
/// Per-remote-speaker playback node. It is parented to the speaker's character so the
/// child <see cref="AudioStreamPlayer3D"/> is spatialised at their position with the
/// same attenuation model the engine uses for <see cref="Sound"/>. Incoming voice
/// packets are decoded and pushed into an <see cref="AudioStreamGenerator"/>; the
/// generator's buffer absorbs network jitter. Out-of-order / duplicate / stale packets
/// are dropped using a monotonic sequence number so playback never jumps backwards.
/// </summary>
public sealed partial class VoiceReceiver : Node3D
{
	/// <summary>Raised when the speaker starts (true) or stops (false) being heard.</summary>
	public event Action<bool>? SpeakingChanged;

	public bool IsSpeaking { get; private set; }

	private const int MaxConcealFrames = 5;

	private AudioStreamPlayer3D _player = null!;
	private AudioStreamGeneratorPlayback _playback = null!;
	private IVoiceCodec _codec = null!;
	private int _lastSeq = int.MinValue;
	private double _silenceTimer;

	public void Setup(IVoiceCodec codec, float maxDistance, float volumeLinear)
	{
		_codec = codec;

		AudioStreamGenerator generator = new()
		{
			MixRate = VoiceConstants.SampleRate,
			BufferLength = VoiceConstants.GeneratorBufferLength
		};

		_player = new AudioStreamPlayer3D
		{
			Stream = generator,
			Bus = VoiceConstants.VoiceBusName,
			AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
			AttenuationFilterCutoffHz = 5000,
			MaxDistance = maxDistance * Sound.SoundDistanceMultipler,
			VolumeLinear = volumeLinear
		};
		AddChild(_player);
		_player.Play();
		_playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();

		SetProcess(true);
	}

	public void SetMaxDistance(float maxDistance)
	{
		if (_player != null)
		{
			_player.MaxDistance = maxDistance * Sound.SoundDistanceMultipler;
		}
	}

	public void SetVolume(float volumeLinear)
	{
		if (_player != null)
		{
			_player.VolumeLinear = volumeLinear;
		}
	}

	/// <summary>Decode and enqueue a voice packet for playback. Stale/duplicate packets are ignored.</summary>
	public void Push(int seq, byte[] data)
	{
		// Monotonic sequence: only accept strictly newer packets (drops dupes/reorders).
		if (seq <= _lastSeq)
		{
			return;
		}
		if (_playback == null)
		{
			return;
		}

		// Conceal lost frames between the last accepted packet and this one so the
		// (stateful) decoder and the playback timeline stay coherent across packet loss.
		if (_lastSeq != int.MinValue)
		{
			int gap = seq - _lastSeq - 1;
			if (gap > 0)
			{
				int conceal = Math.Min(gap, MaxConcealFrames);
				// Older lost frames: pure concealment.
				for (int g = 0; g < conceal - 1; g++)
				{
					PushPcm(_codec.DecodeLost());
				}
				// Most recent lost frame: recover via in-band FEC carried in THIS packet.
				PushPcm(_codec.DecodeFec(data));
			}
		}
		_lastSeq = seq;

		if (PushPcm(_codec.Decode(data)))
		{
			_silenceTimer = 0;
			SetSpeaking(true);
		}
	}

	private bool PushPcm(short[] pcm)
	{
		if (pcm == null || pcm.Length == 0 || _playback == null)
		{
			return false;
		}

		// Drop the whole frame on buffer overrun rather than pushing a partial frame
		// (a partial push would shift timing/pitch).
		if (_playback.GetFramesAvailable() < pcm.Length)
		{
			return false;
		}

		Vector2[] buffer = new Vector2[pcm.Length];
		for (int i = 0; i < pcm.Length; i++)
		{
			// Clamp because the decoder can legitimately emit short.MinValue (-32768),
			// which / short.MaxValue is fractionally below -1.0.
			float s = Mathf.Clamp(pcm[i] / (float)short.MaxValue, -1f, 1f);
			buffer[i] = new Vector2(s, s);
		}
		_playback.PushBuffer(buffer);
		return true;
	}

	public override void _Process(double delta)
	{
		if (IsSpeaking)
		{
			_silenceTimer += delta;
			if (_silenceTimer > VoiceConstants.SpeakingTimeoutSeconds)
			{
				SetSpeaking(false);
			}
		}
	}

	private void SetSpeaking(bool value)
	{
		if (value == IsSpeaking)
		{
			return;
		}
		IsSpeaking = value;
		SpeakingChanged?.Invoke(value);
	}
}
