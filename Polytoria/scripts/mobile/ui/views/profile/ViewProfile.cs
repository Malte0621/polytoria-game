// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel.Resources;
using Polytoria.Mobile;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;

namespace Polytoria.Mobile.UI;

// Native Profile page. Reached via SwitchTo(MobileViewEnum.Profile, userId)
// where the arg is an int userID. Builds a header (headshot + username) and a
// scrollable body (bio + stats) entirely in code.
public partial class ViewProfile : MobileViewBase
{
	private TextureRect _headshotRect = null!;
	private Label _usernameLabel = null!;
	private Label _bioLabel = null!;
	private GridContainer _statsGrid = null!;

	private readonly PTImageAsset _headshotAsset = new();

	private int _userID;
	private int _loadGeneration;

	public override void _Ready()
	{
		_headshotAsset.ResourceLoaded += OnHeadshotLoaded;
		BuildLayout();
	}

	private void BuildLayout()
	{
		ScrollContainer scroll = new();
		scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(scroll);

		MarginContainer margin = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_top", 16);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_bottom", 16);
		scroll.AddChild(margin);

		VBoxContainer column = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		column.AddThemeConstantOverride("separation", 16);
		margin.AddChild(column);

		// Header: headshot + username.
		HBoxContainer header = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		header.AddThemeConstantOverride("separation", 12);
		column.AddChild(header);

		_headshotRect = new TextureRect
		{
			CustomMinimumSize = new Vector2(96, 96),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		};
		header.AddChild(_headshotRect);

		_usernameLabel = new Label
		{
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		_usernameLabel.AddThemeFontSizeOverride("font_size", 24);
		header.AddChild(_usernameLabel);

		// Bio.
		_bioLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		_bioLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.7f));
		column.AddChild(_bioLabel);

		// Stats.
		_statsGrid = new GridContainer
		{
			Columns = 2,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		_statsGrid.AddThemeConstantOverride("h_separation", 24);
		_statsGrid.AddThemeConstantOverride("v_separation", 8);
		column.AddChild(_statsGrid);
	}

	public override async void ShowView(object? args)
	{
		base.ShowView(args);

		if (args is not int userID || userID <= 0)
		{
			PT.PrintWarn("ViewProfile opened without a valid userID arg.");
			ResetContent();
			MobileUI.Singleton.ShowToast("Could not open that profile.");
			return;
		}

		_userID = userID;
		// Guard against overlapping shows from rapid navigation: only the latest wins.
		int generation = ++_loadGeneration;
		ResetContent();

		// Headshot loads independently of the profile request.
		_headshotRect.Texture = null;
		_headshotAsset.ImageType = ImageTypeEnum.UserAvatarHeadshot;
		_headshotAsset.ImageID = (uint)_userID;
		_headshotAsset.LoadResource();

		MobileUI.Singleton.LoadingScreen.ShowScreen();

		try
		{
			APIUserInfo user = await PolyAPI.GetUserFromID(_userID);
			if (generation != _loadGeneration)
			{
				return;
			}

			_usernameLabel.Text = user.Username;
			_bioLabel.Text = string.IsNullOrWhiteSpace(user.Description)
				? "This user has no bio."
				: user.Description;

			ClearStats();
			AddStat("Net Worth", user.NetWorth.ToString());
			AddStat("Place Visits", user.PlaceVisits.ToString());
			AddStat("Profile Views", user.ProfileViews.ToString());
			AddStat("Asset Sales", user.AssetSales.ToString());
			AddStat("Forum Posts", user.ForumPosts.ToString());
		}
		catch (Exception ex)
		{
			if (generation != _loadGeneration)
			{
				return;
			}
			PT.PrintErr(ex);
			MobileUI.Singleton.ShowToast("Could not load this profile. Please try again.");
			_usernameLabel.Text = "Unavailable";
			_bioLabel.Text = "This profile could not be loaded.";
		}
		finally
		{
			if (generation == _loadGeneration)
			{
				MobileUI.Singleton.LoadingScreen.HideScreen();
			}
		}
	}

	private void ResetContent()
	{
		_usernameLabel.Text = "";
		_bioLabel.Text = "";
		ClearStats();
	}

	private void ClearStats()
	{
		foreach (Node child in _statsGrid.GetChildren())
		{
			child.QueueFree();
		}
	}

	private void AddStat(string label, string value)
	{
		Label nameLabel = new()
		{
			Text = label,
		};
		nameLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.5f));
		_statsGrid.AddChild(nameLabel);

		Label valueLabel = new()
		{
			Text = value,
			HorizontalAlignment = HorizontalAlignment.Right,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		_statsGrid.AddChild(valueLabel);
	}

	private void OnHeadshotLoaded(Resource resource)
	{
		if (!IsInstanceValid(this) || _headshotRect == null)
		{
			return;
		}
		_headshotRect.Texture = (Texture2D)resource;
	}
}
