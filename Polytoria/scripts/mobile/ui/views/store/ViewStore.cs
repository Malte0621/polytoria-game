// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Mobile;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;

namespace Polytoria.Mobile.UI;

// Native Store browse page. Reached from the navbar Store tab via
// SwitchTo(MobileViewEnum.Store). Builds its UI in code (header + scrollable
// grid of StoreItemCards) so the backing scene can stay minimal.
public partial class ViewStore : MobileViewBase
{
	private const int GridColumns = 2;

	private GridContainer _grid = null!;
	private Label _emptyLabel = null!;

	private bool _isLoading;

	public override void _Ready()
	{
		BuildLayout();
	}

	private void BuildLayout()
	{
		MarginContainer root = new();
		root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(root);

		VBoxContainer column = new();
		column.AddThemeConstantOverride("separation", 12);
		root.AddChild(column);

		Label title = new()
		{
			Text = "Store",
		};
		title.AddThemeFontSizeOverride("font_size", 24);
		// Indent the heading slightly.
		MarginContainer titleMargin = new();
		titleMargin.AddThemeConstantOverride("margin_left", 16);
		titleMargin.AddThemeConstantOverride("margin_top", 16);
		titleMargin.AddThemeConstantOverride("margin_right", 16);
		titleMargin.AddChild(title);
		column.AddChild(titleMargin);

		ScrollContainer scroll = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		column.AddChild(scroll);

		MarginContainer gridMargin = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		gridMargin.AddThemeConstantOverride("margin_left", 16);
		gridMargin.AddThemeConstantOverride("margin_right", 16);
		gridMargin.AddThemeConstantOverride("margin_bottom", 16);
		scroll.AddChild(gridMargin);

		VBoxContainer scrollColumn = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		gridMargin.AddChild(scrollColumn);

		_grid = new GridContainer
		{
			Columns = GridColumns,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		_grid.AddThemeConstantOverride("h_separation", 8);
		_grid.AddThemeConstantOverride("v_separation", 8);
		scrollColumn.AddChild(_grid);

		_emptyLabel = new Label
		{
			Text = "No items to show right now.",
			HorizontalAlignment = HorizontalAlignment.Center,
			Visible = false,
		};
		_emptyLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.5f));
		scrollColumn.AddChild(_emptyLabel);
	}

	public override async void ShowView(object? args)
	{
		base.ShowView(args);

		if (_isLoading)
		{
			return;
		}
		_isLoading = true;

		ClearGrid();
		_emptyLabel.Visible = false;
		MobileUI.Singleton.LoadingScreen.ShowScreen();

		try
		{
			APIStoreRoot root = await PolyAPI.GetStoreItems(null, 1);

			if (root.Data == null || root.Data.Length == 0)
			{
				_emptyLabel.Visible = true;
			}
			else
			{
				foreach (APIStoreItem item in root.Data)
				{
					StoreItemCard card = new()
					{
						ItemData = item,
					};
					card.CardPressed += OnItemPressed;
					_grid.AddChild(card);
				}
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			MobileUI.Singleton.ShowToast("Could not load the store. Please try again.");
			_emptyLabel.Visible = true;
		}
		finally
		{
			MobileUI.Singleton.LoadingScreen.HideScreen();
			_isLoading = false;
		}
	}

	private void ClearGrid()
	{
		foreach (Node child in _grid.GetChildren())
		{
			child.QueueFree();
		}
	}

	private async void OnItemPressed(int itemID)
	{
		// There is no dedicated item-detail view in MobileViewEnum, so surface a
		// lightweight summary toast. Fetching the full item also confirms it exists.
		try
		{
			APIStoreItem item = await PolyAPI.GetStoreItem(itemID);
			string price = item.IsLimited
				? "Limited"
				: (item.Price is int p && p > 0 ? p + " B$" : "Free");
			MobileUI.Singleton.ShowToast($"{item.Name} - {price}");
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			MobileUI.Singleton.ShowToast("Could not open that item.");
		}
	}
}
