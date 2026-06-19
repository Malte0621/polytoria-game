// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Shared.AssetLoaders;
using Polytoria.Utils;
using System;

namespace Polytoria.Mobile.UI;

public partial class ViewPlaceInfo : MobileViewBase
{
	// Header (kept from the scene).
	[Export] private Button _playButton = null!;
	[Export] private Label _genreLabel = null!;
	[Export] private Label _placeNameLabel = null!;
	[Export] private Label _creatorNameLabel = null!;
	[Export] private TextureRect _thumbnailRect = null!;
	[Export] private Control _thumbnailGradient = null!;

	// Dynamic detail sections are (re)built here in code on every ShowView.
	private VBoxContainer _detailsContainer = null!;

	private int _worldID;
	private int _loadGeneration;
	private APIPlaceInfo _placeInfo;

	// Rating widgets (kept so a vote can update them in place).
	private Button _likeButton = null!;
	private Button _dislikeButton = null!;
	private ColorRect _ratioGreen = null!;
	private ColorRect _ratioRed = null!;
	private Label _percentLabel = null!;
	private string _userVote = "none";
	private bool _voting;

	private static readonly Color Green = new(0.30f, 0.78f, 0.40f);
	private static readonly Color Red = new(0.88f, 0.35f, 0.35f);
	private static readonly Color Muted = new(1, 1, 1, 0.55f);

	public override void _Ready()
	{
		_playButton.Pressed += OnPlayButtonPressed;

		// Host the dynamic sections beneath the existing name/genre/creator block.
		Control content = GetNode<Control>("ScrollContainer/VBoxContainer/PanelContainer/Layout");
		_detailsContainer = new VBoxContainer();
		_detailsContainer.AddThemeConstantOverride("separation", 18);
		content.AddChild(_detailsContainer);
	}

	private void OnPlayButtonPressed()
	{
		MobileUI.Singleton.LaunchGame(_worldID);
	}

