// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;

namespace Polytoria.Mobile.UI;

public partial class WorldsGrid : Control
{
	private const string PlaceCardPath = "res://scenes/mobile/components/shared/place_card.tscn";
	public PackedScene _placeCardPacked = null!;

	public override void _Ready()
	{
		_placeCardPacked = GD.Load<PackedScene>(PlaceCardPath);
		LoadWorlds();
	}

	private async void LoadWorlds()
	{
		MobileUI.Singleton.LoadingScreen.ShowScreen();
		try
		{
			APIWorldsRoot root = await PolyAPI.GetWorlds();
			if (root.Data == null)
			{
				return;
			}

			foreach (APIWorldsData item in root.Data)
			{
				PlaceCard card = _placeCardPacked.Instantiate<PlaceCard>();
				card.PlaceData = item;
				AddChild(card);
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			MobileUI.Singleton.ShowToast("Couldn't load places. Please try again.");
		}
		MobileUI.Singleton.LoadingScreen.HideScreen();
	}
}
