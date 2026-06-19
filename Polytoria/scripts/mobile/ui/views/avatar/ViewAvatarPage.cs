// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Resources;
using Polytoria.Mobile.Utils;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Polytoria.Mobile.UI;

public partial class ViewAvatarPage : MobileViewBase
{
	private const string ItemCardPath = "res://scenes/mobile/components/avatar/avatar_item_card.tscn";

	[Export] private GridContainer _grid = null!;
	[Export] private TextureRect _previewRect = null!;
	[Export] private Button _saveButton = null!;

	[Export] private Button _allButton = null!;
	[Export] private Button _accessoriesButton = null!;
	[Export] private Button _shirtsButton = null!;
	[Export] private Button _pantsButton = null!;
	[Export] private Button _toolsButton = null!;

	private PackedScene _itemCardPacked = null!;
	private readonly PTImageAsset _previewAsset = new();

	// Currently equipped asset IDs. Seeded from the user's saved avatar and
	// mutated locally as the user taps items; persisted on Save.
	private readonly HashSet<int> _equippedIds = [];

	// Body colors of the user's saved avatar. We don't expose a colour editor
	// yet, so we round-trip whatever the server has on Save.
	private APIAvatarBodyColors _currentColors;

	// Maps a filter tab to the store item type string passed to the catalog
	// endpoint. NOTE(backend): these type strings are ASSUMED to match the
	// catalog's filtering taxonomy (see assumedEndpoints in the slice report).
	// "All" loads everything (null filter).
	private string? _currentFilter;

	private bool _isLoading;
	private bool _avatarLoaded;

	public override void _Ready()
	{
		_itemCardPacked = GD.Load<PackedScene>(ItemCardPath);

		_saveButton.Pressed += OnSavePressed;

		_allButton.Pressed += () => OnFilterSelected(null);
		_accessoriesButton.Pressed += () => OnFilterSelected("accessory");
		_shirtsButton.Pressed += () => OnFilterSelected("shirt");
		_pantsButton.Pressed += () => OnFilterSelected("pants");
		_toolsButton.Pressed += () => OnFilterSelected("tool");

		_previewAsset.ResourceLoaded += OnPreviewLoaded;
	}

	public override void ShowView(object? args)
	{
		base.ShowView(args);
		StrikeAPose();

		// (Re)load the signed-in user's avatar + current catalog filter every
		// time the page is shown so it reflects the latest saved state.
		LoadAvatar();
		LoadItems(_currentFilter);
	}

	private static void StrikeAPose()
	{
		// PolytorianModel-driven posing is intentionally not used here.
		//
		// LIMITATION: Rendering a live PolytorianModel into the SubViewport
		// requires the datamodel framework (New<>()/Root/Insert), which is only
		// available inside a running World/network context. Standalone rendering
		// from the mobile UI is unreliable, so we fall back to the user's
		// pre-rendered avatar thumbnail (see LoadAvatar). The SubViewport in the
		// scene is left in place for a future live-preview implementation.
	}

	/// <summary>
	/// Loads the signed-in user's saved avatar: seeds the equipped set + colours
	/// and renders the avatar thumbnail into the preview.
	/// </summary>
	private async void LoadAvatar()
	{
		int userId = PolyMobileAuthAPI.CurrentUserInfo.Id;
		if (userId == 0)
		{
			// Not signed in / not loaded yet. Nothing to preview.
			return;
		}

		// Preview: pre-rendered avatar thumbnail (fallback for live 3D preview).
		_previewAsset.ImageType = ImageTypeEnum.UserAvatar;
		_previewAsset.ImageID = (uint)userId;
		_previewAsset.LoadResource();

		try
		{
			APIAvatarResponse avatar = await PolyAPI.GetUserAvatarFromID(userId);

			_currentColors = avatar.Colors;

			_equippedIds.Clear();
			if (avatar.Assets != null)
			{
				foreach (APIAvatarAsset asset in avatar.Assets)
				{
					_equippedIds.Add(asset.ID);
				}
			}

			MarkEquippedCards();
			_avatarLoaded = true;
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			MobileUI.Singleton.ShowToast("Couldn't load your avatar. Please try again.");
		}
	}

	private void OnFilterSelected(string? type)
	{
		_currentFilter = type;
		LoadItems(type);
	}

