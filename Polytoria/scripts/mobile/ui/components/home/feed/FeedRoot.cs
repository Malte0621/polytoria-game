// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;

namespace Polytoria.Mobile.UI;

public partial class FeedRoot : Node
{
	private const string FeedCardPath = "res://scenes/mobile/components/home/feed_card.tscn";

	private PackedScene _feedCard = null!;
	[Export] private Control _feedContainer = null!;

	private int _currentPage;
	private int _lastPage = 1;
	private bool _isLoading;

	/// <summary>True while a page request is in flight (guards against duplicate loads).</summary>
	public bool IsLoading => _isLoading;

	/// <summary>True while there are still more pages to fetch.</summary>
	public bool HasMorePages => _currentPage < _lastPage;

	public override void _Ready()
	{
		// First page.
		LoadNextPage();
	}

	/// <summary>
	/// Loads the next page of feed posts. Safe to call repeatedly: it no-ops while a
	/// request is in flight or once the last page has been reached.
	/// </summary>
	public async void LoadNextPage()
	{
		if (_isLoading || !HasMorePages)
		{
			return;
		}

		_isLoading = true;
		int pageToLoad = _currentPage + 1;

		try
		{
			APIFeedPostRoot feed = await PolyAPI.GetFeedPosts(pageToLoad);

			_currentPage = feed.Meta.CurrentPage;
			_lastPage = Mathf.Max(feed.Meta.LastPage, _currentPage);

			if (feed.Data != null)
			{
				foreach (APIFeedPostData item in feed.Data)
				{
					FeedPostCard card = Globals.CreateInstanceFromScene<FeedPostCard>(FeedCardPath);
					card.Data = item;
					_feedContainer.AddChild(card);
				}
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			MobileUI.Singleton.ShowToast("Couldn't load the feed. Please try again.");
		}

		_isLoading = false;
	}
}
