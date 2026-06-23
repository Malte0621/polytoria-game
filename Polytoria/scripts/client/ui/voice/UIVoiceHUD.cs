// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Services;
using Polytoria.Enums;

namespace Polytoria.Client.UI.Voice;

/// <summary>
/// Voice-chat control placed in the top button bar, right after the Backpack button.
/// It is a 48x48 button that matches the other topbar buttons — it has no style override,
/// so it inherits the same square background from the theme — with a microphone icon
/// that reflects state: red when voice is off, white when on/idle, green while the local
/// player is speaking. Tap toggles voice on/off; in push-to-talk mode, press-and-hold
/// transmits. This node only manages the button; the button itself lives in the topbar.
/// </summary>
public sealed partial class UIVoiceHUD : Node
{
	private const string MicIconPath = "res://assets/textures/client/ui/menu_button/microphone.svg";

	private World _root = null!;
	private VoiceChatService _voice = null!;
	private Button _button = null!;
	private TextureRect _icon = null!;
	private bool _ready;
	private bool _ptHeld;

	public override void _Ready()
	{
		SetProcess(false);
		Init();
	}

	private void Init()
	{
		_root = CoreUIRoot.Singleton?.Root!;
		HBoxContainer? bar = CoreUIRoot.Singleton?.GetNodeOrNull<HBoxContainer>("Control/MenuButton");
		Button? backpack = CoreUIRoot.Singleton?.GetNodeOrNull<Button>("Control/MenuButton/Backpack");

		if (_root == null || _root.VoiceChat == null || bar == null || backpack == null)
		{
			// CoreUI / world not fully wired yet — retry next frame.
			CallDeferred(nameof(Init));
			return;
		}
		if (_ready)
		{
			return;
		}
		_ready = true;
		_voice = _root.VoiceChat;

		BuildButton(bar, backpack);
		SetProcess(true);
	}

	private void BuildButton(HBoxContainer bar, Button backpack)
	{
		_button = new Button
		{
			Name = "Voice",
			CustomMinimumSize = new Vector2(48, 48),
			FocusMode = Control.FocusModeEnum.None,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
			TooltipText = "Voice Chat"
		};
		// Intentionally NO theme stylebox override -> inherits the same square topbar
		// button background (normal/hover/pressed) as Menu/Chat/Backpack.

		_icon = new TextureRect
		{
			Texture = GD.Load<Texture2D>(MicIconPath),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		// Match the icon placement used by the other topbar buttons (centered, 32x48).
		_icon.AnchorLeft = 0.5f;
		_icon.AnchorTop = 0.5f;
		_icon.AnchorRight = 0.5f;
		_icon.AnchorBottom = 0.5f;
		_icon.OffsetLeft = -16f;
		_icon.OffsetTop = -24f;
		_icon.OffsetRight = 16f;
		_icon.OffsetBottom = 24f;
		_button.AddChild(_icon);

		bar.AddChild(_button);
		bar.MoveChild(_button, backpack.GetIndex() + 1); // right after Backpack (far right)

		_button.Pressed += OnPressed;
		_button.ButtonDown += OnButtonDown;
		_button.ButtonUp += OnButtonUp;
	}

	private bool IsPushToTalk => _voice.ActivationMode == VoiceActivationModeEnum.PushToTalk;

	private void OnPressed()
	{
		// Push-to-talk uses hold (down/up); a tap toggles voice in the other modes.
		if (IsPushToTalk)
		{
			return;
		}
		_voice.Toggle();
	}

	private void OnButtonDown()
	{
		if (!IsPushToTalk)
		{
			return;
		}
		if (!_voice.Enabled)
		{
			_voice.Enabled = true;
		}
		_ptHeld = true;
		_voice.SetPushToTalkActive(true);
	}

	private void OnButtonUp()
	{
		ReleasePushToTalk();
	}

	/// <summary>Clear the push-to-talk latch (idempotent), so the mic can never stay stuck on.</summary>
	private void ReleasePushToTalk()
	{
		if (!_ptHeld)
		{
			return;
		}
		_ptHeld = false;
		if (_voice != null)
		{
			_voice.SetPushToTalkActive(false);
		}
	}

	public override void _Process(double delta)
	{
		if (!_ready)
		{
			return;
		}

		// Self-heal a stuck push-to-talk latch if the button stopped being held without a
		// ButtonUp (e.g. the topbar was hidden via hide_ui mid-hold).
		if (_ptHeld && !_button.IsPressed())
		{
			ReleasePushToTalk();
		}

		if (!_voice.Enabled)
		{
			_icon.Modulate = new Color(0.95f, 0.42f, 0.42f); // off / muted -> red
			return;
		}

		_icon.Modulate = _voice.IsLocalSpeaking
			? new Color(0.35f, 1f, 0.45f)  // speaking -> green
			: new Color(1f, 1f, 1f);       // on, idle -> white
	}

	public override void _ExitTree()
	{
		ReleasePushToTalk();
		if (_button != null && GodotObject.IsInstanceValid(_button))
		{
			_button.QueueFree();
		}
		base._ExitTree();
	}
}
