namespace CazadorYTM.Tests;

using System.Linq;
using Xunit;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;

public class DownloadEngineAndMetadataTests
{
    [Fact]
    public void GetCliArgs_ConstructsValidArguments()
    {
        var args = DownloadEngine.GetCliArgs(
            formatType: "mp3",
            bitrate: "320k",
            sponsorblock: true,
            cookiesBrowser: "chrome",
            normalize: true,
            embedLyrics: true);

        Assert.Contains("--concurrent-fragments", args);
        Assert.Contains("--embed-thumbnail", args);
        Assert.Contains("--embed-metadata", args);
        Assert.Contains("--audio-format", args);
        Assert.Contains("mp3", args);
        Assert.Contains("--audio-quality", args);
        Assert.Contains("320k", args);
        Assert.Contains("--sponsorblock-remove", args);
        Assert.Contains("--embed-subs", args);
    }

    [Fact]
    public void ScoreResult_PrioritizesTopicAndAudioTags()
    {
        var highQualityEntry = new DownloadEntry
        {
            Title = "Song Title (Audio)",
            Uploader = "Artist - Topic"
        };

        var lowQualityEntry = new DownloadEntry
        {
            Title = "Song Title (Official Music Video 4K)",
            Uploader = "Random Uploader"
        };

        var scoreHigh = MetadataService.ScoreResult(highQualityEntry);
        var scoreLow = MetadataService.ScoreResult(lowQualityEntry);

        Assert.True(scoreHigh > scoreLow);
    }

    [Theory]
    [InlineData("01 - Artist Name - Track Title.mp3", "Track Title", "Artist Name")]
    [InlineData("Artist Name - Track Title.mp3", "Track Title", "Artist Name")]
    [InlineData("SingleTrack.mp3", "SingleTrack", "")]
    public void ExtractTitleArtistFromFilename_ExtractsProperly(string filename, string expectedTitle, string expectedArtist)
    {
        var (title, artist) = MetadataService.ExtractTitleArtistFromFilename(filename);
        Assert.Equal(expectedTitle, title);
        Assert.Equal(expectedArtist, artist);
    }
}
