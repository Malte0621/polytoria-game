// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DeepLinkAddon;
using Godot;
using Polytoria.Client;
using Polytoria.Mobile.UI;
using Polytoria.Mobile.Utils;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Web;

namespace Polytoria.Mobile;

public partial class MobileUI : Control
{
	public static MobileUI Singleton { get; private set; } = null!;
	public MobileUI()
	{
		Singleton = this;
	}

	public event Action<MobileViewEnum>? ViewPathSwitched;

	private Control _mainView = null!;
	public MobileViewBase? CurrentViewNode;
	public MobileViewEnum CurrentView;

	[Export] public StartupSplash? StartSplash { get; private set; }
	[Export] public NewUserSplash NewUserSplash = null!;
	[Export] public MobileLoadingScreen LoadingScreen = null!;

	private MobileToast _toast = null!;

	private Deeplink _deepLink = new();
	private readonly Dictionary<MobileViewEnum, MobileViewBase> _viewCache = [];

	public override void _Ready()
	{
		Dictionary<string, string> cmdargs = Globals.ReadCmdArgs();
		cmdargs.TryGetValue("token", out string? mobileToken);
		cmdargs.TryGetValue("code", out string? mobileCode);
		cmdargs.TryGetValue("state", out string? mobileState);

		AddChild(_deepLink, true);

		var initResult = _deepLink.Initialize();

		_deepLink.DeeplinkReceived += OnDeeplinkReceived;

		ApplyContentScale();

		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		try
		{
			_toast = new MobileToast();
			AddChild(_toast);
		}
		catch (Exception ex)
		{
			PT.PrintErr("Toast init failed: ", ex);
		}

		if (StartSplash != null)
		{
			StartSplash!.Visible = true;
		}

		PolyMobileAuthAPI.UserAuthenticated += OnUserAuthenticated;
		PolyMobileAuthAPI.AskForAuthentication += OnAskForAuthentication;

		PolyMobileAuthAPI.SetupClient();
		if (mobileToken != null)
		{
			_ = PolyMobileAuthAPI.LoginWithAuthToken(mobileToken);
		}

		if (mobileCode != null && mobileState != null)
		{
			_ = PolyMobileAuthAPI.LoginWithCodeAndState(mobileCode, mobileState);
		}

		_mainView = GetNode<Control>("Layout/MainView");
		if (Globals.IsMobileBuild)
		{
			DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Portrait);
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
		}

		GetTree().Root.SizeChanged += ApplySafeArea;
		ApplySafeArea();

		if (Globals.IsInGDEditor)
		{
			DisplayServer.WindowSetSize((Vector2I)new Vector2(412, 700));
		}

