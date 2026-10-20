namespace CazadorYTM.Gui.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;

public sealed class RadarActionService
{
    private static readonly Regex VideoIdRegex = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);

    private readonly string _language;
    private readonly string _cookiesBrowser;
    private List<RadarItem>? _playlists;

    public RadarActionService()
    {
        var config = new AppConfig();
        var cookiesPath = config.Get("cookies_path", Helpers.GetDefaultCookiesPath());
        CookiesPath = !string.IsNullOrWhiteSpace(cookiesPath) && File.Exists(cookiesPath)
            ? cookiesPath
            : null;
        _language = config.Get("language", "es");
        _cookiesBrowser = config.Get("cookies_browser", "Ninguno");
        IsSessionConnected = CookiesPath != null && YouTubeMusicSession.TryCreate(CookiesPath) != null;
    }

    public string? CookiesPath { get; }

    public bool IsSessionConnected { get; }

    public static string? GetVideoId(DownloadEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Id) && VideoIdRegex.IsMatch(entry.Id))
        {
            return entry.Id;
        }

        if (string.IsNullOrWhiteSpace(entry.Url) ||
            !Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var shortId = uri.AbsolutePath.Trim('/');
            return VideoIdRegex.IsMatch(shortId) ? shortId : null;
        }

        if (!uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (uri.AbsolutePath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase))
        {
            var pathId = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            return pathId != null && VideoIdRegex.IsMatch(pathId) ? pathId : null;
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == "v")
            {
                var videoId = Uri.UnescapeDataString(parts[1]);
                return VideoIdRegex.IsMatch(videoId) ? videoId : null;
            }
        }

        return null;
    }

    public async Task<string> SetRatingAsync(string videoId, string trackTitle, RadarRating rating)
    {
        await RadarService.SetTrackRatingAsync(videoId, rating, CookiesPath, _language);

        var loc = LocalizationService.Instance;
        var template = rating switch
        {
            RadarRating.Like => loc["RadarStatusLikeSaved"],
            RadarRating.Dislike => loc["RadarStatusDislikeSaved"],
            _ => loc["RadarStatusRatingRemoved"]
        };

        return Format(template, trackTitle);
    }

    public async Task<string?> AddToPlaylistAsync(string videoId, string trackTitle)
    {
        if (_playlists == null)
        {
            var items = await RadarService.FetchRadarItemsAsync(
                RadarSection.Playlists,
                cookiesBrowser: _cookiesBrowser,
                cookiesPath: CookiesPath,
                logger: msg => LogService.Instance.Info(msg),
                language: _language);

            _playlists = items
                .Where(p => p.IsPlaylist && p.Id != "VLLM" && p.Id != "LM")
                .ToList();
        }

        var selection = DialogService.Instance.ShowPlaylistPicker(trackTitle, _playlists);
        if (selection == null)
        {
            return null;
        }

        var loc = LocalizationService.Instance;

        if (!string.IsNullOrWhiteSpace(selection.NewPlaylistTitle))
        {
            await RadarService.CreatePlaylistWithTrackAsync(
                selection.NewPlaylistTitle!, videoId, CookiesPath, _language);
            _playlists = null;
            return Format(loc["RadarStatusPlaylistCreated"],
                selection.NewPlaylistTitle!, trackTitle);
        }

        if (string.IsNullOrWhiteSpace(selection.PlaylistId))
        {
            return null;
        }

        await RadarService.AddTrackToPlaylistAsync(
            selection.PlaylistId!, videoId, CookiesPath, _language);

        var playlistTitle = _playlists.FirstOrDefault(p => p.Id == selection.PlaylistId)?.Title
            ?? selection.PlaylistId!;
        return Format(loc["RadarStatusAddedToPlaylist"], trackTitle, playlistTitle);
    }

    private static string Format(string template, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            template = template.Replace("{" + i + "}", values[i]);
        }

        return template;
    }
}
