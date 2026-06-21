// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Enums;

namespace Polytoria.Client.UI.Voice;

/// <summary>
/// On-screen voice control shown in the in-game HUD (bottom-left). It is a single mic
/// button that doubles as a status indicator:
///   * voice off  -> red muted-mic icon (tap to enable)
///   * voice on    -> green sound-bars (animated while the local player is speaking)
/// In push-to-talk mode the button is hold-to-talk (and auto-enables voice); in the
/// other modes a tap toggles voice on/off. Works on desktop and touch.
/// </summary>
public sealed partial class UIVoiceHUD : Control
{
	private const string MicIconPath = "res://assets/textures/client/ui/indicators/silence.svg";

	private World _root = null!;
	private Button _button = null!;
	private TextureRect _mutedIcon = null!;
	private HBoxContainer _bars = null!;
	private readonly ColorRect[] _voiceBars = new ColorRect[3];
	private bool _ready;
	private bool _ptHeld;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		SetProcess(false);
		Init();
	}

	private void Init()
	{
		_root = CoreUIRoot.Singleton?.Root!;
		if (_root == null || _root.VoiceChat == null)
		{
			// World/service not wired yet — retry next frame.
			CallDeferred(nameof(Init));
			return;
		}
		if (_ready)
		{
			return;
		}
		_ready = true;

		BuildButton();
		SetProcess(true);
	}

	private void BuildButton()
	{
		_button = new Button
		{
			FocusMode = FocusModeEnum.None,
			MouseDefaultCursorShape = CursorShape.PointingHand,
			TooltipText = "Voice chat"
		};
		_button.SetAnchorsPreset(LayoutPreset.BottomLeft);
		_button.OffsetLeft = 24;
		_button.OffsetTop = -84;
		_button.OffsetRight = 84;
		_button.OffsetBottom = -24;
		_button.AddThemeStyleboxOverride("normal", MakeStyle(new Color(0.10f, 0.10f, 0.12f, 0.70f)));
		_button.AddThemeStyleboxOverride("hover", MakeStyle(new Color(0.15f, 0.15f, 0.18f, 0.80f)));
		_button.AddThemeStyleboxOverride("pressed", MakeStyle(new Color(0.20f, 0.20f, 0.24f, 0.90f)));
		AddChild(_button);

		_mutedIcon = new TextureRect
		{
			Texture = GD.Load<Texture2D>(MicIconPath),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = MouseFilterEnum.Ignore,
			Modulate = new Color(0.92f, 0.38f, 0.38f)
		};
		_mutedIcon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_mutedIcon.OffsetLeft = 14;
		_mutedIcon.OffsetTop = 14;
		_mutedIcon.OffsetRight = -14;
		_mutedIcon.OffsetBottom = -14;
		_button.AddChild(_mutedIcon);

		_bars = new HBoxContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
			Alignment = BoxContainer.AlignmentMode.Center
		};
		_bars.AddThemeConstantOverride("separation", 5);
		_bars.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		for (int i = 0; i < _voiceBars.Length; i++)
		{
			ColorRect bar = new()
			{
				Color = new Color(0.4f, 0.7f, 0.45f),
				CustomMinimumSize = new Vector2(8, 10),
				SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
			};
			_voiceBars[i] = bar;
			_bars.AddChild(bar);
		}
		_button.AddChild(_bars);

		_button.Pressed += OnPressed;
		_button.ButtonDown += OnButtonDown;
		_button.ButtonUp += OnButtonUp;
	}

	private static StyleBoxFlat MakeStyle(Color color)
	{
		return new StyleBoxFlat
		{
			BgColor = color,
			CornerRadiusTopLeft = 30,
			CornerRadiusTopRight = 30,
			CornerRadiusBottomLeft = 30,
			CornerRadiusBottomRight = 30
		};
	}

	private bool IsPushToTalk => _root.VoiceChat.ActivationMode == VoiceActivationModeEnum.PushToTalk;

	private void OnPressed()
	{
		// Push-to-talk uses hold (button down/up); a tap toggles voice in the other modes.
		if (IsPushToTalk)
		{
			return;
		}
		_root.VoiceChat.Toggle();
	}

	private void OnButtonDown()
	{
		if (!IsPushToTalk)
		{
			return;
		}
		if (!_root.VoiceChat.Enabled)
		{
			_root.VoiceChat.Enabled = true;
		}
		_ptHeld = true;
		_root.VoiceChat.SetPushToTalkActive(true);
	}

	private void OnButtonUp()
	{
		ReleasePushToTalk();
	}

	/// <summary>Clear the push-to-talk latch (idempotent). Tied to the HUD's lifetime so the
	/// mic can never stay stuck transmitting after the button is released/hidden/freed.</summary>
	private void ReleasePushToTalk()
	{
		if (!_ptHeld)
		{
			return;
		}
		_ptHeld = false;
		if (_root != null && _root.VoiceChat != null)
		{
			_root.VoiceChat.SetPushToTalkActive(false);
		}
	}

	public override void _Process(double delta)
	{
		if (!_ready)
		{
			return;
		}

		// Self-heal a stuck push-to-talk latch: if we think it's held but the button is no
		// longer pressed (e.g. the HUD was hidden via hide_ui mid-hold), release it.
		if (_ptHeld && !_button.IsPressed())
		{
			ReleasePushToTalk();
		}

		bool enabled = _root.VoiceChat.Enabled;
		_mutedIcon.Visible = !enabled;
		_bars.Visible = enabled;
		if (!enabled)
		{
			return;
		}

		bool speaking = _root.VoiceChat.IsLocalSpeaking;
		double t = Time.GetTicksMsec() / 1000.0;
		for (int i = 0; i < _voiceBars.Length; i++)
		{
			float h = speaking
				? 10f + 16f * (0.5f + 0.5f * Mathf.Sin((float)(t * 14.0) + i * 1.6f))
				: 10f;
			_voiceBars[i].CustomMinimumSize = new Vector2(8, h);
			_voiceBars[i].Color = speaking ? new Color(0.25f, 0.95f, 0.35f) : new Color(0.4f, 0.7f, 0.45f);
		}
	}

	public override void _ExitTree()
	{
		ReleasePushToTalk();
		base._ExitTree();
	}
}
