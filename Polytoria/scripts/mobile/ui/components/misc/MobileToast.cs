// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Mobile.UI;

/// <summary>
/// Lightweight, non-blocking toast for the mobile UI. Built entirely in code so it has
/// no scene dependency. Shown via <see cref="MobileUI.ShowToast"/>. Keep messages short
/// and user-friendly; log full exception detail with PT.PrintErr separately.
/// </summary>
public partial class MobileToast : Control
{
	private PanelContainer _panel = null!;
	private Label _label = null!;
	private Tween? _tween;

	public override void _Ready()
	{
		// The toast overlay must never eat touch input from the UI behind it.
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		_panel = new PanelContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
			Modulate = new Color(1, 1, 1, 0),
		};
		_panel.SetAnchorsPreset(LayoutPreset.CenterBottom);
		_panel.GrowHorizontal = GrowDirection.Both;
		_panel.GrowVertical = GrowDirection.Begin;
		// Sit above the 80px navbar with a comfortable margin.
		_panel.OffsetBottom = -120;

		StyleBoxFlat style = new()
		{
			BgColor = new Color(0.02f, 0.03f, 0.05f, 0.93f),
			CornerRadiusTopLeft = 12,
			CornerRadiusTopRight = 12,
			CornerRadiusBottomLeft = 12,
			CornerRadiusBottomRight = 12,
			ContentMarginLeft = 22,
			ContentMarginRight = 22,
			ContentMarginTop = 12,
			ContentMarginBottom = 12,
		};
		_panel.AddThemeStyleboxOverride("panel", style);

		_label = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_label.AddThemeColorOverride("font_color", new Color(1, 1, 1));
		_panel.AddChild(_label);
		AddChild(_panel);

		base._Ready();
	}

	public void Show(string message, float duration = 3.0f)
	{
		if (_panel == null || _label == null)
		{
			return;
		}

		_label.Text = message;

		_tween?.Kill();
		_panel.Modulate = new Color(1, 1, 1, 0);

		_tween = CreateTween();
		_tween.TweenProperty(_panel, "modulate:a", 1.0f, 0.2f);
		_tween.TweenInterval(duration);
		_tween.TweenProperty(_panel, "modulate:a", 0.0f, 0.4f);
	}
}
