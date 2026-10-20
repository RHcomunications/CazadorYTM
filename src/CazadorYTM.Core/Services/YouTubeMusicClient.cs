namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public sealed class YouTubeMusicClient
{
    private const string Origin = "https://music.youtube.com";
    private const string BrowseUrl = Origin + "/youtubei/v1/browse?prettyPrint=false";
    private const string FallbackClientVersion = "1.20261018.16.00";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    private const int MaxPages = 30;

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly SemaphoreSlim VersionLock = new(1, 1);
    private static readonly Regex ClientVersionRegex = new("\"INNERTUBE_CLIENT_VERSION\":\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex LoggedOutRegex = new("\"logged_in\",\"value\":\"0\"", RegexOptions.Compiled);
    private static string? _cachedClientVersion;

    private readonly YouTubeMusicSession _session;
    private readonly string _language;

    public YouTubeMusicClient(YouTubeMusicSession session, string? language = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _language = string.IsNullOrWhiteSpace(language) ? "es" : language.Trim();
    }

    public async Task<List<RadarItem>> FetchSectionAsync(RadarSection section, int maxItems = 1000, CancellationToken cancellationToken = default)
    {
        var browseId = section switch
        {
            RadarSection.History => "FEmusic_history",
            RadarSection.LikedSongs => "VLLM",
            RadarSection.Playlists => "FEmusic_liked_playlists",
            _ => null
        };

        if (browseId == null)
        {
            return new List<RadarItem>();
        }

        return await FetchAllAsync(browseId, section, maxItems, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<RadarItem>> FetchPlaylistTracksAsync(string playlistIdOrUrl, int maxItems = 1000, CancellationToken cancellationToken = default)
    {
        var browseId = ToPlaylistBrowseId(playlistIdOrUrl);
        return browseId == null
            ? new List<RadarItem>()
            : await FetchAllAsync(browseId, RadarSection.Playlists, maxItems, cancellationToken).ConfigureAwait(false);
    }

    public static string? ToPlaylistBrowseId(string? playlistIdOrUrl)
    {
        if (string.IsNullOrWhiteSpace(playlistIdOrUrl))
        {
            return null;
        }

        var value = playlistIdOrUrl.Trim();
        if (value.Contains("list=", StringComparison.Ordinal))
        {
            var listId = ExtractListId(value);
            if (string.IsNullOrWhiteSpace(listId))
            {
                return null;
            }

            return listId.StartsWith("VL", StringComparison.Ordinal) ? listId : "VL" + listId;
        }

        if (value.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }

        return value.StartsWith("VL", StringComparison.Ordinal) ? value : "VL" + value;
    }

    private static string? ExtractListId(string value)
    {
        var queryIndex = value.IndexOf('?');
        if (queryIndex < 0)
        {
            return null;
        }

        var query = value.Substring(queryIndex + 1);
        foreach (var pair in query.Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (pair.Substring(0, separator) == "list")
            {
                return Uri.UnescapeDataString(pair.Substring(separator + 1));
            }
        }

        return null;
    }

    private async Task<List<RadarItem>> FetchAllAsync(string browseId, RadarSection section, int maxItems, CancellationToken cancellationToken)
    {
        var clientVersion = await GetClientVersionAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<RadarItem>();
        string? continuation = null;

        for (var page = 0; page < MaxPages && items.Count < maxItems; page++)
        {
            var payload = new Dictionary<string, object?>
            {
                ["context"] = BuildContext(clientVersion)
            };

            if (continuation == null)
            {
                payload["browseId"] = browseId;
            }
            else
            {
                payload["continuation"] = continuation;
            }

            var root = await PostBrowseAsync(payload, cancellationToken).ConfigureAwait(false);
            var (pageItems, nextContinuation) = YouTubeMusicResponseParser.Parse(root, section);

            foreach (var item in pageItems)
            {
                if (items.Count >= maxItems)
                {
                    break;
                }

                items.Add(item);
            }

            if (nextContinuation == null || pageItems.Count == 0)
            {
                break;
            }

            continuation = nextContinuation;
        }

        return items;
    }

    private Dictionary<string, object?> BuildContext(string clientVersion)
    {
        return new Dictionary<string, object?>
        {
            ["client"] = new Dictionary<string, object?>
            {
                ["clientName"] = "WEB_REMIX",
                ["clientVersion"] = clientVersion,
                ["hl"] = _language
            },
            ["user"] = new Dictionary<string, object?>
            {
                ["lockedSafetyMode"] = false
            },
            ["request"] = new Dictionary<string, object?>
            {
                ["useSsl"] = true
            }
        };
    }

    private async Task<JsonElement> PostBrowseAsync(Dictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, BrowseUrl);
        request.Headers.TryAddWithoutValidation("Cookie", _session.CookieHeader);
        request.Headers.TryAddWithoutValidation("Authorization", _session.CreateAuthorizationHeader(DateTimeOffset.UtcNow, Origin));
        request.Headers.TryAddWithoutValidation("Origin", Origin);
        request.Headers.TryAddWithoutValidation("Referer", Origin + "/");
        request.Headers.TryAddWithoutValidation("X-Goog-AuthUser", _session.AccountIndex.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new YouTubeMusicException($"YouTube Music respondió {(int)response.StatusCode}.");
        }

        if (LoggedOutRegex.IsMatch(body))
        {
            throw new YouTubeMusicException("La sesión de YouTube Music no está autenticada (cookies vencidas o incompletas).");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new YouTubeMusicException("Respuesta inválida de YouTube Music.", ex);
        }
    }

    private async Task<string> GetClientVersionAsync(CancellationToken cancellationToken)
    {
        if (_cachedClientVersion != null)
        {
            return _cachedClientVersion;
        }

        await VersionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedClientVersion != null)
            {
                return _cachedClientVersion;
            }

            try
            {
                using var response = await Http.GetAsync(Origin + "/", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var match = ClientVersionRegex.Match(html);
                    if (match.Success)
                    {
                        _cachedClientVersion = match.Groups[1].Value;
                    }
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            _cachedClientVersion ??= FallbackClientVersion;
            return _cachedClientVersion;
        }
        finally
        {
            VersionLock.Release();
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        return client;
    }
}

public class YouTubeMusicException : Exception
{
    public YouTubeMusicException(string message) : base(message)
    {
    }

    public YouTubeMusicException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
