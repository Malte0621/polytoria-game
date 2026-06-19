// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Resources;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Shared.AssetLoaders;
using Polytoria.Utils;
using System;

namespace Polytoria.Mobile.UI;

public partial class UserHeadshotCard : PanelContainer
{
	[Export] public uint UserID;

	[Export] private TextureRect _imageRect = null!;
	[Export] private Label _usernameLabel = null!;

	private readonly PTImageAsset _iconAsset = new();

	// When set, the card renders straight from this data instead of fetching it,
	// avoiding an extra GetUserFromID round-trip (used by the friends list).
	private bool _hasPresetData;
	private string? _presetUsername;
	private string? _presetIconUrl;

	/// <summary>Populate the card directly from a friend record (no extra API call).</summary>
	public void SetFriendData(APIFriendData friend)
	{
		UserID = (uint)friend.Id;
		_hasPresetData = true;
		_presetUsername = friend.Username;
		_presetIconUrl = friend.AvatarIconUrl;
	}

	public override void _Ready()
	{
		_imageRect.Texture = null;
		_usernameLabel.Text = _presetUsername ?? "";
		_iconAsset.ResourceLoaded += OnIconLoaded;

		// Tap anywhere on the card to open the user's profile. The root catches the
		// input; let the headshot pass taps through so it isn't swallowed.
		MouseFilter = MouseFilterEnum.Stop;
		_imageRect.MouseFilter = MouseFilterEnum.Pass;
		_usernameLabel.MouseFilter = MouseFilterEnum.Pass;
		GuiInput += OnGuiInput;

		LoadUserCard();
	}

	private void OnGuiInput(InputEvent @event)
	{
		if (UserID == 0)
		{
			return;
		}

		if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
			|| @event is InputEventScreenTouch { Pressed: true })
		{
			MobileUI.Singleton.SwitchTo(MobileViewEnum.Profile, (int)UserID);
			AcceptEvent();
		}
	}

	private void OnIconLoaded(Resource resource)
	{
		if (IsInstanceValid(_imageRect) && resource is Texture2D tex)
		{
			_imageRect.Texture = tex;
		}
	}

	public async void LoadUserCard()
	{
		// Prefer the direct icon URL when we already have it (friends payload),
		// otherwise fall back to the Polytoria asset service via PTImageAsset.
		if (_hasPresetData && !string.IsNullOrEmpty(_presetIconUrl))
		{
			LoadIconFromUrl(_presetIconUrl!);
		}
		else
		{
			_iconAsset.ImageType = ImageTypeEnum.UserAvatarHeadshot;
			_iconAsset.ImageID = UserID;
			_iconAsset.LoadResource();
		}

		if (_hasPresetData)
		{
			_usernameLabel.Text = _presetUsername ?? "";
			return;
		}

		try
		{
			APIUserInfo userData = await PolyAPI.GetUserFromID((int)UserID);

			_usernameLabel.Text = userData.Username;
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}
	}

	private void LoadIconFromUrl(string url)
	{
		try
		{
			WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = url }, (resource) =>
			{
				if (IsInstanceValid(_imageRect) && resource is Texture2D tex)
				{
					_imageRect.Texture = tex;
				}
			});
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			// Fall back to the asset service if the direct URL fails.
			_iconAsset.ImageType = ImageTypeEnum.UserAvatarHeadshot;
			_iconAsset.ImageID = UserID;
			_iconAsset.LoadResource();
		}
	}
}
