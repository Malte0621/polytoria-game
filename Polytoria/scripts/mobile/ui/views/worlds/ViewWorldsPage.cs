// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;

namespace Polytoria.Mobile.UI;

/// <summary>
/// Worlds view controller. Populates the "Featured Place" header from live data (the
/// first place returned by the worlds listing). The grid of all places is loaded
/// independently by <see cref="WorldsGrid"/>.
/// </summary>
public partial class ViewWorldsPage : MobileViewBase
{
	private Label _featuredName = null!;
	private Label _featuredDescription = null!;
	private bool _loaded;

	public override void _Ready()
	{
		_featuredName = GetNode<Label>("ScrollContainer/VBoxContainer/Control/Layout/Label3");
		_featuredDescription = GetNode<Label>("ScrollContainer/VBoxContainer/Control/Layout/Label2");
		base._Ready();
	}

	public override void ShowView(object? args)
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		LoadFeatured();
	}

	private async void LoadFeatured()
	{
		try
		{
			// NOTE: WorldsGrid also fetches the listing for the grid; the featured place
			// reuses the first entry of this call rather than a dedicated endpoint.
			APIWorldsRoot root = await PolyAPI.GetWorlds();
			if (root.Data == null || root.Data.Length == 0)
			{
				return;
			}

			APIWorldsData featured = root.Data[0];
			_featuredName.Text = featured.Name;
			_featuredDescription.Text = string.IsNullOrEmpty(featured.Description) ? "" : featured.Description;
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}
	}
}
