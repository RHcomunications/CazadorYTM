namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;

public enum RadarSection
{
    History,
    LikedSongs,
    Playlists
}

public class RadarItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public string? Url { get; set; }
    public int? Duration { get; set; }
    public string? ThumbnailUrl { get; set; }
    public RadarSection Section { get; set; }
    public bool IsPlaylist { get; set; }

    public string DisplayDuration => Duration.HasValue ? Helpers.FormatDuration(Duration.Value) : "--:--";

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Artist) ? Title : $"{Title} — {Artist}";
    }

    public DownloadEntry ToDownloadEntry()
    {
        return new DownloadEntry
        {
            Id = Id,
            Title = Title,
            Uploader = Artist,
            Url = Url ?? (!string.IsNullOrEmpty(Id) ? $"https://music.youtube.com/watch?v={Id}" : null),
            Duration = Duration
        };
    }
}

public static class RadarService
{
    public static async Task<List<RadarItem>> FetchRadarItemsAsync(
        RadarSection section,
        string? cookiesBrowser = null,
        string? cookiesPath = null,
        Action<string>? logger = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = YouTubeMusicSession.TryCreate(cookiesPath);
        if (session != null)
        {
            try
            {
                logger?.Invoke($"[RADAR] Consultando {section} en YouTube Music...");
                var client = new YouTubeMusicClient(session, language);
                var items = await client.FetchSectionAsync(section, cancellationToken: cancellationToken);
                logger?.Invoke($"[RADAR] {items.Count} elementos obtenidos vía YouTube Music.");
                if (items.Count > 0)
                {
                    return items;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.Invoke($"[RADAR] YouTube Music no respondió ({ex.Message}); usando yt-dlp.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await FetchRadarItemsFromYtDlpAsync(section, cookiesBrowser, cookiesPath, logger, cancellationToken);
    }

    private static async Task<List<RadarItem>> FetchRadarItemsFromYtDlpAsync(
        RadarSection section,
        string? cookiesBrowser,
        string? cookiesPath,
        Action<string>? logger,
        CancellationToken cancellationToken = default)
    {
        var results = new List<RadarItem>();
        try
        {
            var exe = BinaryManager.GetYtDlpPath();
            if (!File.Exists(exe))
            {
                logger?.Invoke("[RADAR] yt-dlp no encontrado para consultar Radar.");
                return results;
            }

            var args = new List<string> { "-J", "--flat-playlist", "--no-warnings" };

            // Configurar cookies para acceder a la cuenta autenticada
            if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
            {
                args.Add("--cookies");
                args.Add(cookiesPath);
            }
            else if (!string.IsNullOrEmpty(cookiesBrowser) && !cookiesBrowser.Equals("ninguno", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--cookies-from-browser");
                args.Add(cookiesBrowser.ToLower());
            }

            // Identificador especial de YouTube / YTM según la sección
            string targetUrl = section switch
            {
                RadarSection.History => ":ythistory",
                RadarSection.LikedSongs => "https://music.youtube.com/playlist?list=LM", // Liked Music playlist
                RadarSection.Playlists => "https://www.youtube.com/feed/playlists",
                _ => ":ythistory"
            };

            args.Add(targetUrl);

            var runResult = await ProcessRunner.RunAsync(exe, args, timeoutMs: 35000, cancellationToken: cancellationToken);

            if (!runResult.Success)
            {
                var err = runResult.StandardError;
                if (err.Contains("Sign in", StringComparison.OrdinalIgnoreCase) ||
                    err.Contains("bot", StringComparison.OrdinalIgnoreCase) ||
                    err.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                    err.Contains("authentication", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Se requiere iniciar sesión en YouTube con cookies activas para acceder a esta sección.");
                }

                logger?.Invoke($"[RADAR] Error al consultar sección {section}: {err}");
                throw new InvalidOperationException(
                    $"No se pudo consultar la sección {section} de YouTube. Detalle: {FirstErrorLine(err)}");
            }

            var output = runResult.StandardOutput;
            if (!string.IsNullOrWhiteSpace(output))
            {
                using var doc = JsonDocument.Parse(output);
                var root = doc.RootElement;

                if (root.TryGetProperty("entries", out var entriesProp) && entriesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in entriesProp.EnumerateArray())
                    {
                        var title = el.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
                        var id = el.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                        var uploader = el.TryGetProperty("uploader", out var uProp) ? uProp.GetString() : null;
                        var url = el.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                        var dur = el.TryGetProperty("duration", out var dProp) && dProp.TryGetInt32(out var dVal) ? (int?)dVal : null;

                        if (string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(id))
                        {
                            url = $"https://music.youtube.com/watch?v={id}";
                        }

                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            results.Add(new RadarItem
                            {
                                Id = id,
                                Title = title,
                                Artist = uploader,
                                Url = url,
                                Duration = dur,
                                Section = section,
                                IsPlaylist = section == RadarSection.Playlists
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.Invoke($"[RADAR] Error al consultar sección {section}: {ex.Message}");
            throw;
        }

        return results;
    }

    public static async Task<List<RadarItem>> FetchPlaylistTracksAsync(
        string playlistUrlOrId,
        string? cookiesBrowser = null,
        string? cookiesPath = null,
        Action<string>? logger = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = YouTubeMusicSession.TryCreate(cookiesPath);
        var browseId = YouTubeMusicClient.ToPlaylistBrowseId(playlistUrlOrId);
        if (session != null && browseId != null)
        {
            try
            {
                logger?.Invoke("[RADAR] Cargando pistas desde YouTube Music...");
                var client = new YouTubeMusicClient(session, language);
                var items = await client.FetchPlaylistTracksAsync(browseId, cancellationToken: cancellationToken);
                logger?.Invoke($"[RADAR] {items.Count} pistas obtenidas vía YouTube Music.");
                if (items.Count > 0)
                {
                    return items;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.Invoke($"[RADAR] YouTube Music no respondió ({ex.Message}); usando yt-dlp.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await FetchPlaylistTracksFromYtDlpAsync(playlistUrlOrId, cookiesBrowser, cookiesPath, logger, cancellationToken);
    }

    private static async Task<List<RadarItem>> FetchPlaylistTracksFromYtDlpAsync(
        string playlistUrlOrId,
        string? cookiesBrowser,
        string? cookiesPath,
        Action<string>? logger,
        CancellationToken cancellationToken = default)
    {
        var results = new List<RadarItem>();
        try
        {
            var exe = BinaryManager.GetYtDlpPath();
            if (!File.Exists(exe))
            {
                logger?.Invoke("[RADAR] yt-dlp no encontrado.");
                return results;
            }

            var args = new List<string> { "-J", "--flat-playlist", "--no-warnings" };

            if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
            {
                args.Add("--cookies");
                args.Add(cookiesPath);
            }
            else if (!string.IsNullOrEmpty(cookiesBrowser) && !cookiesBrowser.Equals("ninguno", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--cookies-from-browser");
                args.Add(cookiesBrowser.ToLower());
            }

            string targetUrl = playlistUrlOrId;
            if (!Helpers.IsUrl(targetUrl))
            {
                targetUrl = $"https://music.youtube.com/playlist?list={playlistUrlOrId}";
            }

            args.Add(targetUrl);

            var runResult = await ProcessRunner.RunAsync(exe, args, timeoutMs: 35000, cancellationToken: cancellationToken);

            if (!runResult.Success)
            {
                var err = runResult.StandardError;
                if (err.Contains("Sign in", StringComparison.OrdinalIgnoreCase) ||
                    err.Contains("Private playlist", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Esta lista de reproducción requiere sesión de YouTube iniciada para acceder a sus pistas.");
                }

                logger?.Invoke($"[RADAR] Error al obtener pistas de la lista: {err}");
                throw new InvalidOperationException(
                    $"No se pudieron cargar las pistas de la lista. Detalle: {FirstErrorLine(err)}");
            }

            var output = runResult.StandardOutput;
            if (!string.IsNullOrWhiteSpace(output))
            {
                using var doc = JsonDocument.Parse(output);
                var root = doc.RootElement;

                if (root.TryGetProperty("entries", out var entriesProp) && entriesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in entriesProp.EnumerateArray())
                    {
                        var title = el.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
                        var id = el.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                        var uploader = el.TryGetProperty("uploader", out var uProp) ? uProp.GetString() :
                                      (el.TryGetProperty("channel", out var cProp) ? cProp.GetString() : null);
                        var url = el.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                        var dur = el.TryGetProperty("duration", out var dProp) && dProp.TryGetInt32(out var dVal) ? (int?)dVal : null;

                        if (string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(id))
                        {
                            url = $"https://music.youtube.com/watch?v={id}";
                        }

                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            results.Add(new RadarItem
                            {
                                Id = id,
                                Title = title,
                                Artist = uploader,
                                Url = url,
                                Duration = dur,
                                Section = RadarSection.Playlists,
                                IsPlaylist = false
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.Invoke($"[RADAR] Error al obtener pistas de la lista: {ex.Message}");
            throw;
        }

        return results;
    }

    public static async Task SetTrackRatingAsync(
        string videoId,
        RadarRating rating,
        string? cookiesPath = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var client = CreateWriteClient(cookiesPath, language);
        await client.SetTrackRatingAsync(videoId, rating, cancellationToken);
    }

    public static async Task AddTrackToPlaylistAsync(
        string playlistId,
        string videoId,
        string? cookiesPath = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var client = CreateWriteClient(cookiesPath, language);
        await client.AddTrackToPlaylistAsync(playlistId, videoId, cancellationToken);
    }

    public static async Task<string> CreatePlaylistWithTrackAsync(
        string title,
        string videoId,
        string? cookiesPath = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var client = CreateWriteClient(cookiesPath, language);
        return await client.CreatePlaylistWithTrackAsync(title, videoId, cancellationToken);
    }

    private static YouTubeMusicClient CreateWriteClient(string? cookiesPath, string? language)
    {
        var session = YouTubeMusicSession.TryCreate(cookiesPath);
        if (session == null)
        {
            throw new InvalidOperationException(
                "Conecta tu sesión de YouTube Music para valorar pistas o modificar listas.");
        }

        return new YouTubeMusicClient(session, language);
    }

    public static async Task<string?> GetStreamUrlAsync(
        string videoUrlOrId,
        string? cookiesBrowser = null,
        string? cookiesPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exe = BinaryManager.GetYtDlpPath();
            if (!File.Exists(exe)) return null;

            var args = new List<string> { "-g", "-f", "bestaudio/best", "--no-warnings" };

            if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
            {
                args.Add("--cookies");
                args.Add(cookiesPath);
            }
            else if (!string.IsNullOrEmpty(cookiesBrowser) && !cookiesBrowser.Equals("ninguno", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--cookies-from-browser");
                args.Add(cookiesBrowser.ToLower());
            }

            args.Add(videoUrlOrId);

            var runResult = await ProcessRunner.RunAsync(exe, args, timeoutMs: 15000, cancellationToken: cancellationToken);

            if (runResult.Success && !string.IsNullOrWhiteSpace(runResult.StandardOutput))
            {
                var lines = runResult.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (Helpers.IsUrl(trimmed))
                    {
                        return trimmed;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private static string FirstErrorLine(string stderr)
    {
        string? fallback = null;
        foreach (var raw in stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            fallback ??= line;
            if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)) return line;
        }

        return fallback ?? "error desconocido de yt-dlp";
    }
}
