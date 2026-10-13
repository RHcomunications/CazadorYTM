namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        Action<string>? logger = null)
    {
        return await Task.Run(() =>
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

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                psi.ArgumentList.Add("-J");
                psi.ArgumentList.Add("--flat-playlist");
                psi.ArgumentList.Add("--no-warnings");

                // Configurar cookies para acceder a la cuenta autenticada
                if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
                {
                    psi.ArgumentList.Add("--cookies");
                    psi.ArgumentList.Add(cookiesPath);
                }
                else if (!string.IsNullOrEmpty(cookiesBrowser) && !cookiesBrowser.Equals("ninguno", StringComparison.OrdinalIgnoreCase))
                {
                    psi.ArgumentList.Add("--cookies-from-browser");
                    psi.ArgumentList.Add(cookiesBrowser.ToLower());
                }

                // Identificador especial de YouTube / YTM según la sección
                string targetUrl = section switch
                {
                    RadarSection.History => ":ythistory",
                    RadarSection.LikedSongs => "https://music.youtube.com/playlist?list=LM", // Liked Music playlist
                    RadarSection.Playlists => ":ytsubscriptions",
                    _ => ":ythistory"
                };

                psi.ArgumentList.Add(targetUrl);

                using var process = Process.Start(psi);
                if (process == null) return results;

                var output = process.StandardOutput.ReadToEnd();
                var err = process.StandardError.ReadToEnd();
                process.WaitForExit(30000);

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
            }

            return results;
        });
    }

    public static async Task<List<RadarItem>> FetchPlaylistTracksAsync(
        string playlistUrlOrId,
        string? cookiesBrowser = null,
        string? cookiesPath = null,
        Action<string>? logger = null)
    {
        return await Task.Run(() =>
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

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                psi.ArgumentList.Add("-J");
                psi.ArgumentList.Add("--flat-playlist");
                psi.ArgumentList.Add("--no-warnings");

                if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
                {
                    psi.ArgumentList.Add("--cookies");
                    psi.ArgumentList.Add(cookiesPath);
                }
                else if (!string.IsNullOrEmpty(cookiesBrowser) && !cookiesBrowser.Equals("ninguno", StringComparison.OrdinalIgnoreCase))
                {
                    psi.ArgumentList.Add("--cookies-from-browser");
                    psi.ArgumentList.Add(cookiesBrowser.ToLower());
                }

                string targetUrl = playlistUrlOrId;
                if (!Helpers.IsUrl(targetUrl))
                {
                    targetUrl = $"https://music.youtube.com/playlist?list={playlistUrlOrId}";
                }

                psi.ArgumentList.Add(targetUrl);

                using var process = Process.Start(psi);
                if (process == null) return results;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(30000);

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
            }

            return results;
        });
    }

    public static async Task<string?> GetStreamUrlAsync(string videoUrlOrId, string? cookiesBrowser = null, string? cookiesPath = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                var exe = BinaryManager.GetYtDlpPath();
                if (!File.Exists(exe)) return null;

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                psi.ArgumentList.Add("-g");
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add("bestaudio/best");
                psi.ArgumentList.Add("--no-warnings");

                if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
                {
                    psi.ArgumentList.Add("--cookies");
                    psi.ArgumentList.Add(cookiesPath);
                }
                else if (!string.IsNullOrEmpty(cookiesBrowser) && !cookiesBrowser.Equals("ninguno", StringComparison.OrdinalIgnoreCase))
                {
                    psi.ArgumentList.Add("--cookies-from-browser");
                    psi.ArgumentList.Add(cookiesBrowser.ToLower());
                }

                psi.ArgumentList.Add(videoUrlOrId);

                using var process = Process.Start(psi);
                if (process == null) return null;

                var streamUrl = process.StandardOutput.ReadLine();
                process.WaitForExit(15000);

                if (!string.IsNullOrWhiteSpace(streamUrl) && Helpers.IsUrl(streamUrl.Trim()))
                {
                    return streamUrl.Trim();
                }
            }
            catch { }
            return null;
        });
    }
}