	/// <summary>
	/// Loads the first page of catalog items for the given type filter and
	/// rebuilds the grid. Owned-only browsing can be swapped in via
	/// PolyAPI.GetUserInventory (see notes).
	/// </summary>
	private async void LoadItems(string? type)
	{
		if (_isLoading)
		{
			return;
		}
		_isLoading = true;

		ClearGrid();
		MobileUI.Singleton.LoadingScreen.ShowScreen();

		try
		{
			// NOTE(backend): catalog listing endpoint is ASSUMED. First page
			// only for now; pagination can be layered on via root.Meta
			// (CurrentPage/LastPage) + a "load more" trigger.
			APIStoreRoot root = await PolyAPI.GetStoreItems(type, 1);

			if (root.Data != null)
			{
				foreach (APIStoreItem item in root.Data)
				{
					AvatarItemCard card = _itemCardPacked.Instantiate<AvatarItemCard>();
					card.ItemData = item;
					card.Equipped = _equippedIds.Contains(item.Id);
					card.CardTapped += OnItemToggled;
					_grid.AddChild(card);
				}
			}
			// Empty result simply leaves an empty grid (graceful empty state).
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			MobileUI.Singleton.ShowToast("Couldn't load items. Please try again.");
		}

		MobileUI.Singleton.LoadingScreen.HideScreen();
		_isLoading = false;
	}

	private void OnItemToggled(AvatarItemCard card)
	{
		int id = card.ItemData.Id;
		if (_equippedIds.Contains(id))
		{
			_equippedIds.Remove(id);
			card.Equipped = false;
		}
		else
		{
			_equippedIds.Add(id);
			card.Equipped = true;
		}
	}

	private void MarkEquippedCards()
	{
		foreach (Node child in _grid.GetChildren())
		{
			if (child is AvatarItemCard card)
			{
				card.Equipped = _equippedIds.Contains(card.ItemData.Id);
			}
		}
	}

	private void ClearGrid()
	{
		foreach (Node child in _grid.GetChildren())
		{
			if (child is AvatarItemCard card)
			{
				card.CardTapped -= OnItemToggled;
			}
			child.QueueFree();
		}
	}

	private async void OnSavePressed()
	{
		if (!_avatarLoaded)
		{
			MobileUI.Singleton.ShowToast("Still loading your avatar, please wait.");
			return;
		}

		_saveButton.Disabled = true;
		MobileUI.Singleton.LoadingScreen.ShowScreen();

		try
		{
			// NOTE(backend): avatar save endpoint is ASSUMED.
			APIAvatarResponse saved = await PolyAPI.SaveAvatar(new APIAvatarSaveRequest
			{
				Assets = _equippedIds.ToArray(),
				Colors = _currentColors,
			});

			_currentColors = saved.Colors;

			_equippedIds.Clear();
			if (saved.Assets != null)
			{
				foreach (APIAvatarAsset asset in saved.Assets)
				{
					_equippedIds.Add(asset.ID);
				}
			}
			MarkEquippedCards();

			// Re-render the preview. We re-fetch the user's avatar thumbnail
			// rather than driving a live 3D update: LoadAppearance only takes a
			// userID and the live SubViewport preview is not wired up (see
			// StrikeAPose limitation), so reflecting the change post-Save is the
			// supported behaviour.
			RefreshPreview();

			MobileUI.Singleton.ShowToast("Avatar saved!");
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			MobileUI.Singleton.ShowToast("Couldn't save your avatar. Please try again.");
		}

		MobileUI.Singleton.LoadingScreen.HideScreen();
		_saveButton.Disabled = false;
	}

	private void RefreshPreview()
	{
		int userId = PolyMobileAuthAPI.CurrentUserInfo.Id;
		if (userId == 0)
		{
			return;
		}

		// Clear so the stale thumbnail doesn't linger if the reload is slow, then
		// re-request. The avatar thumbnail service regenerates from the freshly
		// saved appearance.
		_previewRect.Texture = null;
		_previewAsset.ImageType = ImageTypeEnum.UserAvatar;
		_previewAsset.ImageID = (uint)userId;
		_previewAsset.LoadResource();
	}

	private void OnPreviewLoaded(Resource tex)
	{
		_previewRect.Texture = (Texture2D)tex;
	}
}
