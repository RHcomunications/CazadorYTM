namespace CazadorYTM.Gui.Services;

using System.IO;
using CazadorYTM.Core;

public sealed record DownloadPreferencesState(
    string Format,
    string Bitrate,
    int Concurrent,
    string Browser,
    bool SponsorBlock,
    bool Normalize,
    bool EmbedLyrics,
    bool Enumerate,
    string DestinationFolder,
    bool ClipboardMonitor);

public static class DownloadPreferences
{
    public static DownloadPreferencesState Load(AppConfig config, string fallbackDestination)
    {
        var savedDest = config.Get("destination_folder", "");
        var destination = string.IsNullOrWhiteSpace(savedDest) || !Directory.Exists(savedDest)
            ? fallbackDestination
            : savedDest;

        return new DownloadPreferencesState(
            config.Get("format", "mp3"),
            config.Get("bitrate", "320K"),
            config.Get("max_concurrent", 3),
            config.Get("cookies_browser", "Ninguno"),
            config.Get("sponsorblock", true),
            config.Get("normalize", true),
            config.Get("embed_lyrics", false),
            config.Get("enumerar", false),
            destination,
            config.Get("clipboard_monitor", false));
    }

    public static void Save(AppConfig config, DownloadPreferencesState state)
    {
        config.Set("format", state.Format);
        config.Set("bitrate", state.Bitrate);
        config.Set("max_concurrent", state.Concurrent);
        config.Set("cookies_browser", state.Browser);
        config.Set("sponsorblock", state.SponsorBlock);
        config.Set("normalize", state.Normalize);
        config.Set("embed_lyrics", state.EmbedLyrics);
        config.Set("enumerar", state.Enumerate);
        config.Set("destination_folder", state.DestinationFolder);
        config.Set("clipboard_monitor", state.ClipboardMonitor);
        config.Save();
    }
}
