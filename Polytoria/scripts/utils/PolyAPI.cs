// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Polytoria.Utils;

public static class PolyAPI
{
	private static readonly PTHttpClient _client = new();

	public static void SetAuthToken(string userToken)
	{
		// Remove Authorization if exists
		_client.DefaultRequestHeaders.Remove("Authorization");
		_client.DefaultRequestHeaders.Add("Authorization", "Bearer " + userToken);
	}

	public static Task<APIUserInfo> GetUserFromID(int userID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/users/" + userID.ToString()),
			APIGenerationContext.Default.APIUserInfo
		);
	}

	public static Task<APIMeResponse> GetCurrentUser()
	{
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin("/api/users/me"),
			APIGenerationContext.Default.APIMeResponse
		);
	}

	public static async Task<APIJoinPlaceResponse> RequestJoinGame(APIJoinPlaceRequest req)
	{
		HttpResponseMessage response = await _client.PostAsJsonAsync(
			Globals.MainEndpoint.PathJoin("/api/places/join"),
			req,
			APIGenerationContext.Default.APIJoinPlaceRequest
		);

		response.EnsureSuccessStatusCode();

		APIJoinPlaceResponse result = await response.Content.ReadFromJsonAsync(
			APIGenerationContext.Default.APIJoinPlaceResponse
		);

		return result;
	}

	public static Task<APIAvatarResponse> GetUserAvatarFromID(int userID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/users/" + userID.ToString() + "/avatar"),
			APIGenerationContext.Default.APIAvatarResponse
		);
	}

	public static Task<APIPlaceInfo> GetWorldFromID(int placeID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/places/" + placeID.ToString()),
			APIGenerationContext.Default.APIPlaceInfo
		);
	}
	public static Task<APIGuildInfo> GetGuildFromID(int guildID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/guilds/" + guildID.ToString()),
			APIGenerationContext.Default.APIGuildInfo
		);
	}

	public static Task<APIPlaceMedia[]?> GetWorldMedia(int placeID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/places/" + placeID.ToString() + "/media"),
			APIGenerationContext.Default.APIPlaceMediaArray
		);
	}

	public static Task<APIFeedPostRoot> GetFeedPosts(int page = 1)
	{
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin("/api/feed?page=" + page.ToString()),
			APIGenerationContext.Default.APIFeedPostRoot
		);
	}

	public static Task<APIWorldsRoot> GetWorlds()
	{
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin("/api/places"),
			APIGenerationContext.Default.APIWorldsRoot
		);
	}

	public static Task<APIStoreItem> GetStoreItem(int id)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/store/" + id),
			APIGenerationContext.Default.APIStoreItem
		);
	}

	// ---------------------------------------------------------------------------------
	// Mobile-only endpoints.
	//
	// NOTE: the URLs below are ASSUMED and must be confirmed/implemented on the backend.
	// They follow the project's existing REST conventions. Each consumer degrades
	// gracefully (shows an empty state) if the endpoint is missing.
	// ---------------------------------------------------------------------------------

	/// <summary>Friends of a user. ASSUMED endpoint: GET /v1/users/{id}/friends?page=N</summary>
	public static Task<APIFriendsRoot> GetUserFriends(int userID, int page = 1)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin($"/v1/users/{userID}/friends?page={page}"),
			APIGenerationContext.Default.APIFriendsRoot
		);
	}

	/// <summary>Recently played places for the signed-in user. ASSUMED endpoint: GET /api/users/me/recently-played</summary>
	public static Task<APIWorldsRoot> GetRecentlyPlayed()
	{
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin("/api/users/me/recently-played"),
			APIGenerationContext.Default.APIWorldsRoot
		);
	}

	/// <summary>Store listing, optionally filtered by item type. ASSUMED endpoint: GET /v1/store?type=&amp;page=N</summary>
	public static Task<APIStoreRoot> GetStoreItems(string? type = null, int page = 1)
	{
		string query = $"/v1/store?page={page}";
		if (!string.IsNullOrEmpty(type))
		{
			query += "&type=" + type;
		}
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin(query),
			APIGenerationContext.Default.APIStoreRoot
		);
	}

	/// <summary>Avatar items owned by a user, optionally filtered by type. ASSUMED endpoint: GET /v1/users/{id}/inventory?type=&amp;page=N</summary>
	public static Task<APIStoreRoot> GetUserInventory(int userID, string? type = null, int page = 1)
	{
		string query = $"/v1/users/{userID}/inventory?page={page}";
		if (!string.IsNullOrEmpty(type))
		{
			query += "&type=" + type;
		}
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin(query),
			APIGenerationContext.Default.APIStoreRoot
		);
	}

	/// <summary>Persist the signed-in user's avatar. ASSUMED endpoint: POST /v1/users/me/avatar</summary>
	public static async Task<APIAvatarResponse> SaveAvatar(APIAvatarSaveRequest req)
	{
		HttpResponseMessage response = await _client.PostAsJsonAsync(
			Globals.ApiEndpoint.PathJoin("/v1/users/me/avatar"),
			req,
			APIGenerationContext.Default.APIAvatarSaveRequest
		);

		response.EnsureSuccessStatusCode();

		return await response.Content.ReadFromJsonAsync(
			APIGenerationContext.Default.APIAvatarResponse
		);
	}

	/// <summary>Comments on a place. ASSUMED endpoint: GET /v1/places/{id}/comments?page=N</summary>
	public static Task<APIPlaceCommentsRoot> GetPlaceComments(int placeID, int page = 1)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin($"/v1/places/{placeID}/comments?page={page}"),
			APIGenerationContext.Default.APIPlaceCommentsRoot
		);
	}

	/// <summary>Achievements for a place. Confirmed endpoint: GET /v1/places/{id}/achievements</summary>
	public static Task<APIAchievementsRoot> GetPlaceAchievements(int placeID)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin($"/v1/places/{placeID}/achievements"),
			APIGenerationContext.Default.APIAchievementsRoot
		);
	}

	/// <summary>Active server instances for a place. ASSUMED endpoint: GET /v1/places/{id}/servers?page=N</summary>
	public static Task<APIPlaceServersRoot> GetPlaceServers(int placeID, int page = 1)
	{
		return _client.GetFromJsonAsync(
			Globals.ApiEndpoint.PathJoin($"/v1/places/{placeID}/servers?page={page}"),
			APIGenerationContext.Default.APIPlaceServersRoot
		);
	}

	/// <summary>Like/dislike a place ("like"/"dislike"/"none"). ASSUMED endpoint: POST /v1/places/{id}/vote</summary>
	public static async Task<APIVoteResponse> VotePlace(int placeID, string vote)
	{
		HttpResponseMessage response = await _client.PostAsJsonAsync(
			Globals.ApiEndpoint.PathJoin($"/v1/places/{placeID}/vote"),
			new APIVoteRequest { Vote = vote },
			APIGenerationContext.Default.APIVoteRequest
		);

		response.EnsureSuccessStatusCode();

		return await response.Content.ReadFromJsonAsync(
			APIGenerationContext.Default.APIVoteResponse
		);
	}

#if CREATOR
	public static Task<APILibraryResponse> GetLibrary(LibraryQueryTypeEnum type, int page = 1, string searchQuery = "")
	{
		string queryType = type switch
		{
			LibraryQueryTypeEnum.Model => "model",
			LibraryQueryTypeEnum.Image => "decal",
			LibraryQueryTypeEnum.Audio => "audio",
			LibraryQueryTypeEnum.Mesh => "mesh",
			LibraryQueryTypeEnum.Addon => "addon",
			_ => ""
		};
		return _client.GetFromJsonAsync(
			Globals.MainEndpoint.PathJoin($"/api/library?page={page}&search={searchQuery}&type={queryType}"),
			APIGenerationContext.Default.APILibraryResponse
		);
	}
#endif

	public static Task<string> GetProfanityList()
	{
		return _client.GetStringAsync(Globals.ApiEndpoint.PathJoin("/v1/game/server/profanity"));
	}
}
