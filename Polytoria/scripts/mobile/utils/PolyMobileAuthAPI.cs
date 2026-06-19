// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Polytoria.Mobile.Utils;

public static class PolyMobileAuthAPI
{
	private static readonly PTHttpClient _client = new();
	private static string _authState = "";

	public static event Action<APIMeResponse>? UserAuthenticated;
	public static event Action? AskForAuthentication;

	public static APIMeResponse CurrentUserInfo { get; private set; }

	private static MobileAuthData _authData;
	private const string AuthDataPath = "user://auth2";

	public static async void SetupClient()
	{
		_authData = new();

		if (FileAccess.FileExists(AuthDataPath))
		{
			try
			{
				using FileAccess access = FileAccess.Open(AuthDataPath, FileAccess.ModeFlags.Read);
				if (access == null)
				{
					PT.PrintErr($"FileAccess.Open returned null for path {AuthDataPath}");
				}
				else
				{
					string data = access.GetAsText();

					if (string.IsNullOrEmpty(data))
					{
						PT.PrintWarn($"Auth data file at '{AuthDataPath}' is empty, removing.");
						access.Close();
						DirAccess.RemoveAbsolute(AuthDataPath);
					}
					else
					{
						MobileAuthData? auth = JsonSerializer.Deserialize(data, MobileAuthDataGenerationContext.Default.MobileAuthData);
						if (auth != null)
						{
							PT.Print("Existing auth data exists, using");
							_authData = auth.Value;
						}
					}
				}
			}
			catch (Exception ex)
			{
				// Corrupt/unreadable auth data should never crash us, just fall back to unauthenticated.
				PT.PrintErr(ex);
				PT.PrintWarn($"Failed to load auth data from '{AuthDataPath}', removing and falling back to unauthenticated.");
				try
				{
					DirAccess.RemoveAbsolute(AuthDataPath);
				}
				catch (Exception removeEx)
				{
					PT.PrintErr(removeEx);
				}
				_authData = new();
			}
		}

		if (_authData.Token == null)
		{
			AskForAuthentication?.Invoke();
		}
		else
		{
			await LoginWithAuthToken(_authData.Token!);
		}
	}

	private static void SaveAuthData()
	{
		try
		{
			// Serialize BEFORE opening the file so a serialization failure doesnt leave a half-written/empty file.
			string json = JsonSerializer.Serialize(_authData, MobileAuthDataGenerationContext.Default.MobileAuthData);

			using FileAccess authData = FileAccess.Open(AuthDataPath, FileAccess.ModeFlags.Write);
			if (authData == null)
			{
				PT.PrintErr($"FileAccess.Open returned null for path {AuthDataPath}");
				return;
			}
			authData.StoreString(json);
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}
	}

	public static void StartMobileAuth()
	{
		_authState = Guid.NewGuid().ToString();
		OS.ShellOpen(Globals.MainEndpoint.PathJoin("/auth/mobile?state=" + _authState));
	}

	public static async Task LoginWithCodeAndState(string code, string state)
	{
		using HttpResponseMessage response = await _client.PostAsJsonAsync(Globals.ApiEndpoint.PathJoin("/v1/mobile/token"), new APIMobileTokenRequest()
		{
			Code = code,
			State = state
		}, APIGenerationContext.Default.APIMobileTokenRequest);
		if (response.IsSuccessStatusCode)
		{
			APIMobileTokenResponse tokenRes = await response.Content.ReadFromJsonAsync(APIGenerationContext.Default.APIMobileTokenResponse);
			if (tokenRes.Success)
			{
				await LoginWithAuthToken(tokenRes.Token);
			}
		}
		else
		{
			PT.PrintErr(response);
			throw new AuthenticationException("Something went wrong");
		}
	}

	public static async Task LoginWithAuthToken(string userToken)
	{
		PolyAPI.SetAuthToken(userToken);
		try
		{
			APIMeResponse me = await PolyAPI.GetCurrentUser();

			_authData.Username = me.Username;
			_authData.Token = userToken;
			_authData.UserID = me.Id;
			SaveAuthData();
			PT.Print("Hello!! ", me.Username);

			CurrentUserInfo = me;
			UserAuthenticated?.Invoke(me);
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
			Polytoria.Mobile.MobileUI.Singleton?.ShowToast("Your session has expired, please log back in again.");
			AskForAuthentication?.Invoke();
		}
	}
}


[JsonSerializable(typeof(MobileAuthData))]
internal partial class MobileAuthDataGenerationContext : JsonSerializerContext { }

public struct MobileAuthData
{
	[JsonInclude]
	public string Token { get; set; }

	[JsonInclude]
	public int UserID { get; set; }

	[JsonInclude]
	public string Username { get; set; }
}
