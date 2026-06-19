// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Mobile.Utils;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;

namespace Polytoria.Mobile.UI;

public partial class ViewHomePage : MobileViewBase
{
	private const string UserHeadshotCardPath = "res://scenes/mobile/components/home/user_headshot_card.tscn";
	private const string PlaceCardPath = "res://scenes/mobile/components/shared/place_card.tscn";

	[Export] private Label _usernameLabel = null!;

	// Outer scroller used to drive feed infinite-scroll.
	[Export] private ScrollContainer _scrollContainer = null!;

	// Friends section.
	[Export] private Control _friendsSection = null!;
	[Export] private Label _friendsCountLabel = null!;
	[Export] private Control _friendsContainer = null!;

	// Continue Playing section.
	[Export] private Control _continueSection = null!;
	[Export] private Control _continueContainer = null!;

	// Feed (drives pagination on scroll).
	[Export] private FeedRoot _feedRoot = null!;

	// Pixels from the bottom of the scroll range at which we trigger the next feed page.
	private const float FeedLoadThreshold = 400.0f;

	private bool _dataLoaded;

	public override void _Ready()
	{
		if (_scrollContainer != null)
		{
			VScrollBar vbar = _scrollContainer.GetVScrollBar();
			if (vbar != null)
			{
				vbar.ValueChanged += OnScrolled;
			}
		}
	}

	public override void _EnterTree()
	{
		PolyMobileAuthAPI.UserAuthenticated += OnUserAuthenticated;
		base._EnterTree();
	}

	public override void _ExitTree()
	{
		PolyMobileAuthAPI.UserAuthenticated -= OnUserAuthenticated;
		base._ExitTree();
	}

	private void OnUserAuthenticated(APIMeResponse response)
	{
		LoadView();
	}

	public override void ShowView(object? args)
	{
		base.ShowView(args);

		// CurrentUserInfo is a default struct (Id == 0) until auth completes; the
		// UserAuthenticated handler will (re)load the view once it does.
		if (PolyMobileAuthAPI.CurrentUserInfo.Id != 0 && !_dataLoaded)
		{
			LoadView();
		}
	}

	private void LoadView()
	{
		_dataLoaded = true;

		SetUsername();
		LoadFriends();
		LoadContinuePlaying();
	}

	private void SetUsername()
	{
		try
		{
			if (_usernameLabel != null)
			{
				_usernameLabel.Text = PolyMobileAuthAPI.CurrentUserInfo.Username;
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}
	}

	private async void LoadFriends()
	{
		if (_friendsContainer == null)
		{
			return;
		}

		try
		{
			int userID = PolyMobileAuthAPI.CurrentUserInfo.Id;
			APIFriendsRoot root = await PolyAPI.GetUserFriends(userID);

			// Clear any existing/placeholder cards.
			foreach (Node child in _friendsContainer.GetChildren())
			{
				child.QueueFree();
			}

			APIFriendData[] friends = root.Data ?? Array.Empty<APIFriendData>();

			if (friends.Length == 0)
			{
				if (_friendsSection != null)
				{
					_friendsSection.Visible = false;
				}
				return;
			}

			if (_friendsSection != null)
			{
				_friendsSection.Visible = true;
			}

			if (_friendsCountLabel != null)
			{
				_friendsCountLabel.Text = $"Your Friends ({root.Meta.Total})";
			}

			foreach (APIFriendData friend in friends)
			{
				UserHeadshotCard card = Globals.CreateInstanceFromScene<UserHeadshotCard>(UserHeadshotCardPath);
				card.SetFriendData(friend);
				_friendsContainer.AddChild(card);
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			MobileUI.Singleton.ShowToast("Couldn't load your friends.");
			if (_friendsSection != null)
			{
				_friendsSection.Visible = false;
			}
		}
	}

	private async void LoadContinuePlaying()
	{
		if (_continueContainer == null)
		{
			return;
		}

		try
		{
			APIWorldsRoot root = await PolyAPI.GetRecentlyPlayed();

			foreach (Node child in _continueContainer.GetChildren())
			{
				child.QueueFree();
			}

			APIWorldsData[] places = root.Data ?? Array.Empty<APIWorldsData>();

			if (places.Length == 0)
			{
				if (_continueSection != null)
				{
					_continueSection.Visible = false;
				}
				return;
			}

			if (_continueSection != null)
			{
				_continueSection.Visible = true;
			}

			PackedScene placeCardPacked = GD.Load<PackedScene>(PlaceCardPath);
			foreach (APIWorldsData place in places)
			{
				PlaceCard card = placeCardPacked.Instantiate<PlaceCard>();
				card.PlaceData = place;
				_continueContainer.AddChild(card);
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			MobileUI.Singleton.ShowToast("Couldn't load recently played places.");
			if (_continueSection != null)
			{
				_continueSection.Visible = false;
			}
		}
	}

	private void OnScrolled(double value)
	{
		if (_scrollContainer == null || _feedRoot == null)
		{
			return;
		}

		if (_feedRoot.IsLoading || !_feedRoot.HasMorePages)
		{
			return;
		}

		VScrollBar vbar = _scrollContainer.GetVScrollBar();
		if (vbar == null)
		{
			return;
		}

		// MaxValue includes the page (visible) size; subtract it to get the true bottom.
		double bottom = vbar.MaxValue - vbar.Page;
		if (value >= bottom - FeedLoadThreshold)
		{
			_feedRoot.LoadNextPage();
		}
	}
}
