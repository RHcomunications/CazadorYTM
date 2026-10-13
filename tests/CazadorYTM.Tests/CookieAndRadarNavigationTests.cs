namespace CazadorYTM.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using CazadorYTM.Core.Services;

public class CookieAndRadarNavigationTests : IDisposable
{
    private readonly string _tempDir;

    public CookieAndRadarNavigationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cazador_cookie_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void CookieService_NetscapeToJson_ConvertsCorrectly()
    {
        var netscape = "# Netscape HTTP Cookie File\n" +
                       ".youtube.com\tTRUE\t/\tTRUE\t1790000000\tSID\tsample_sid_token_123\n" +
                       ".youtube.com\tTRUE\t/\tTRUE\t1790000000\tHSID\tsample_hsid_token_456\n";

        var json = CookieService.NetscapeToJson(netscape);

        Assert.Contains("SID", json);
        Assert.Contains("sample_sid_token_123", json);
        Assert.Contains("HSID", json);
        Assert.Contains(".youtube.com", json);
    }

    [Fact]
    public void CookieService_JsonToNetscape_ConvertsCorrectly()
    {
        var json = "[\n" +
                   "  {\n" +
                   "    \"domain\": \".youtube.com\",\n" +
                   "    \"path\": \"/\",\n" +
                   "    \"secure\": true,\n" +
                   "    \"expirationDate\": 1790000000,\n" +
                   "    \"name\": \"LOGIN_INFO\",\n" +
                   "    \"value\": \"sample_login_info_val\"\n" +
                   "  }\n" +
                   "]";

        var netscape = CookieService.JsonToNetscape(json);

        Assert.Contains("# Netscape HTTP Cookie File", netscape);
        Assert.Contains(".youtube.com\tTRUE\t/\tTRUE\t1790000000\tLOGIN_INFO\tsample_login_info_val", netscape);
    }

    [Fact]
    public async Task CookieService_SaveCookiesFromTextAsync_Netscape_DetectsAuthCookies()
    {
        var targetFile = Path.Combine(_tempDir, "cookies.txt");
        var rawNetscape = "# Netscape HTTP Cookie File\n" +
                          ".youtube.com\tTRUE\t/\tTRUE\t1790000000\tSID\tabc123\n" +
                          ".youtube.com\tTRUE\t/\tTRUE\t1790000000\tLOGIN_INFO\txyz789\n";

        var result = await CookieService.SaveCookiesFromTextAsync(rawNetscape, targetFile, asJson: true);

        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.True(File.Exists(targetFile));

        var jsonFile = Path.ChangeExtension(targetFile, ".json");
        Assert.True(File.Exists(jsonFile));

        var status = CookieService.GetCookiesStatus(targetFile);
        Assert.True(status.Exists);
        Assert.True(status.HasYouTubeAuthCookies);
        Assert.Contains("SID", status.DetectedAuthKeys);
        Assert.Contains("LOGIN_INFO", status.DetectedAuthKeys);
    }

    [Fact]
    public async Task CookieService_SaveCookiesFromTextAsync_JsonFormat_ConvertsAndSaves()
    {
        var targetFile = Path.Combine(_tempDir, "cookies_from_json.txt");
        var rawJson = "[\n" +
                      "  {\n" +
                      "    \"domain\": \".youtube.com\",\n" +
                      "    \"path\": \"/\",\n" +
                      "    \"secure\": true,\n" +
                      "    \"expirationDate\": 1790000000,\n" +
                      "    \"name\": \"HSID\",\n" +
                      "    \"value\": \"hsid_value_from_json\"\n" +
                      "  }\n" +
                      "]";

        var result = await CookieService.SaveCookiesFromTextAsync(rawJson, targetFile);

        Assert.True(result.Success);
        Assert.Equal(1, result.Count);

        var content = File.ReadAllText(targetFile);
        Assert.Contains("# Netscape HTTP Cookie File", content);
        Assert.Contains("HSID", content);
    }

    [Fact]
    public async Task CookieService_ExportAndImport_PreservesIntegrity()
    {
        var originalFile = Path.Combine(_tempDir, "original.txt");
        var exportFile = Path.Combine(_tempDir, "exported.txt");
        var importFile = Path.Combine(_tempDir, "imported.txt");

        var raw = "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t1790000000\tSID\tsecret_sid\n";
        File.WriteAllText(originalFile, raw);

        var exportRes = await CookieService.ExportCookiesAsync(originalFile, exportFile);
        Assert.True(exportRes.Success);
        Assert.True(File.Exists(exportFile));

        var importRes = await CookieService.ImportCookiesAsync(exportFile, importFile);
        Assert.True(importRes.Success);
        Assert.True(File.Exists(importFile));

        var importedContent = File.ReadAllText(importFile);
        Assert.Contains("secret_sid", importedContent);
    }

    [Fact]
    public void RadarItem_PlaylistFlag_DistinguishesPlaylistsFromTracks()
    {
        var playlistItem = new RadarItem
        {
            Id = "PL12345",
            Title = "Mi Lista Favorita",
            IsPlaylist = true,
            Section = RadarSection.Playlists
        };

        var trackItem = new RadarItem
        {
            Id = "trk123",
            Title = "Canción Individual",
            IsPlaylist = false,
            Section = RadarSection.Playlists
        };

        Assert.True(playlistItem.IsPlaylist);
        Assert.False(trackItem.IsPlaylist);
    }
}
