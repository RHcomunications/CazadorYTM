namespace CazadorYTM.Gui.Tests;

using System;
using System.IO;
using CazadorYTM.Core;
using CazadorYTM.Gui.Services;

public class DownloadPreferencesTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public DownloadPreferencesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dlprefs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Load_Defaults_MatchAppConfigDefaults()
    {
        var config = new AppConfig(_configPath);
        config.Set("destination_folder", "");

        var state = DownloadPreferences.Load(config, "fallback");

        Assert.Equal("mp3", state.Format);
        Assert.Equal("320K", state.Bitrate);
        Assert.Equal(3, state.Concurrent);
        Assert.Equal("Ninguno", state.Browser);
        Assert.False(state.SponsorBlock);
        Assert.False(state.Normalize);
        Assert.False(state.EmbedLyrics);
        Assert.False(state.Enumerate);
        Assert.Equal("fallback", state.DestinationFolder);
        Assert.False(state.ClipboardMonitor);
    }

    [Fact]
    public void Load_ReadsSavedValues()
    {
        var config = new AppConfig(_configPath);
        config.Set("format", "flac");
        config.Set("bitrate", "192K");
        config.Set("max_concurrent", 5);
        config.Set("cookies_browser", "Chrome");
        config.Set("sponsorblock", false);
        config.Set("normalize", false);
        config.Set("embed_lyrics", true);
        config.Set("enumerar", true);
        config.Set("destination_folder", _tempDir);
        config.Set("clipboard_monitor", true);
        config.Save();

        var state = DownloadPreferences.Load(new AppConfig(_configPath), "fallback");

        Assert.Equal("flac", state.Format);
        Assert.Equal("192K", state.Bitrate);
        Assert.Equal(5, state.Concurrent);
        Assert.Equal("Chrome", state.Browser);
        Assert.False(state.SponsorBlock);
        Assert.False(state.Normalize);
        Assert.True(state.EmbedLyrics);
        Assert.True(state.Enumerate);
        Assert.Equal(_tempDir, state.DestinationFolder);
        Assert.True(state.ClipboardMonitor);
    }

    [Fact]
    public void Load_NonExistingDestination_UsesFallback()
    {
        var config = new AppConfig(_configPath);
        config.Set("destination_folder", Path.Combine(_tempDir, "missing-folder"));

        var state = DownloadPreferences.Load(config, "fallback");

        Assert.Equal("fallback", state.DestinationFolder);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllValues()
    {
        var config = new AppConfig(_configPath);
        var original = new DownloadPreferencesState(
            "opus", "128K", 2, "Firefox", false, false, true, true, _tempDir, true);

        DownloadPreferences.Save(config, original);
        var loaded = DownloadPreferences.Load(new AppConfig(_configPath), "fallback");

        Assert.Equal(original, loaded);
    }
}
