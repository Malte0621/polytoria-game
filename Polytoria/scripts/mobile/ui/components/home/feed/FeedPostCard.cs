// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Resources;
using Polytoria.Schemas.API;
using Polytoria.Shared.AssetLoaders;

namespace Polytoria.Mobile.UI;

public partial class FeedPostCard : PanelContainer
{
	[Export] private Label _usernameLabel = null!;
	[Export] private Label _postDateLabel = null!;
	[Export] private Label _locationLabel = null!;
	[Export] private Label _contentLabel = null!;
	[Export] private TextureRect _pfpRect = null!;
	[Export] private TextureRect _mediaRect = null!;
	[Export] private Label _likeLabel = null!;
	[Export] private Label _commentLabel = null!;

	private readonly PTImageAsset _pfpAsset = new();

	public APIFeedPostData Data;

	public override void _Ready()
	{
		_pfpAsset.ResourceLoaded += OnPFPLoaded;
		_pfpAsset.ImageType = ImageTypeEnum.UserAvatarHeadshot;
		_pfpAsset.ImageID = (uint)Data.Author.Id;
		_pfpAsset.LoadResource();
		_usernameLabel.Text = Data.Author.Username;
		_postDateLabel.Text = Data.PostedAt.ToShortDateString();
		_contentLabel.Text = Data.Content;
		_likeLabel.Text = Data.LikeCount.ToString();
		_commentLabel.Text = (Data.Comments?.Length ?? 0).ToString();

		if (Data.PlaceID != null)
		{
			_locationLabel.Visible = true;
			_locationLabel.Text = Data.PlaceName;
		}
		else
		{
			_locationLabel.Visible = false;
		}

		if (Data.MediaUrl != null)
		{
			_mediaRect.Visible = true;
			WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = Data.MediaUrl }, (resource) =>
			{
				if (IsInstanceValid(_mediaRect) && resource is Texture2D tex)
				{
					_mediaRect.Texture = tex;
				}
			});
		}
		else
		{
			_mediaRect.Visible = false;
		}

		// Tapping the author's avatar or username opens their profile.
		MakeAuthorTappable(_pfpRect);
		MakeAuthorTappable(_usernameLabel);
	}

	private void MakeAuthorTappable(Control target)
	{
		target.MouseFilter = MouseFilterEnum.Stop;
		target.GuiInput += OnAuthorInput;
	}

	private void OnAuthorInput(InputEvent @event)
	{
		if (Data.Author.Id == 0)
		{
			return;
		}

		if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
			|| @event is InputEventScreenTouch { Pressed: true })
		{
			MobileUI.Singleton.SwitchTo(MobileViewEnum.Profile, Data.Author.Id);
			AcceptEvent();
		}
	}

	private void OnPFPLoaded(Resource resource)
	{
		if (IsInstanceValid(_pfpRect) && resource is Texture2D tex)
		{
			_pfpRect.Texture = tex;
		}
	}
}
