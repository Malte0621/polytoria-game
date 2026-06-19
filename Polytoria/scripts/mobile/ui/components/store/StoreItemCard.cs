// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared.AssetLoaders;
using System;

namespace Polytoria.Mobile.UI;

// Touch-friendly card for a single store item. Built entirely in code so the
// owning view can spawn it without a hand-authored scene. The thumbnail is a
// direct web URL (APIStoreItem.Thumbnail) loaded via WebAssetLoader.
public partial class StoreItemCard : Button
{
	private TextureRect _thumbnailRect = null!;
	private Label _nameLabel = null!;
	private Label _priceLabel = null!;

	public APIStoreItem ItemData;

	// Raised when the card is tapped, carrying the item id so the view can open detail.
	public event Action<int>? CardPressed;

	public override void _Ready()
	{
		CustomMinimumSize = new Vector2(150, 200);
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		ClipText = true;

		VBoxContainer layout = new()
		{
			MouseFilter = MouseFilterEnum.Ignore,
		};
		layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.KeepSize, 8);
		layout.AddThemeConstantOverride("separation", 6);
		AddChild(layout);

		_thumbnailRect = new TextureRect
		{
			CustomMinimumSize = new Vector2(0, 120),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		layout.AddChild(_thumbnailRect);

		_nameLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
			MaxLinesVisible = 2,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_nameLabel.AddThemeFontSizeOverride("font_size", 14);
		layout.AddChild(_nameLabel);

		_priceLabel = new Label
		{
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_priceLabel.AddThemeFontSizeOverride("font_size", 14);
		_priceLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.82f, 1f));
		layout.AddChild(_priceLabel);

		Bind();

		Pressed += () => CardPressed?.Invoke(ItemData.Id);
	}

	private void Bind()
	{
		_nameLabel.Text = ItemData.Name;

		if (ItemData.IsLimited)
		{
			_priceLabel.Text = "Limited";
			_priceLabel.AddThemeColorOverride("font_color", new Color(1f, 0.82f, 0.35f));
		}
		else if (ItemData.Price is int price && price > 0)
		{
			_priceLabel.Text = price + " B$";
		}
		else
		{
			_priceLabel.Text = "Free";
		}

		if (!string.IsNullOrEmpty(ItemData.Thumbnail))
		{
			WebAssetLoader.Singleton.GetResource(
				new() { Type = WebResourceType.Image, URL = ItemData.Thumbnail },
				OnThumbnailLoaded
			);
		}
	}

	private void OnThumbnailLoaded(Resource resource)
	{
		// The card may be freed before the async load returns.
		if (!IsInstanceValid(this) || _thumbnailRect == null)
		{
			return;
		}
		_thumbnailRect.Texture = (Texture2D)resource;
	}
}