		SwitchTo(MobileViewEnum.Home);
	}

	private void OnUserAuthenticated(APIMeResponse me)
	{
		HideStartupSplash();
		if (NewUserSplash != null && IsInstanceValid(NewUserSplash))
		{
			NewUserSplash.Visible = false;
		}
	}

	private void OnAskForAuthentication()
	{
		HideStartupSplash();
		if (!Globals.IsInGDEditor)
		{
			NewUserSplash.ShowSplash();
		}
	}

	private void HideStartupSplash()
	{
		StartSplash?.HideSplash();
		StartSplash = null;
	}

	private async void OnDeeplinkReceived(DeeplinkURL url)
	{
		// Handle polytoria://auth link
		if (url.Host == "auth")
		{
			NameValueCollection authQuery = HttpUtility.ParseQueryString(url.Query);
			string code = authQuery.Get("code")!;
			string state = authQuery.Get("state")!;

			LoadingScreen.ShowScreen();
			try
			{
				await PolyMobileAuthAPI.LoginWithCodeAndState(code, state);
			}
			catch (Exception ex)
			{
				PT.PrintErr("Mobile auth failed: ", ex);
				ShowToast("Sign-in failed. Please check your connection and try again.");
			}
			finally
			{
				LoadingScreen.HideScreen();
			}
		}
		// polytoria://client/<placeID> (also "game"/"place") -> jump straight into a world
		else if (url.Host == "client" || url.Host == "game" || url.Host == "place")
		{
			if (int.TryParse(url.Path.Trim('/'), out int placeID))
			{
				LaunchGame(placeID);
			}
			else
			{
				PT.PrintErr("Invalid game deeplink path: ", url.Path);
			}
		}
		// polytoria://user/<userID> -> open a profile
		else if (url.Host == "user")
		{
			if (int.TryParse(url.Path.Trim('/'), out int userID))
			{
				SwitchTo(MobileViewEnum.Profile, userID);
			}
			else
			{
				PT.PrintErr("Invalid user deeplink path: ", url.Path);
			}
		}
	}

	public async void LaunchGame(int placeID, string? serverID = null)
	{
		LoadingScreen.ShowScreen();

		try
		{
			APIJoinPlaceResponse res = await PolyAPI.RequestJoinGame(new() { PlaceID = placeID, IsBeta = Globals.IsBetaBuild, ServerID = serverID });

			Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
			if (app is ClientEntry ce)
			{
				ClientEntry.ClientEntryData entryData = new()
				{
					Token = res.Token
				};
				ce.Entry(entryData);
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr("World join failed: ", ex);
			ShowToast("Couldn't join the place. Please try again.");
		}

		LoadingScreen.HideScreen();
	}

	public void SwitchTo(MobileViewEnum viewEnum, object? args = null)
	{
		// Navbar taps (no args) to the current tab are a no-op, but entity views such as
		// PlaceInfo/Profile must re-show when navigated to with a different argument.
		if (viewEnum == CurrentView && args == null)
		{
			return;
		}

		try
		{
			if (CurrentViewNode != null)
			{
				CurrentViewNode.HideView();
				CurrentViewNode.Visible = false;
			}

			// Check if cached
			if (!_viewCache.TryGetValue(viewEnum, out MobileViewBase? page))
			{
				PT.Print("Loading ", viewEnum);
				string pathToLoad = viewEnum switch
				{
					MobileViewEnum.Home => "res://scenes/mobile/views/home.tscn",
					MobileViewEnum.Worlds => "res://scenes/mobile/views/worlds.tscn",
					MobileViewEnum.PlaceInfo => "res://scenes/mobile/views/place_info.tscn",
					MobileViewEnum.Avatar => "res://scenes/mobile/views/avatar.tscn",
					MobileViewEnum.Store => "res://scenes/mobile/views/store.tscn",
					MobileViewEnum.Profile => "res://scenes/mobile/views/profile.tscn",
					MobileViewEnum.Dev => "res://scenes/mobile/views/test.tscn",
					_ => throw new ArgumentOutOfRangeException(nameof(viewEnum),
						 $"No scene defined for {viewEnum}")
				};

				PackedScene packed = ResourceLoader.Load<PackedScene>(pathToLoad, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
				page = packed.Instantiate<MobileViewBase>();
				_viewCache[viewEnum] = page;
				_mainView.AddChild(page);
				page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			}

			CurrentViewNode = page;
			page.ShowView(args);
			page.Visible = true;
			ViewPathSwitched?.Invoke(viewEnum);
		}
		catch (Exception ex)
		{
			// A single view failing to load must not leave the whole app blank; surface
			// the error instead of silently throwing out of _Ready/navigation.
			PT.PrintErr("Failed to open view ", viewEnum, ": ", ex);
			ShowToast(viewEnum + " failed: " + ex.Message);
		}
	}

	/// <summary>Show a short, non-blocking message to the user (replaces blocking OS.Alert).</summary>
	public void ShowToast(string message)
	{
		_toast?.Show(message);
	}

	private void ApplyContentScale()
	{
		if (!Globals.IsMobileBuild)
		{
			return;
		}

		try
		{
			// MobileScale is the base UI multiplier (tuned on an ~xhdpi reference device).
			// Scale it proportionally to the device's DPI so controls keep a consistent
			// physical size across screen densities, clamped so it never gets unusable.
			float scale = Globals.MobileScale;
			float dpi = DisplayServer.ScreenGetDpi(DisplayServer.WindowGetCurrentScreen());
			if (dpi > 0)
			{
				const float referenceDpi = 440f;
				scale *= Mathf.Clamp(dpi / referenceDpi, 0.6f, 1.6f);
			}

			GetTree().Root.ContentScaleFactor = scale;
		}
		catch (Exception ex)
		{
			PT.PrintErr("ApplyContentScale failed: ", ex);
			GetTree().Root.ContentScaleFactor = Globals.MobileScale;
		}
	}

	private void ApplySafeArea()
	{
		if (!Globals.IsMobileBuild)
		{
			return;
		}

		try
		{
			Rect2I safe = DisplayServer.GetDisplaySafeArea();
			Vector2I windowSize = DisplayServer.WindowGetSize();
			float scale = GetTree().Root.ContentScaleFactor;
			if (scale <= 0)
			{
				scale = 1f;
			}

			// Convert physical-pixel safe-area insets into UI units (content scale applied),
			// so the navbar and content avoid notches / rounded corners / gesture bars.
			float left = safe.Position.X / scale;
			float top = safe.Position.Y / scale;
			float right = (windowSize.X - (safe.Position.X + safe.Size.X)) / scale;
			float bottom = (windowSize.Y - (safe.Position.Y + safe.Size.Y)) / scale;

			Control? layout = GetNodeOrNull<Control>("Layout");
			if (layout != null)
			{
				layout.OffsetLeft = left;
				layout.OffsetTop = top;
				layout.OffsetRight = -right;
				layout.OffsetBottom = -bottom;
			}

			// Push the decorative top logo bar below the status-bar/notch as well.
			Control? topPanel = GetNodeOrNull<Control>("Panel");
			if (topPanel != null)
			{
				topPanel.OffsetTop = top;
				topPanel.OffsetBottom = 68f + top;
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr("ApplySafeArea failed: ", ex);
		}
	}
}

public enum MobileViewEnum
{
	None,
	Home,
	Worlds,
	Avatar,
	Store,
	Dev,
	PlaceInfo,
	Profile
}