	public override async void ShowView(object? args)
	{
		base.ShowView(args);

		if (args is not int worldID)
		{
			return;
		}

		_worldID = worldID;
		int generation = ++_loadGeneration;

		_genreLabel.Text = "";
		_placeNameLabel.Text = "";
		_creatorNameLabel.Text = "";
		_thumbnailRect.Texture = null;
		_userVote = "none";
		ClearDetails();

		MobileUI.Singleton.LoadingScreen.ShowScreen();
		try
		{
			_placeInfo = await PolyAPI.GetWorldFromID(_worldID);
			if (generation != _loadGeneration)
			{
				return;
			}

			_genreLabel.Text = _placeInfo.Genre;
			_placeNameLabel.Text = _placeInfo.Name;
			_creatorNameLabel.Text = "By " + (_placeInfo.Creator.Name ?? "Unknown");

			LoadThumbnail(_placeInfo.Thumbnail, generation);

			BuildRatingSection(_placeInfo.Rating);
			BuildStatsSection(_placeInfo);
			BuildDescriptionSection(_placeInfo.Description);
			LoadScreenshots(_worldID, generation);
			LoadServers(_worldID, generation);
			LoadComments(_worldID, generation);
			LoadAchievements(_worldID, generation);

			// Clear the fixed Play button so the last sections aren't hidden behind it.
			_detailsContainer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 96) });
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			MobileUI.Singleton.ShowToast("Couldn't load this place. Please try again.");
		}
		finally
		{
			if (generation == _loadGeneration)
			{
				MobileUI.Singleton.LoadingScreen.HideScreen();
			}
		}
	}

	private void ClearDetails()
	{
		foreach (Node child in _detailsContainer.GetChildren())
		{
			child.QueueFree();
		}
	}

	// ---------------------------------------------------------------------------------
	// Rating + like / dislike
	// ---------------------------------------------------------------------------------

	private void BuildRatingSection(APIPlaceRating rating)
	{
		VBoxContainer root = new();
		root.AddThemeConstantOverride("separation", 8);

		HBoxContainer buttons = new();
		buttons.AddThemeConstantOverride("separation", 10);

		_likeButton = MakeChip("Like");
		_likeButton.Pressed += () => OnVote("like");
		buttons.AddChild(_likeButton);

		_dislikeButton = MakeChip("Dislike");
		_dislikeButton.Pressed += () => OnVote("dislike");
		buttons.AddChild(_dislikeButton);

		buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

		_percentLabel = new Label { SizeFlagsVertical = SizeFlags.ShrinkCenter };
		_percentLabel.AddThemeFontSizeOverride("font_size", 16);
		buttons.AddChild(_percentLabel);
		root.AddChild(buttons);

		// Two-colour ratio bar.
		HBoxContainer bar = new() { CustomMinimumSize = new Vector2(0, 6) };
		bar.AddThemeConstantOverride("separation", 0);
		_ratioGreen = new ColorRect { Color = Green, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_ratioRed = new ColorRect { Color = Red, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		bar.AddChild(_ratioGreen);
		bar.AddChild(_ratioRed);
		root.AddChild(bar);

		_detailsContainer.AddChild(root);

		UpdateRatingUI(rating.Likes, rating.Dislikes, rating.Percent, _userVote);
	}

	private void UpdateRatingUI(int likes, int dislikes, string? percent, string userVote)
	{
		_userVote = userVote ?? "none";

		_likeButton.Text = "Like  " + likes;
		_dislikeButton.Text = "Dislike  " + dislikes;
		_likeButton.AddThemeColorOverride("font_color", _userVote == "like" ? Green : Muted);
		_dislikeButton.AddThemeColorOverride("font_color", _userVote == "dislike" ? Red : Muted);

		_percentLabel.Text = string.IsNullOrEmpty(percent)
			? (likes + dislikes == 0 ? "No ratings" : "")
			: percent + (percent.EndsWith("%") ? "" : "%");

		int total = likes + dislikes;
		if (total == 0)
		{
			_ratioGreen.Color = new Color(1, 1, 1, 0.18f);
			_ratioGreen.SizeFlagsStretchRatio = 1;
			_ratioRed.SizeFlagsStretchRatio = 0;
		}
		else
		{
			_ratioGreen.Color = Green;
			_ratioGreen.SizeFlagsStretchRatio = likes;
			_ratioRed.SizeFlagsStretchRatio = dislikes;
		}
	}

	private async void OnVote(string type)
	{
		if (_voting)
		{
			return;
		}
		// Tapping the active vote again clears it.
		string newVote = _userVote == type ? "none" : type;
		_voting = true;
		try
		{
			APIVoteResponse res = await PolyAPI.VotePlace(_worldID, newVote);
			if (!IsInstanceValid(_likeButton))
			{
				return;
			}
			UpdateRatingUI(res.Likes, res.Dislikes, res.Percent, res.UserVote ?? newVote);
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			MobileUI.Singleton.ShowToast("Couldn't submit your rating.");
		}
		finally
		{
			_voting = false;
		}
	}

	// ---------------------------------------------------------------------------------
	// Stats + description
	// ---------------------------------------------------------------------------------

	private void BuildStatsSection(APIPlaceInfo info)
	{
		GridContainer grid = new() { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 24);
		grid.AddThemeConstantOverride("v_separation", 8);

		AddStat(grid, "Playing", info.Playing.ToString("N0"));
		AddStat(grid, "Visits", info.Visits.ToString("N0"));
		AddStat(grid, "Max Players", info.MaxPlayers.ToString());
		AddStat(grid, "Created", info.CreatedAt.ToString("d MMM yyyy"));

		_detailsContainer.AddChild(grid);
	}

	private void AddStat(GridContainer grid, string label, string value)
	{
		Label name = new() { Text = label };
		name.AddThemeColorOverride("font_color", Muted);
		grid.AddChild(name);

		Label val = new() { Text = value, HorizontalAlignment = HorizontalAlignment.Right, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		grid.AddChild(val);
	}

	private void BuildDescriptionSection(string? description)
	{
		if (string.IsNullOrWhiteSpace(description))
		{
			return;
		}

		VBoxContainer root = new();
		root.AddThemeConstantOverride("separation", 6);
		root.AddChild(MakeHeader("Description"));

		Label body = new()
		{
			Text = description,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		body.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.8f));
		root.AddChild(body);
		_detailsContainer.AddChild(root);
	}

	// ---------------------------------------------------------------------------------
	// Screenshots
	// ---------------------------------------------------------------------------------

	private async void LoadScreenshots(int placeID, int generation)
	{
		VBoxContainer section = AddSection("Screenshots", out _, hideUntilFilled: true);

		ScrollContainer scroll = new()
		{
			CustomMinimumSize = new Vector2(0, 150),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
			VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		HBoxContainer strip = new();
		strip.AddThemeConstantOverride("separation", 8);
		scroll.AddChild(strip);
		section.AddChild(scroll);

		try
		{
			APIPlaceMedia[]? media = await PolyAPI.GetWorldMedia(placeID);
			if (generation != _loadGeneration || !IsInstanceValid(strip))
			{
				return;
			}

			int count = 0;
			if (media != null)
			{
				foreach (APIPlaceMedia item in media)
				{
					if (string.IsNullOrEmpty(item.Url))
					{
						continue;
					}
					TextureRect shot = new()
					{
						CustomMinimumSize = new Vector2(240, 135),
						ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
						StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
						ClipContents = true,
					};
					strip.AddChild(shot);
					LoadWebImage(shot, item.Url);
					count++;
				}
			}

			section.Visible = count > 0;
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			if (IsInstanceValid(section))
			{
				section.Visible = false;
			}
		}
	}

	// ---------------------------------------------------------------------------------
	// Active servers
	// ---------------------------------------------------------------------------------

	private async void LoadServers(int placeID, int generation)
	{
		VBoxContainer section = AddSection("Active Servers", out VBoxContainer content, hideUntilFilled: true);

		try
		{
			APIPlaceServersRoot root = await PolyAPI.GetPlaceServers(placeID);
			if (generation != _loadGeneration || !IsInstanceValid(content))
			{
				return;
			}

			APIPlaceServer[] servers = root.Data ?? Array.Empty<APIPlaceServer>();
			foreach (APIPlaceServer server in servers)
			{
				content.AddChild(MakeServerRow(server, placeID));
			}
			section.Visible = servers.Length > 0;
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			if (IsInstanceValid(section))
			{
				section.Visible = false;
			}
		}
	}

	private Control MakeServerRow(APIPlaceServer server, int placeID)
	{
		PanelContainer card = MakeCard();
		HBoxContainer row = new();
		row.AddThemeConstantOverride("separation", 14);
		card.AddChild(row);

		// Left: player count + a prominent green Join button.
		VBoxContainer left = new() { SizeFlagsVertical = SizeFlags.ShrinkCenter };
		left.AddThemeConstantOverride("separation", 8);

		Label players = new() { Text = $"{server.Playing} / {server.MaxPlayers} players" };
		left.AddChild(players);
		if (!string.IsNullOrEmpty(server.Region))
		{
			Label region = new() { Text = server.Region };
			region.AddThemeFontSizeOverride("font_size", 12);
			region.AddThemeColorOverride("font_color", Muted);
			left.AddChild(region);
		}

		Button join = new() { Text = "Join" };
		join.CustomMinimumSize = new Vector2(130, 42);
		join.AddThemeColorOverride("font_color", new Color(1, 1, 1));
		join.AddThemeStyleboxOverride("normal", JoinStyle(Green));
		join.AddThemeStyleboxOverride("hover", JoinStyle(new Color(Green.R, Green.G, Green.B, 0.85f)));
		join.AddThemeStyleboxOverride("pressed", JoinStyle(new Color(Green.R * 0.85f, Green.G * 0.85f, Green.B * 0.85f)));
		join.AddThemeStyleboxOverride("disabled", JoinStyle(new Color(0.4f, 0.4f, 0.4f, 0.5f)));
		bool full = server.MaxPlayers > 0 && server.Playing >= server.MaxPlayers;
		join.Disabled = full;
		string serverId = server.Id;
		join.Pressed += () => MobileUI.Singleton.LaunchGame(placeID, serverId);
		left.AddChild(join);

		row.AddChild(left);

		// Right: avatars of players currently in this server (tooltip = username).
		HBoxContainer avatars = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			ClipContents = true,
		};
		avatars.AddThemeConstantOverride("separation", 4);
		if (server.Players != null)
		{
			int shown = 0;
			foreach (APIServerPlayer player in server.Players)
			{
				if (shown >= 8)
				{
					break;
				}
				TextureRect pfp = new()
				{
					CustomMinimumSize = new Vector2(42, 42),
					ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
					StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
					TooltipText = player.Username,
					ClipContents = true,
				};
				avatars.AddChild(pfp);
				LoadWebImage(pfp, player.Thumbnail);
				shown++;
			}
		}
		row.AddChild(avatars);

		return card;
	}

	private static StyleBoxFlat JoinStyle(Color color)
	{
		return new StyleBoxFlat
		{
			BgColor = color,
			CornerRadiusTopLeft = 8,
			CornerRadiusTopRight = 8,
			CornerRadiusBottomLeft = 8,
			CornerRadiusBottomRight = 8,
		};
	}

	private static void SetSectionTitle(VBoxContainer section, string title)
	{
		if (section.GetChildCount() > 0 && section.GetChild(0) is Label header)
		{
			header.Text = title;
		}
	}

	// ---------------------------------------------------------------------------------
	// Comments
	// ---------------------------------------------------------------------------------

	private async void LoadComments(int placeID, int generation)
	{
		VBoxContainer section = AddSection("Comments", out VBoxContainer content, hideUntilFilled: true);

		try
		{
			APIPlaceCommentsRoot root = await PolyAPI.GetPlaceComments(placeID);
			if (generation != _loadGeneration || !IsInstanceValid(content))
			{
				return;
			}

			APIPlaceComment[] comments = root.Data ?? Array.Empty<APIPlaceComment>();
			foreach (APIPlaceComment comment in comments)
			{
				content.AddChild(MakeCommentRow(comment));
			}
			section.Visible = comments.Length > 0;
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			if (IsInstanceValid(section))
			{
				section.Visible = false;
			}
		}
	}

	private Control MakeCommentRow(APIPlaceComment comment)
	{
		PanelContainer card = MakeCard();
		HBoxContainer row = new();
		row.AddThemeConstantOverride("separation", 10);
		card.AddChild(row);

		TextureRect pfp = new()
		{
			CustomMinimumSize = new Vector2(36, 36),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			SizeFlagsVertical = SizeFlags.ShrinkBegin,
		};
		row.AddChild(pfp);
		LoadWebImage(pfp, comment.Author.AvatarIconUrl);

		VBoxContainer body = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 2);

		Label header = new() { Text = comment.Author.Username };
		header.AddThemeColorOverride("font_color", new Color(0.6f, 0.78f, 1f));
		body.AddChild(header);

		Label text = new()
		{
			Text = comment.Content,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		body.AddChild(text);

		Label date = new() { Text = comment.PostedAt.ToString("d MMM yyyy") };
		date.AddThemeFontSizeOverride("font_size", 12);
		date.AddThemeColorOverride("font_color", Muted);
		body.AddChild(date);

		row.AddChild(body);
		return card;
	}

	// ---------------------------------------------------------------------------------
	// Achievements
	// ---------------------------------------------------------------------------------

	private async void LoadAchievements(int placeID, int generation)
	{
		VBoxContainer section = AddSection("Achievements", out VBoxContainer content, hideUntilFilled: true);

		try
		{
			APIAchievementsRoot root = await PolyAPI.GetPlaceAchievements(placeID);
			if (generation != _loadGeneration || !IsInstanceValid(content))
			{
				return;
			}

			APIAchievement[] achievements = root.Achievements ?? Array.Empty<APIAchievement>();
			int earned = 0;
			foreach (APIAchievement achievement in achievements)
			{
				if (achievement.AwardedAt.HasValue)
				{
					earned++;
				}
				content.AddChild(MakeAchievementRow(achievement));
			}

			if (achievements.Length > 0)
			{
				SetSectionTitle(section, $"Achievements {earned}/{achievements.Length}");
				section.Visible = true;
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			if (IsInstanceValid(section))
			{
				section.Visible = false;
			}
		}
	}

	private Control MakeAchievementRow(APIAchievement achievement)
	{
		APIAchievementAsset asset = achievement.Asset;
		PanelContainer card = MakeCard();
		HBoxContainer row = new();
		row.AddThemeConstantOverride("separation", 12);
		card.AddChild(row);

		TextureRect icon = new()
		{
			CustomMinimumSize = new Vector2(56, 56),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			ClipContents = true,
		};
		row.AddChild(icon);
		LoadWebImage(icon, asset.Thumbnail);

		VBoxContainer body = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 2);

		Label name = new() { Text = asset.Name };
		name.AddThemeFontSizeOverride("font_size", 16);
		body.AddChild(name);

		if (!string.IsNullOrWhiteSpace(asset.Description))
		{
			Label desc = new()
			{
				Text = asset.Description,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
			};
			desc.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.8f));
			body.AddChild(desc);
		}

		Label owners = new() { Text = $"Owned by {asset.Owners:N0} players" };
		owners.AddThemeFontSizeOverride("font_size", 12);
		owners.AddThemeColorOverride("font_color", Muted);
		body.AddChild(owners);

		if (achievement.AwardedAt.HasValue)
		{
			Label earned = new() { Text = "You earned this on " + achievement.AwardedAt.Value.ToString("d MMM yyyy") };
			earned.AddThemeFontSizeOverride("font_size", 12);
			earned.AddThemeColorOverride("font_color", Green);
			body.AddChild(earned);
		}

		row.AddChild(body);
		return card;
	}

	// ---------------------------------------------------------------------------------
	// Shared helpers
	// ---------------------------------------------------------------------------------

	private void LoadThumbnail(string? url, int generation)
	{
		if (string.IsNullOrEmpty(url))
		{
			return;
		}
		WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = url }, (resource) =>
		{
			if (generation != _loadGeneration || !IsInstanceValid(_thumbnailRect))
			{
				return;
			}
			if (resource is Texture2D tex)
			{
				_thumbnailRect.Texture = tex;
			}
		});
	}

	private void LoadWebImage(TextureRect rect, string? url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return;
		}
		WebAssetLoader.Singleton.GetResource(new() { Type = WebResourceType.Image, URL = url }, (resource) =>
		{
			if (IsInstanceValid(rect) && resource is Texture2D tex)
			{
				rect.Texture = tex;
			}
		});
	}

	private VBoxContainer AddSection(string title, out VBoxContainer content, bool hideUntilFilled)
	{
		VBoxContainer root = new() { Visible = !hideUntilFilled };
		root.AddThemeConstantOverride("separation", 8);
		root.AddChild(MakeHeader(title));

		content = new VBoxContainer();
		content.AddThemeConstantOverride("separation", 8);
		root.AddChild(content);

		_detailsContainer.AddChild(root);
		return root;
	}

	private static Label MakeHeader(string title)
	{
		Label header = new() { Text = title };
		header.AddThemeFontSizeOverride("font_size", 20);
		return header;
	}

	private static Button MakeChip(string text)
	{
		Button chip = new() { Text = text };
		chip.CustomMinimumSize = new Vector2(0, 38);
		chip.AddThemeColorOverride("font_color", Muted);
		return chip;
	}

	private static PanelContainer MakeCard()
	{
		PanelContainer card = new();
		StyleBoxFlat style = new()
		{
			BgColor = new Color(1, 1, 1, 0.06f),
			CornerRadiusTopLeft = 8,
			CornerRadiusTopRight = 8,
			CornerRadiusBottomLeft = 8,
			CornerRadiusBottomRight = 8,
			ContentMarginLeft = 12,
			ContentMarginRight = 12,
			ContentMarginTop = 10,
			ContentMarginBottom = 10,
		};
		card.AddThemeStyleboxOverride("panel", style);
		return card;
	}
}
