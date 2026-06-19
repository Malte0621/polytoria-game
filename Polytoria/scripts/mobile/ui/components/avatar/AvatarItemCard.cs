// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Resources;
using Polytoria.Schemas.API;
using System;

namespace Polytoria.Mobile.UI;

/// <summary>
/// A single catalog/inventory item in the avatar customizer grid. Displays the
/// item's thumbnail + name and visually marks itself as equipped. Tapping the
/// card raises <see cref="Toggled"/> so the owning view can update the equipped
/// set.
/// </summary>
public partial class AvatarItemCard : Button
{
	[Export] private TextureRect _iconRect = null!;
	[Export] private Label _nameLabel = null!;
	[Export] private Control _equippedMarker = null!;

	public APIStoreItem ItemData;

	/// <summary>Raised when the card is tapped. Argument is this card.</summary>
	public event Action<AvatarItemCard>? CardTapped;

	private bool _equipped;
	public bool Equipped
	{
		get => _equipped;
		set
		{
			_equipped = value;
			if (_equippedMarker != null)
			{
				_equippedMarker.Visible = value;
			}
		}
	}

	private readonly PTImageAsset _iconAsset = new();

	public override void _Ready()
	{
		_iconAsset.ResourceLoaded += OnIconLoaded;

		_nameLabel.Text = ItemData.Name;
		_equippedMarker.Visible = _equipped;

		_iconAsset.ImageType = ImageTypeEnum.AssetThumbnail;
		_iconAsset.ImageID = (uint)ItemData.Id;
		_iconAsset.LoadResource();

		Pressed += OnPressed;
	}

	private void OnPressed()
	{
		CardTapped?.Invoke(this);
	}

	private void OnIconLoaded(Resource tex)
	{
		if (!IsInstanceValid(_iconRect))
		{
			return;
		}
		if (tex is Texture2D texture)
		{
			_iconRect.Texture = texture;
		}
	}
}
