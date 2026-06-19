// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Providers.AssetLoaders;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Shared.AssetLoaders;

public partial class AssetLoader : Node
{

	private readonly record struct AssetCacheKey(ResourceType Type, uint ID, Vector2I? Resize);
	private const int DefaultMaxConcurrentRequests = 5;

	// Maximum amount of resident asset bytes before least-recently-used entries are evicted.
	// Tunable; keeps long mobile sessions from growing the cache unbounded.
	private const long MaxCacheBytes = 96 * 1024 * 1024;

	public AssetLoader()
	{
		Singleton = this;
		AssetProvider = new PTAssetProvider();
	}

	public static AssetLoader Singleton { get; private set; } = null!;
	public bool UseAssetLoader { get; set; } = true;

	private long _assetSizeBytes = 0;
	internal long AssetSizeBytes => _assetSizeBytes;
	internal int PendingAssetsCount => _pendingRequests.Count;
	internal int AssetCacheCount => _cache.Count;

	private readonly ConcurrentDictionary<AssetCacheKey, CacheItem> _cache = [];
	private readonly ConcurrentDictionary<AssetCacheKey, Lazy<Task<CacheItem>>> _pendingRequests = [];

	// Tracks usage recency for LRU eviction. Most-recently-used at the head (First),
	// least-recently-used at the tail (Last). All access guarded by _lruLock.
	private readonly LinkedList<AssetCacheKey> _lruOrder = new();
	private readonly Dictionary<AssetCacheKey, LinkedListNode<AssetCacheKey>> _lruNodes = [];
	private readonly object _lruLock = new();

	public int MaxConcurrentRequests { get; set; } = DefaultMaxConcurrentRequests;

	private SemaphoreSlim _loadSlots = null!;

	public IAssetProvider AssetProvider = null!;

	private static AssetCacheKey KeyFor(CacheItem item)
	{
		return new AssetCacheKey(item.Type, item.ID, item.Resize);
	}

	private async Task<CacheItem> LoadResource(CacheItem item)
	{
		if (item.ID == 0)
		{
			return item;
		}

		if (!UseAssetLoader)
		{
			return item;
		}

		return await AssetProvider.LoadResource(item);
	}

	public void GetResource(CacheItem item, Action<Resource> callback)
	{
		GetRawCache(item, result =>
		{
			callback(result.Resource);
		});
	}

	private async Task<CacheItem> LoadItem(CacheItem item, AssetCacheKey key)
	{
		if (_loadSlots == null)
		{
			_loadSlots = new(MaxConcurrentRequests);
		}

		await _loadSlots.WaitAsync();
		try
		{
			CacheItem result = await LoadResource(item);

			// If an identical entry already lived in the cache (e.g. a race), drop the
			// stale byte accounting before replacing it so the running total stays accurate.
			if (_cache.TryGetValue(key, out CacheItem previous))
			{
				Interlocked.Add(ref _assetSizeBytes, -previous.SizeBytes);
			}

			_cache[key] = result;
			Interlocked.Add(ref _assetSizeBytes, result.SizeBytes);
			TouchLru(key);
			EvictIfNeeded();
			return result;
		}
		finally
		{
			_pendingRequests.TryRemove(key, out _);
			_loadSlots.Release();
		}
	}

	// Marks a key as most-recently-used, inserting it if not already tracked.
	private void TouchLru(AssetCacheKey key)
	{
		lock (_lruLock)
		{
			// The entry may have been evicted between a cache hit and acquiring this
			// lock; don't resurrect untracked-but-uncached keys into the LRU.
			if (!_cache.ContainsKey(key))
			{
				return;
			}

			if (_lruNodes.TryGetValue(key, out LinkedListNode<AssetCacheKey>? node))
			{
				_lruOrder.Remove(node);
				_lruOrder.AddFirst(node);
			}
			else
			{
				_lruNodes[key] = _lruOrder.AddFirst(key);
			}
		}
	}

	// Evicts least-recently-used entries until the resident byte count is back under the cap.
	// Underlying Godot resources are freed on the main thread to stay thread-safe.
	private void EvictIfNeeded()
	{
		if (Interlocked.Read(ref _assetSizeBytes) <= MaxCacheBytes)
		{
			return;
		}

		List<CacheItem> evicted = [];

		lock (_lruLock)
		{
			// Never evict the single most-recently-used entry; it is likely in active use.
			while (Interlocked.Read(ref _assetSizeBytes) > MaxCacheBytes && _lruOrder.Count > 1)
			{
				LinkedListNode<AssetCacheKey>? lruNode = _lruOrder.Last;
				if (lruNode == null)
				{
					break;
				}

				AssetCacheKey key = lruNode.Value;
				_lruOrder.RemoveLast();
				_lruNodes.Remove(key);

				// Skip eviction if a fresh load for this key is in flight; the pending
				// load will re-insert it shortly and freeing now could race that consumer.
				if (_pendingRequests.ContainsKey(key))
				{
					continue;
				}

				if (_cache.TryRemove(key, out CacheItem item))
				{
					Interlocked.Add(ref _assetSizeBytes, -item.SizeBytes);
					evicted.Add(item);
				}
			}
		}

		foreach (CacheItem item in evicted)
		{
			FreeResource(item.Resource);
		}
	}

	// Frees a cached Godot resource safely on the main thread.
	private static void FreeResource(Resource? resource)
	{
		if (resource == null)
		{
			return;
		}

		Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(resource))
			{
				// Resource derives from RefCounted, so disposing the managed wrapper
				// releases our reference and lets Godot reclaim it once unused.
				resource.Dispose();
			}
		}).CallDeferred();
	}

	public void GetRawCache(CacheItem item, Action<CacheItem> callback)
	{
		AssetCacheKey key = KeyFor(item);

		// Return cached asset
		if (_cache.TryGetValue(key, out CacheItem cached))
		{
			TouchLru(key);
			Callable.From(() => callback(cached)).CallDeferred();
			return;
		}

		Lazy<Task<CacheItem>> task = _pendingRequests.GetOrAdd(key, _ => new Lazy<Task<CacheItem>>(() => LoadItem(item, key), LazyThreadSafetyMode.ExecutionAndPublication));

		_ = WaitForResource(task.Value, item, callback);
	}

	private static async Task WaitForResource(Task<CacheItem> task, CacheItem item, Action<CacheItem> callback)
	{
		try
		{
			CacheItem result = await task;
			Callable.From(() => callback(result)).CallDeferred();
		}
		catch (Exception exception)
		{
			Callable.From(() => PT.PrintErr("Failed to load resource (Type: " + item.Type + ", ID: " + item.ID + "): " + exception.Message)).CallDeferred();
		}
	}
}

public enum ResourceType
{
	Mesh,
	Decal,
	Audio,
	AssetThumbnail,
	PlaceThumbnail,
	PlaceIcon,
	UserThumbnail,
	UserHeadshot,
	GuildThumbnail,
	GuildBanner,
	Asset,
	Font
}


public struct CacheItem
{
	public ResourceType Type { get; set; }
	public uint ID { get; set; }
	public string DirectURL { get; set; }
	public Vector2I? Resize { get; set; }
	public Resource Resource { get; set; }
	public long SizeBytes { get; set; }

	public override readonly bool Equals(object? obj)
	{
		return obj is CacheItem item && item.Type == Type && item.ID == ID && item.Resize == Resize;
	}

	public override readonly int GetHashCode()
	{
		return HashCode.Combine(Type, ID, Resize);
	}

	public static bool operator ==(CacheItem left, CacheItem right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(CacheItem left, CacheItem right)
	{
		return !(left == right);
	}
}
