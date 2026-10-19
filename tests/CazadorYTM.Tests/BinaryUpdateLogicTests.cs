namespace CazadorYTM.Tests;

using CazadorYTM.Core.Services;

public class BinaryUpdateLogicTests
{
    [Fact]
    public void ParseYtdlpGithubTag_StripsLeadingV()
        => Assert.Equal("2024.01.01", BinaryUpdateLogic.ParseYtdlpGithubTag("{\"tag_name\":\"v2024.01.01\"}"));

    [Fact]
    public void ParseYtdlpGithubTag_TrimsWhitespace()
        => Assert.Equal("2024.1", BinaryUpdateLogic.ParseYtdlpGithubTag("{\"tag_name\":\"  v2024.1  \"}"));

    [Fact]
    public void ParseYtdlpGithubTag_BlankTag_ReturnsNull()
        => Assert.Null(BinaryUpdateLogic.ParseYtdlpGithubTag("{\"tag_name\":\"   \"}"));

    [Fact]
    public void ParseYtdlpGithubTag_MissingTag_ReturnsNull()
        => Assert.Null(BinaryUpdateLogic.ParseYtdlpGithubTag("{}"));

    [Fact]
    public void ParseYtdlpGithubTag_InvalidJson_ReturnsNull()
        => Assert.Null(BinaryUpdateLogic.ParseYtdlpGithubTag("not json"));

    [Fact]
    public void ParseYtdlpPyPiVersion_ReadsInfoVersion()
        => Assert.Equal("2024.1.1", BinaryUpdateLogic.ParseYtdlpPyPiVersion("{\"info\":{\"version\":\"2024.1.1\"}}"));

    [Fact]
    public void ParseYtdlpPyPiVersion_InvalidJson_ReturnsNull()
        => Assert.Null(BinaryUpdateLogic.ParseYtdlpPyPiVersion("{}"));

    [Theory]
    [InlineData("{\"tag_name\":\"v1.2.3\"}", "1.2.3")]
    [InlineData("{\"tag_name\":\"V1.2.3\"}", "1.2.3")]
    [InlineData("{\"tag_name\":\"1.2.3\"}", "1.2.3")]
    [InlineData("{\"tag_name\":null}", "")]
    public void ParseGithubTag_NormalizesTag(string json, string expected)
        => Assert.Equal(expected, BinaryUpdateLogic.ParseGithubTag(json));

    [Fact]
    public void ParseGithubTag_InvalidJson_ReturnsNull()
        => Assert.Null(BinaryUpdateLogic.ParseGithubTag("<html>"));

    [Fact]
    public void ParseFfmpegBuildDate_FormatsPublishedAt()
        => Assert.Equal("2024.05.06", BinaryUpdateLogic.ParseFfmpegBuildDate("{\"published_at\":\"2024-05-06T12:00:00Z\"}"));

    [Fact]
    public void ParseFfmpegBuildDate_Missing_ReturnsNull()
        => Assert.Null(BinaryUpdateLogic.ParseFfmpegBuildDate("{}"));

    [Fact]
    public void ParseFfmpegBuildDate_Empty_ReturnsNull()
        => Assert.Null(BinaryUpdateLogic.ParseFfmpegBuildDate("{\"published_at\":\"\"}"));

    [Fact]
    public void ParseFfmpegBuildDate_InvalidDate_ReturnsNull()
        => Assert.Null(BinaryUpdateLogic.ParseFfmpegBuildDate("{\"published_at\":\"not-a-date\"}"));

    [Fact]
    public void BuildUpdatesNeeded_AllMissing_ReturnsThreeMissingEntries()
    {
        var result = BinaryUpdateLogic.BuildUpdatesNeeded(
            false, null, null,
            false, null, null,
            false, null, null);

        Assert.Equal(new[] { "yt-dlp (Faltante)", "Deno (Faltante)", "FFmpeg (Faltante)" }, result);
    }

    [Fact]
    public void BuildUpdatesNeeded_NothingToUpdate_ReturnsEmpty()
    {
        var result = BinaryUpdateLogic.BuildUpdatesNeeded(
            true, "2024.1", "2024.1",
            true, "1.0", "1.0",
            true, "2024.01.01", "2024.01.01");

        Assert.Empty(result);
    }

    [Fact]
    public void BuildUpdatesNeeded_RemoteNewer_AddsEntryWithVersions()
    {
        var result = BinaryUpdateLogic.BuildUpdatesNeeded(
            true, "2024.1", "2024.2",
            true, "1.0", "1.0",
            true, "2024.01.01", "2024.01.01");

        Assert.Single(result);
        Assert.Equal("yt-dlp (Instalado: 2024.1 -> Disponible: 2024.2)", result[0]);
    }

    [Fact]
    public void BuildUpdatesNeeded_RemoteOlderOrEqual_DoesNotAdd()
    {
        var result = BinaryUpdateLogic.BuildUpdatesNeeded(
            true, "2024.2", "2024.1",
            true, "1.0", "1.0",
            true, "2024.01.01", "2024.01.01");

        Assert.Empty(result);
    }

    [Fact]
    public void BuildUpdatesNeeded_RemoteNull_DoesNotAdd()
    {
        var result = BinaryUpdateLogic.BuildUpdatesNeeded(
            true, "2024.1", null,
            true, "1.0", null,
            true, "2024.01.01", null);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildUpdatesNeeded_InstalledButLocalEmpty_TreatedAsMissing()
    {
        var result = BinaryUpdateLogic.BuildUpdatesNeeded(
            true, "", "2024.2",
            true, "", "1.0",
            true, "", "2024.01.01");

        Assert.Equal(new[] { "yt-dlp (Faltante)", "Deno (Faltante)" }, result);
    }

    [Fact]
    public void BuildUpdatesNeeded_FfmpegLocalInstalado_DoesNotAdd()
    {
        var result = BinaryUpdateLogic.BuildUpdatesNeeded(
            true, "2024.1", "2024.1",
            true, "1.0", "1.0",
            true, "Instalado", "2024.01.01");

        Assert.Empty(result);
    }

    [Fact]
    public void BuildUpdatesNeeded_DenoNewer_AddsEntry()
    {
        var result = BinaryUpdateLogic.BuildUpdatesNeeded(
            true, "2024.1", "2024.1",
            true, "1.0", "2.0",
            true, "2024.01.01", "2024.01.01");

        Assert.Single(result);
        Assert.Equal("Deno (Instalado: 1.0 -> Disponible: 2.0)", result[0]);
    }

    [Fact]
    public void BuildUpdatesNeeded_FfmpegNewer_AddsEntry()
    {
        var result = BinaryUpdateLogic.BuildUpdatesNeeded(
            true, "2024.1", "2024.1",
            true, "1.0", "1.0",
            true, "2024.01.01", "2024.02.02");

        Assert.Single(result);
        Assert.Equal("FFmpeg (Instalado: 2024.01.01 -> Disponible: 2024.02.02)", result[0]);
    }

    [Fact]
    public void BuildUpdatesNeeded_MultipleUpdates_PreservesComponentOrder()
    {
        var result = BinaryUpdateLogic.BuildUpdatesNeeded(
            true, "1.0", "2.0",
            true, "1.0", "2.0",
            true, "2024.01.01", "2024.02.02");

        Assert.Equal(3, result.Count);
        Assert.StartsWith("yt-dlp", result[0]);
        Assert.StartsWith("Deno", result[1]);
        Assert.StartsWith("FFmpeg", result[2]);
    }
}
