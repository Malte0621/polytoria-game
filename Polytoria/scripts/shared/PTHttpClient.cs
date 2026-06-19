// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
#if !USE_NATIVE_HTTP
using System;
using System.Net;
#endif

namespace Polytoria.Shared;

public partial class PTHttpClient
{
	private const int DefaultDownloadChunkSize = 10000;

	// Request timeout in seconds. Applied to both the Godot HttpRequest node and
	// the native HttpClient so requests fault instead of hanging forever.
	private const double RequestTimeoutSeconds = 30.0;

	// Maximum number of retries on transient failure/timeout (total attempts = 1 + MaxRetries).
	private const int MaxRetries = 2;

	// Base backoff delay (ms) between retries; grows linearly with the attempt number.
	private const int RetryBackoffBaseMs = 500;
#if USE_NATIVE_HTTP
	private static readonly HttpClient _httpClient = new()
	{
		Timeout = System.TimeSpan.FromSeconds(RequestTimeoutSeconds)
	};
#endif
	public Dictionary<string, string> DefaultRequestHeaders { get; set; } = [];

	public PTHttpClient()
	{
		DefaultRequestHeaders["User-Agent"] = $"Polytoria Client {Globals.AppVersion}";
	}

#if !USE_NATIVE_HTTP
	public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage msg)
	{
		// Check nohttp feature flag
		if (Globals.UseNoHttp) throw new HttpRequestException("Http is disabled via feature flag");

		List<string> headers = [];

		foreach ((string k, string v) in DefaultRequestHeaders)
		{
			headers.Add(k + ": " + v);
		}

		foreach (var item in msg.Headers)
		{
			headers.Add(item.Key + ": " + string.Join(", ", item.Value));
		}

		// Add content headers if present
		if (msg.Content != null)
		{
			foreach (var item in msg.Content.Headers)
			{
				headers.Add(item.Key + ": " + string.Join(", ", item.Value));
			}
		}

		// Read the body once; reused across retries since HttpRequestMessage.Content
		// is not guaranteed to be re-readable.
		byte[] body = msg.Content != null ? await msg.Content.ReadAsByteArrayAsync() : [];

		string url = msg.RequestUri?.ToString() ?? throw new InvalidOperationException("URL is null");
		Godot.HttpClient.Method method = Enum.Parse<Godot.HttpClient.Method>(msg.Method.Method.ToLower().Capitalize());

		HttpRequestException? lastError = null;

		// Retry-with-backoff loop around transient failures/timeouts.
		for (int attempt = 0; attempt <= MaxRetries; attempt++)
		{
			if (attempt > 0)
			{
				PT.PrintWarn($"HttpRequest retry {attempt}/{MaxRetries} for {url} after transient error: {lastError?.Message}");
				await Task.Delay(RetryBackoffBaseMs * attempt);
			}

			try
			{
				return await SendOnceAsync(url, method, headers, body);
			}
			catch (HttpRequestException ex)
			{
				// Only the transient-marked failures get here; bubble anything else up immediately.
				lastError = ex;
			}
		}

		throw lastError ?? new HttpRequestException("HttpRequest failed after retries");
	}

	private Task<HttpResponseMessage> SendOnceAsync(string url, Godot.HttpClient.Method method, List<string> headers, byte[] body)
	{
		TaskCompletionSource<HttpResponseMessage> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

		// needs to be callable due to add_child
		Callable.From(() =>
		{
			HttpRequest req = new()
			{
				DownloadChunkSize = DefaultDownloadChunkSize,
				Timeout = RequestTimeoutSeconds
			};

			Globals.Singleton.AddChild(req);

			// Ensures the node is freed and the task resolved exactly once, even on
			// error paths, so the awaiting Task can never hang forever.
			bool finished = false;
			void Finish(HttpResponseMessage? response, HttpRequestException? error)
			{
				if (finished) return;
				finished = true;

				if (GodotObject.IsInstanceValid(req))
				{
					req.QueueFree();
				}

				if (error != null)
				{
					tcs.TrySetException(error);
				}
				else
				{
					tcs.TrySetResult(response!);
				}
			}

			req.RequestCompleted += (result, responseCode, responseHeaders, responseBody) =>
			{
				HttpRequest.Result requestResult = (HttpRequest.Result)result;

				if (requestResult != HttpRequest.Result.Success)
				{
					// Network-level failure (timeout, can't connect, TLS error, ...).
					// Surface as a transient HttpRequestException so the retry loop can react.
					Finish(null, new HttpRequestException($"HttpRequest result: {requestResult}"));
					return;
				}

				HttpResponseMessage response = new((HttpStatusCode)responseCode)
				{
					Content = new ByteArrayContent(responseBody)
				};

				foreach (string header in responseHeaders)
				{
					string[] parts = header.Split(':', 2);
					if (parts.Length == 2)
					{
						response.Headers.TryAddWithoutValidation(parts[0].Trim(), parts[1].Trim());
					}
				}

				Finish(response, null);
			};

			Error error = req.RequestRaw(url, [.. headers], method, new ReadOnlySpan<byte>(body));

			if (error != Error.Ok)
			{
				// Request could not even be dispatched; treat as transient so we retry.
				Finish(null, new HttpRequestException($"HttpRequest failed with error: {error}"));
			}
		}).CallDeferred();

		return tcs.Task;
	}
#else
	public Task<HttpResponseMessage> SendAsync(HttpRequestMessage msg)
	{
		foreach ((string key, string val) in DefaultRequestHeaders)
		{
			msg.Headers.TryAddWithoutValidation(key, val);
		}
		return _httpClient.SendAsync(msg);
	}
#endif

	public async Task<HttpResponseMessage> GetAsync(string url)
	{
		using HttpRequestMessage msg = new(HttpMethod.Get, url);
		return await SendAsync(msg);
	}

	public async Task<T?> GetFromJsonAsync<T>(string url, JsonTypeInfo<T> jsonTypeInfo)
	{
		using HttpRequestMessage msg = new(HttpMethod.Get, url);
		msg.Headers.TryAddWithoutValidation("Accept", "application/json");

		using HttpResponseMessage response = await SendAsync(msg);
		response.EnsureSuccessStatusCode();

		string json = await response.Content.ReadAsStringAsync();
		return JsonSerializer.Deserialize(json, jsonTypeInfo);
	}

	public async Task<byte[]> GetByteArrayAsync(string url)
	{
		using HttpResponseMessage response = await GetAsync(url);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadAsByteArrayAsync();
	}

	public async Task<HttpResponseMessage> PostAsync(string url, HttpContent content)
	{
		using HttpRequestMessage msg = new(HttpMethod.Post, url)
		{
			Content = content
		};

		return await SendAsync(msg);
	}

	public async Task<HttpResponseMessage> PostAsJsonAsync<T>(string url, T value, JsonTypeInfo<T> jsonTypeInfo)
	{
		string json = JsonSerializer.Serialize(value, jsonTypeInfo);

		using HttpRequestMessage msg = new(HttpMethod.Post, url)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json")
		};

		return await SendAsync(msg);
	}

	public async Task<string> GetStringAsync(string url)
	{
		using HttpResponseMessage response = await GetAsync(url);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadAsStringAsync();
	}
}
