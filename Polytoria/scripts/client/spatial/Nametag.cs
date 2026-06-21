// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;

namespace Polytoria.Client;

public partial class Nametag : Node3D
{
	private Label _titleLabel = null!;
	private ProgressBar _healthBar = null!;
	private Node3D _nametag = null!;

	private Control _speakingIndicator = null!;
	private readonly ColorRect[] _voiceBars = new ColorRect[3];

	public NPC Target = null!;

	public override void _Ready()
	{
		_nametag = Globals.CreateInstanceFromScene<Node3D>("res://scenes/client/spatial/nametag.tscn");
		AddChild(_nametag);
		_titleLabel = _nametag.GetNode<Label>("SubViewport/Control/Title");
		_healthBar = _nametag.GetNode<ProgressBar>("SubViewport/Control/Healthbar");

		// Overhead "speaking" indicator: a small badge of animated sound bars, shown
		// above the name while the target player is talking on voice chat.
		Control container = _nametag.GetNode<Control>("SubViewport/Control");
		_speakingIndicator = BuildSpeakingIndicator();
		container.AddChild(_speakingIndicator);
		container.MoveChild(_speakingIndicator, 0);
		_speakingIndicator.Visible = false;
	}

	private Control BuildSpeakingIndicator()
	{
		CenterContainer wrap = new() { CustomMinimumSize = new Vector2(0, 32) };

		PanelContainer panel = new();
		StyleBoxFlat style = new()
		{
			BgColor = new Color(0.07f, 0.07f, 0.07f, 0.75f),
			CornerRadiusTopLeft = 10,
			CornerRadiusTopRight = 10,
			CornerRadiusBottomLeft = 10,
			CornerRadiusBottomRight = 10,
			ContentMarginLeft = 8,
			ContentMarginRight = 8,
			ContentMarginTop = 5,
			ContentMarginBottom = 5
		};
		panel.AddThemeStyleboxOverride("panel", style);

		HBoxContainer bars = new();
		bars.AddThemeConstantOverride("separation", 4);
		for (int i = 0; i < _voiceBars.Length; i++)
		{
			ColorRect bar = new()
			{
				Color = new Color(0.25f, 0.9f, 0.35f),
				CustomMinimumSize = new Vector2(7, 22),
				SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
			};
			_voiceBars[i] = bar;
			bars.AddChild(bar);
		}

		panel.AddChild(bars);
		wrap.AddChild(panel);
		return wrap;
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		UpdateNameTag();
	}

	public void UpdateNameTag()
	{
		bool useNametag = Target.UseNametag;

		Camera? cam = Target.Root.Environment.CurrentCamera;

		// Check distance from camera if is with-in radius
		if (cam != null && useNametag)
		{
			useNametag = (cam.Position - GlobalPosition).Length() < Target.NametagVisibleRadius;
		}

		// Hide if self is Target
		if (Target == Target.Root.Players?.LocalPlayer)
		{
			useNametag = false;
		}

		Visible = useNametag;
		_titleLabel.Text = Target.DisplayName != string.Empty ? Target.DisplayName : Target.Name;
		_healthBar.Visible = (Target.Health < Target.MaxHealth);
		_healthBar.Value = Target.Health;
		_healthBar.MaxValue = Target.MaxHealth;

		bool speaking = useNametag && Target is Player player && player.IsSpeaking;
		_speakingIndicator.Visible = speaking;
		if (speaking)
		{
			AnimateVoiceBars();
		}
	}

	private void AnimateVoiceBars()
	{
		double t = Time.GetTicksMsec() / 1000.0;
		for (int i = 0; i < _voiceBars.Length; i++)
		{
			float h = 8f + 14f * (0.5f + 0.5f * Mathf.Sin((float)(t * 12.0) + i * 1.7f));
			_voiceBars[i].CustomMinimumSize = new Vector2(7, h);
		}
	}
}
