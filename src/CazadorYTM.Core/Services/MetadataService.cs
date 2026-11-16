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
    private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private static readonly YoutubeClient _youtubeClient = new(_httpClient);
    private const int MaxMetadataEntries = 10000;

    static MetadataService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"CazadorYTM/{Constants.AppVersion} (https://github.com/{Constants.GitHubRepo})");
    }

    public static List<string> BuildLrclibCandidateUrls(string trackName, string? artistName = null, int? duration = null)
    {
        var urls = new List<string>();
        var trimmedTrack = trackName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedTrack))
        {
            return urls;
        }

        var trimmedArtist = string.IsNullOrWhiteSpace(artistName) ? null : artistName.Trim();

        // LRCLIB /api/get requires both track_name and artist_name; duration (1..3600s) enables +-2s exact matching.
        if (trimmedArtist != null && duration is >= 1 and <= 3600)
        {
            urls.Add($"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(trimmedTrack)}&artist_name={Uri.EscapeDataString(trimmedArtist)}&duration={duration.Value}");
        }

        // LRCLIB /api/search: omit 'q' when using structured track_name + artist_name because 'q' overrides structured parameters.
        if (trimmedArtist != null)
        {
            urls.Add($"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(trimmedTrack)}&artist_name={Uri.EscapeDataString(trimmedArtist)}");
            urls.Add($"https://lrclib.net/api/search?q={Uri.EscapeDataString($"{trimmedTrack} {trimmedArtist}")}");
        }
        else
        {
            urls.Add($"https://lrclib.net/api/search?q={Uri.EscapeDataString(trimmedTrack)}");
        }

        return urls;
    }

    public static string? ExtractLyricsFromLrclibJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object)
        {
            return ExtractLyricsFromRecord(root);
        }

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            string? plainFallback = null;
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (item.TryGetProperty("syncedLyrics", out var synced) && synced.ValueKind == JsonValueKind.String)
                {
                    var s = synced.GetString();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                if (plainFallback == null && item.TryGetProperty("plainLyrics", out var plain) && plain.ValueKind == JsonValueKind.String)
                {
                    var p = plain.GetString();
                    if (!string.IsNullOrEmpty(p)) plainFallback = p;
                }
            }
            return plainFallback;
        }

        return null;
    }

    private static string? ExtractLyricsFromRecord(JsonElement record)
    {
        if (record.TryGetProperty("syncedLyrics", out var synced) && synced.ValueKind == JsonValueKind.String)
        {
            var s = synced.GetString();
            if (!string.IsNullOrEmpty(s)) return s;
        }
        if (record.TryGetProperty("plainLyrics", out var plain) && plain.ValueKind == JsonValueKind.String)
        {
            var p = plain.GetString();
            if (!string.IsNullOrEmpty(p)) return p;
        }
        return null;
    }

    public static async Task<string?> FetchLyricsLrclibAsync(string trackName, string? artistName = null, int? duration = null, System.Threading.CancellationToken cancellationToken = default)
    {
        var candidateUrls = BuildLrclibCandidateUrls(trackName, artistName, duration);
        foreach (var url in candidateUrls)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    break;
                }
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var lyrics = ExtractLyricsFromLrclibJson(content);
                if (!string.IsNullOrEmpty(lyrics))
                {
                    return lyrics;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { }
        }

        return null;
    }

    public static async Task<bool> TryWriteLyricsAsync(string audioFilePath, string trackName, string? artistName = null, int? duration = null, System.Threading.CancellationToken cancellationToken = default)
    {
        try
        {
            var lyrics = await FetchLyricsLrclibAsync(trackName, artistName, duration, cancellationToken);
            if (!string.IsNullOrEmpty(lyrics))
            {
                var lrcPath = Path.ChangeExtension(audioFilePath, ".lrc");
                await File.WriteAllTextAsync(lrcPath, lyrics, Encoding.UTF8, cancellationToken);
                return true;
            }
        }
        catch { }

        return false;
    }

    public static bool TryWriteLyrics(string audioFilePath, string trackName, string? artistName = null, int? duration = null)
    {
        return TryWriteLyricsAsync(audioFilePath, trackName, artistName, duration).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Loads lyrics from a local .lrc sidecar file or retrieves them from the LRCLIB service.
    /// Returns parsed LyricLine collection ordered chronologically.
    /// </summary>
    public static async Task<List<LyricLine>> LoadLyricsAsync(string? audioFilePath, string trackName, string? artistName = null, int? duration = null, System.Threading.CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(audioFilePath) && !Helpers.IsUrl(audioFilePath))
        {
            try
            {
                var lrcPath = Path.ChangeExtension(audioFilePath, ".lrc");
                if (File.Exists(lrcPath))
                {
                    var raw = await File.ReadAllTextAsync(lrcPath, Encoding.UTF8, cancellationToken);
                    var parsed = LyricLine.ParseLrc(raw);
                    if (parsed.Count > 0)
                        return parsed;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { }
        }

        var onlineRaw = await FetchLyricsLrclibAsync(trackName, artistName, duration, cancellationToken);
        if (!string.IsNullOrEmpty(onlineRaw))
        {
            return LyricLine.ParseLrc(onlineRaw);
        }

        return new List<LyricLine>();
    }

    public static async Task<SearchResult> GetMetadataAsync(string userInput, bool isSearch = false, int limit = 10,
        string? cookiesBrowser = null, string? proxy = null,
        Action<string>? loggerCallback = null, string? cookiesPath = null, string searchSource = "YouTube Music",
        System.Threading.CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (isSearch && (searchSource == "YouTube Music" || searchSource == "YouTube (General)"))
        {
            try
            {
                var entries = new List<DownloadEntry>();

                await foreach (var video in _youtubeClient.Search.GetVideosAsync(userInput, cancellationToken))
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                loggerCallback?.Invoke($"[AVISO] YoutubeExplode ({e.Message}), consultando con yt-dlp...");
            }
        }

        return await Task.Run(() => GetMetadataFromYtdlp(userInput, isSearch, limit, cookiesBrowser, proxy, loggerCallback, cookiesPath, searchSource, cancellationToken), cancellationToken);
    }

    public static SearchResult GetMetadata(string userInput, bool isSearch = false, int limit = 10,
        string? cookiesBrowser = null, string? proxy = null,
        Action<string>? loggerCallback = null, string? cookiesPath = null, string searchSource = "YouTube Music")
    {
        return GetMetadataAsync(userInput, isSearch, limit, cookiesBrowser, proxy, loggerCallback, cookiesPath, searchSource)
            .GetAwaiter().GetResult();
    }

    private static SearchResult GetMetadataFromYtdlp(string userInput, bool isSearch, int limit,
        string? cookiesBrowser, string? proxy, Action<string>? loggerCallback, string? cookiesPath, string searchSource = "YouTube Music",
        System.Threading.CancellationToken cancellationToken = default)
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
            YtDlpArgumentBuilder.AppendCookieArgs(args, cookiesPath, cookiesBrowser);

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
                maxCapturedChars: 16 * 1024 * 1024,
                cancellationToken: cancellationToken).GetAwaiter().GetResult();

            if (runResult.Canceled)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
                                searchSource: searchSource,
                                cancellationToken: cancellationToken);

                            if (meta.Entries.Count > 0)
                            {
                                entry = meta.Entries[0];
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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