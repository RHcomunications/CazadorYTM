namespace CazadorYTM.Tests;

using System.Linq;
using Xunit;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;

public class DownloadEngineAndMetadataTests
{
    [Theory]
    [InlineData("--exec calc.exe")]
    [InlineData("--output=otro.mp3")]
    [InlineData("-o otro.mp3")]
    [InlineData("--proxy=http://evil")]
    [InlineData("--downloader-args=--x")]
    public void Build_RejectsDangerousRawArguments(string rawArgs)
        => Assert.Throws<ArgumentException>(() => YtDlpArgumentBuilder.Build(rawArgs: rawArgs));

    [Fact]
    public void Build_ConstructsValidArguments()
    {
        var args = YtDlpArgumentBuilder.Build(
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

        var scoreHigh = MetadataHelpers.Score(highQualityEntry);
        var scoreLow = MetadataHelpers.Score(lowQualityEntry);

        Assert.True(scoreHigh > scoreLow);
    }

    [Theory]
    [InlineData("01 - Artist Name - Track Title.mp3", "Track Title", "Artist Name")]
    [InlineData("Artist Name - Track Title.mp3", "Track Title", "Artist Name")]
    [InlineData("SingleTrack.mp3", "SingleTrack", "")]
    public void ExtractTitleArtistFromFilename_ExtractsProperly(string filename, string expectedTitle, string expectedArtist)
    {
        var (title, artist) = MetadataHelpers.ExtractTitleArtist(filename);
        Assert.Equal(expectedTitle, title);
        Assert.Equal(expectedArtist, artist);
    }

    [Fact]
    public async System.Threading.Tasks.Task ResolveBatchEntriesChannelAsync_ProcessesItemsCorrectly()
    {
        var rawList = new[]
        {
            "https://music.youtube.com/watch?v=mock1",
            "https://music.youtube.com/watch?v=mock2"
        };

        var results = new System.Collections.Generic.List<DownloadEntry>();
        await foreach (var item in MetadataService.ResolveBatchEntriesChannelAsync(rawList, maxParallelism: 2))
        {
            results.Add(item);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("https://music.youtube.com/watch?v=mock1", results[0].Url);
        Assert.Equal("https://music.youtube.com/watch?v=mock2", results[1].Url);
    }
}
