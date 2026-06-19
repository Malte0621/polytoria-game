// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared.AssetLoaders;
using System;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public partial class PTImageAsset : ImageAsset
{
	private uint _imageID;
	private ImageTypeEnum _imageType;
	private Vector2I? _desiredSize;

	[Editable, ScriptProperty]
	public uint ImageID
	{
		get => _imageID;
		set
		{
			_imageID = value;
			QueueLoadResource();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public ImageTypeEnum ImageType
	{
		get => _imageType;
		set
		{
			_imageType = value;
			QueueLoadResource();
			OnPropertyChanged();
		}
	}

	// Optional downscale target. When set, the loaded image is resized to these
	// dimensions before being cached, saving mobile bandwidth and memory.
	// Default null leaves the image at its native size (unchanged behaviour).
	public Vector2I? DesiredSize
	{
		get => _desiredSize;
		set
		{
			_desiredSize = value;
			QueueLoadResource();
			OnPropertyChanged();
		}
	}

	internal string? DirectImageURL { get; private set; }

	public static void RegisterAsset()
	{
		RegisterType<PTImageAsset>();
	}

	public override void LoadResource()
	{
		if (ImageID == 0) { return; }
		ResourceType resourceType = ImageType switch
		{
			ImageTypeEnum.Asset => ResourceType.Decal,
			ImageTypeEnum.AssetThumbnail => ResourceType.AssetThumbnail,
			ImageTypeEnum.WorldThumbnail => ResourceType.PlaceThumbnail,
			ImageTypeEnum.UserAvatar => ResourceType.UserThumbnail,
			ImageTypeEnum.UserAvatarHeadshot => ResourceType.UserHeadshot,
			ImageTypeEnum.GuildIcon => ResourceType.GuildThumbnail,
			ImageTypeEnum.GuildBanner => ResourceType.GuildBanner,
			ImageTypeEnum.PlaceIcon => ResourceType.PlaceIcon,
			_ => throw new NotImplementedException()
		};

		AssetLoader.Singleton.GetRawCache(
			new() { Type = resourceType, ID = ImageID, Resize = DesiredSize },
			OnResourceLoaded
		);
	}

	private void OnResourceLoaded(CacheItem cacheItem)
	{
		DirectImageURL = cacheItem.DirectURL;
		InvokeResourceLoaded(cacheItem.Resource);
	}
}

[ScriptEnum]
public enum ImageTypeEnum
{
	Asset,
	AssetThumbnail,
	WorldThumbnail,
	UserAvatar,
	UserAvatarHeadshot,
	GuildIcon,
	GuildBanner,
	PlaceIcon
}
