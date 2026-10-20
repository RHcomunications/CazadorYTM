namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Search;
using YoutubeExplode.Videos;
using CazadorYTM.Core.Models;

public static class MetadataService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly YoutubeClient _youtubeClient = new(_httpClient);
    private const int MaxMetadataEntries = 10000;

    static MetadataService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"CazadorYTM/{Constants.AppVersion} (Windows NT 10.0; Win64; x64)");
    }

    public static string? FetchLyricsLrclib(string trackName, string? artistName = null, int? duration = null)
    {
        try
        {
            var url = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(trackName)}";
            if (!string.IsNullOrEmpty(artistName))
            {
                url += $"&artist_name={Uri.EscapeDataString(artistName)}";
            }

            var response = _httpClient.GetStringAsync(url).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                foreach (var item in root.EnumerateArray())
                {
                    if (item.TryGetProperty("syncedLyrics", out var synced) && !string.IsNullOrEmpty(synced.GetString()))
                    {
                        return synced.GetString();
                    }
                    if (item.TryGetProperty("plainLyrics", out var plain) && !string.IsNullOrEmpty(plain.GetString()))
                    {
                        return plain.GetString();
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public static bool TryWriteLyrics(string audioFilePath, string trackName, string? artistName = null, int? duration = null)
    {
        try
        {
            var lyrics = FetchLyricsLrclib(trackName, artistName, duration);
            if (!string.IsNullOrEmpty(lyrics))
            {
                var lrcPath = Path.ChangeExtension(audioFilePath, ".lrc");
                File.WriteAllText(lrcPath, lyrics, Encoding.UTF8);
                return true;
            }
        }
        catch { }

        return false;
    }

    public static async Task<SearchResult> GetMetadataAsync(string userInput, bool isSearch = false, int limit = 10,
        string? cookiesBrowser = null, string? proxy = null,
        Action<string>? loggerCallback = null, string? cookiesPath = null, string searchSource = "YouTube Music")
    {
        if (isSearch && (searchSource == "YouTube Music" || searchSource == "YouTube (General)"))
        {
            try
            {
                var entries = new List<DownloadEntry>();

                var query = (searchSource == "YouTube Music" && !userInput.Contains("topic", StringComparison.OrdinalIgnoreCase))
                    ? $"{userInput}"
                    : userInput;

                await foreach (var video in _youtubeClient.Search.GetVideosAsync(query))
                {
                    if (entries.Count >= limit) break;

                    var durSec = video.Duration.HasValue ? (int?)video.Duration.Value.TotalSeconds : null;
                    entries.Add(new DownloadEntry
                    {
                        Id = video.Id.Value,
                        Title = video.Title,
                        Url = $"https://music.youtube.com/watch?v={video.Id.Value}",
                        Duration = durSec,
                        Uploader = video.Author.ChannelTitle
                    });
                }

                if (entries.Count > 0)
                {
                    if (searchSource == "YouTube Music")
                    {
                        entries = entries.OrderByDescending(e => MetadataHelpers.Score(e)).ToList();
                    }

                    loggerCallback?.Invoke($"[SISTEMA] Búsqueda en {searchSource} exitosa ({entries.Count} resultados).");
                    return new SearchResult
                    {
                        Type = "search",
                        Title = $"Resultados en {searchSource} para: {userInput}",
                        Entries = entries
                    };
                }
            }
            catch (Exception e)
            {
                loggerCallback?.Invoke($"[AVISO] YoutubeExplode ({e.Message}), consultando con yt-dlp...");
            }
        }

        return await Task.Run(() => GetMetadataFromYtdlp(userInput, isSearch, limit, cookiesBrowser, proxy, loggerCallback, cookiesPath, searchSource));
    }

    public static SearchResult GetMetadata(string userInput, bool isSearch = false, int limit = 10,
        string? cookiesBrowser = null, string? proxy = null,
        Action<string>? loggerCallback = null, string? cookiesPath = null, string searchSource = "YouTube Music")
    {
        return GetMetadataAsync(userInput, isSearch, limit, cookiesBrowser, proxy, loggerCallback, cookiesPath, searchSource)
            .GetAwaiter().GetResult();
    }

    private static SearchResult GetMetadataFromYtdlp(string userInput, bool isSearch, int limit,
        string? cookiesBrowser, string? proxy, Action<string>? loggerCallback, string? cookiesPath, string searchSource = "YouTube Music")
    {
        try
        {
            var exe = BinaryManager.GetYtDlpPath();
            if (string.IsNullOrEmpty(exe))
            {
                loggerCallback?.Invoke("[ERROR] No se encontró yt-dlp.");
                return new SearchResult();
            }

            var args = new List<string> { "--ignore-config", "-J", "--flat-playlist", "--no-warnings" };

            if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
            {
                args.Add("--cookies");
                args.Add(cookiesPath);
            }
            else if (!string.IsNullOrEmpty(cookiesBrowser) && cookiesBrowser.ToLower() != "ninguno")
            {
                args.Add("--cookies-from-browser");
                args.Add(cookiesBrowser.ToLower());
            }

            if (!string.IsNullOrEmpty(proxy))
            {
                args.Add("--proxy");
                args.Add(proxy);
            }

            string query;
            if (isSearch)
            {
                if (searchSource == "SoundCloud")
                {
                    query = $"scsearch{limit}:{userInput}";
                }
                else if (searchSource == "YouTube Music")
                {
                    args.Add("--extractor-args");
                    args.Add("youtube:player_client=android,web;po_token=web+");
                    query = $"ytsearch{limit}:{userInput} topic";
                }
                else
                {
                    query = $"ytsearch{limit}:{userInput}";
                }
            }
            else
            {
                query = userInput;
            }

            args.Add("--");
            args.Add(query);

            var runResult = ProcessRunner.RunAsync(
                exe,
                args,
                timeoutMs: 45000,
                maxCapturedChars: 16 * 1024 * 1024).GetAwaiter().GetResult();

            if (runResult.Canceled)
            {
                return new SearchResult();
            }

            if (runResult.TimedOut)
            {
                loggerCallback?.Invoke("[AVISO] Tiempo de espera agotado al consultar metadatos con yt-dlp.");
                return new SearchResult();
            }

            if (runResult.OutputTruncated)
            {
                loggerCallback?.Invoke("[AVISO] La respuesta de metadatos superó el límite permitido.");
                return new SearchResult();
            }

            if (runResult.ExitCode != 0 && !string.IsNullOrWhiteSpace(runResult.StandardError))
            {
                loggerCallback?.Invoke($"[AVISO] yt-dlp: {runResult.StandardError.Trim()}");
            }

            var output = runResult.StandardOutput;
            if (string.IsNullOrWhiteSpace(output)) return new SearchResult();

            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            var results = new List<DownloadEntry>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in root.EnumerateArray())
                {
                    if (results.Count >= MaxMetadataEntries) break;
                    if (entry.ValueKind == JsonValueKind.Null) continue;
                    var item = ParseDownloadEntryFromJson(entry);
                    if (item != null) results.Add(item);
                }

                if (searchSource == "YouTube Music")
                {
                    results = results.OrderByDescending(e => MetadataHelpers.Score(e)).ToList();
                }

                var title = (root.TryGetProperty("title", out var tProp) ? tProp.GetString() : null)
                    ?? (root.TryGetProperty("album", out var aProp) ? aProp.GetString() : null)
                    ?? (root.TryGetProperty("playlist_title", out var pltProp) ? pltProp.GetString() : null)
                    ?? (root.TryGetProperty("playlist", out var plProp) ? plProp.GetString() : null)
                    ?? "Lista_Descargas";

                return new SearchResult
                {
                    Type = "playlist",
                    Title = title,
                    Entries = results
                };
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("entries", out var entriesProp) && entriesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in entriesProp.EnumerateArray())
                    {
                        if (results.Count >= MaxMetadataEntries) break;
                        if (entry.ValueKind == JsonValueKind.Null) continue;
                        var item = ParseDownloadEntryFromJson(entry);
                        if (item != null) results.Add(item);
                    }

                    if (searchSource == "YouTube Music")
                    {
                        results = results.OrderByDescending(e => MetadataHelpers.Score(e)).ToList();
                    }

                    var playlistTitle = (root.TryGetProperty("title", out var ptProp) ? ptProp.GetString() : null)
                        ?? (root.TryGetProperty("album", out var albProp) ? albProp.GetString() : null)
                        ?? (root.TryGetProperty("playlist_title", out var ptlProp) ? ptlProp.GetString() : null)
                        ?? (root.TryGetProperty("playlist", out var pProp) ? pProp.GetString() : null)
                        ?? (root.TryGetProperty("uploader", out var upProp) ? upProp.GetString() : null)
                        ?? "Lista_Descargas";

                    return new SearchResult
                    {
                        Type = isSearch ? "search" : "playlist",
                        Title = playlistTitle,
                        Entries = results
                    };
                }

                var singleItem = ParseDownloadEntryFromJson(root);
                if (singleItem != null)
                {
                    return new SearchResult
                    {
                        Type = "single",
                        Title = singleItem.Title ?? "Canción",
                        Entries = new List<DownloadEntry> { singleItem }
                    };
                }
            }

            return new SearchResult();
        }
        catch (Exception e)
        {
            loggerCallback?.Invoke($"[ERROR] Error al extraer metadatos: {e.Message}");
            return new SearchResult();
        }
    }

    private static DownloadEntry? ParseDownloadEntryFromJson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var id = element.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        var title = element.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
        var uploader = element.TryGetProperty("uploader", out var upProp) ? upProp.GetString() : null
            ?? (element.TryGetProperty("channel", out var chProp) ? chProp.GetString() : null)
            ?? (element.TryGetProperty("artist", out var artProp) ? artProp.GetString() : null)
            ?? (element.TryGetProperty("creator", out var crProp) ? crProp.GetString() : null);

        var url = (element.TryGetProperty("webpage_url", out var wpProp) ? wpProp.GetString() : null)
            ?? (element.TryGetProperty("original_url", out var origProp) ? origProp.GetString() : null)
            ?? (element.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null);

        if (string.IsNullOrEmpty(url))
        {
            if (!string.IsNullOrEmpty(id))
            {
                if (id.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || id.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    url = id;
                else
                    url = $"https://music.youtube.com/watch?v={id}";
            }
        }

        int? duration = null;
        if (element.TryGetProperty("duration", out var durProp))
        {
            if (durProp.ValueKind == JsonValueKind.Number && durProp.TryGetInt32(out var durInt))
                duration = durInt;
            else if (durProp.ValueKind == JsonValueKind.Number && durProp.TryGetDouble(out var durDbl))
                duration = (int)durDbl;
            else if (durProp.ValueKind == JsonValueKind.String)
                duration = ParseDuration(durProp.GetString());
        }

        return new DownloadEntry
        {
            Id = id,
            Title = title,
            Url = url,
            Duration = duration,
            Uploader = uploader
        };
    }

    private static int? ParseDuration(string? durationStr)
    {
        if (string.IsNullOrEmpty(durationStr) || !durationStr.Contains(":"))
            return null;

        var parts = durationStr.Split(':');
        try
        {
            if (parts.Length == 2) return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
            if (parts.Length == 3) return int.Parse(parts[0]) * 3600 + int.Parse(parts[1]) * 60 + int.Parse(parts[2]);
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Processes a list of raw song titles or URLs concurrently using a bounded Channel pipeline,
    /// yielding resolved DownloadEntry results in real time as they complete.
    /// </summary>
    public static async IAsyncEnumerable<DownloadEntry> ResolveBatchEntriesChannelAsync(
        IEnumerable<string> rawItems,
        string searchSource = "YouTube Music",
        string? cookiesBrowser = null,
        string? proxy = null,
        int maxParallelism = 3,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = System.Threading.Channels.Channel.CreateBounded<DownloadEntry>(new System.Threading.Channels.BoundedChannelOptions(50)
        {
            FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        var producerTask = Task.Run(async () =>
        {
            try
            {
                var batchSize = Math.Max(1, maxParallelism);
                var batch = new List<string>(batchSize);

                async Task<DownloadEntry> ResolveOne(string item)
                {
                    var trimmed = item.Trim();
                    var isUrl = Helpers.IsUrl(trimmed);
                    var entry = new DownloadEntry
                    {
                        Title = trimmed,
                        Url = isUrl ? trimmed : null
                    };

                    if (!isUrl)
                    {
                        try
                        {
                            var meta = await GetMetadataAsync(
                                trimmed,
                                isSearch: true,
                                limit: 1,
                                cookiesBrowser: cookiesBrowser,
                                proxy: proxy,
                                searchSource: searchSource);

                            if (meta.Entries.Count > 0)
                            {
                                entry = meta.Entries[0];
                            }
                        }
                        catch { }
                    }

                    return entry;
                }

                async Task FlushBatchAsync()
                {
                    var entries = await Task.WhenAll(batch.Select(ResolveOne));
                    foreach (var entry in entries)
                    {
                        await channel.Writer.WriteAsync(entry, cancellationToken);
                    }
                    batch.Clear();
                }

                foreach (var item in rawItems.Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    batch.Add(item);

                    if (batch.Count >= batchSize)
                    {
                        await FlushBatchAsync();
                    }
                }

                if (batch.Count > 0)
                {
                    await FlushBatchAsync();
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        while (await channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (channel.Reader.TryRead(out var resolvedEntry))
            {
                yield return resolvedEntry;
            }
        }

        await producerTask;
    }
}