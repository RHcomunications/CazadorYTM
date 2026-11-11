namespace CazadorYTM.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using CazadorYTM.Core.Models;
using CazadorYTM.Core.Services;
using Xunit;

public class LyricLineTests
{
    [Fact]
    public void ParseLrc_WithNullOrEmpty_ReturnsEmptyList()
    {
        Assert.Empty(LyricLine.ParseLrc(null));
        Assert.Empty(LyricLine.ParseLrc(""));
        Assert.Empty(LyricLine.ParseLrc("   \r\n   "));
    }

    [Fact]
    public void ParseLrc_WithStandardTimestamps_ParsesAndSortsCorrectly()
    {
        var rawLrc = @"[ti:Test Title]
[ar:Test Artist]
[01:05.50] Second line of lyric
[00:15.200] First line of lyric
[02:10.00] Third line of lyric";

        var lines = LyricLine.ParseLrc(rawLrc);

        Assert.Equal(3, lines.Count);
        Assert.True(lines[0].IsSynchronized);
        Assert.Equal("First line of lyric", lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(15.2), lines[0].Timestamp);

        Assert.Equal("Second line of lyric", lines[1].Text);
        Assert.Equal(new TimeSpan(0, 0, 1, 5, 500), lines[1].Timestamp);

        Assert.Equal("Third line of lyric", lines[2].Text);
        Assert.Equal(new TimeSpan(0, 0, 2, 10, 0), lines[2].Timestamp);
    }

    [Fact]
    public void ParseLrc_WithMultipleTimestampsPerLine_ExpandsAndSorts()
    {
        var rawLrc = @"[00:10.00][00:40.00] Chorus line
[00:20.00] Verse line";

        var lines = LyricLine.ParseLrc(rawLrc);

        Assert.Equal(3, lines.Count);
        Assert.Equal("Chorus line", lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(10), lines[0].Timestamp);

        Assert.Equal("Verse line", lines[1].Text);
        Assert.Equal(TimeSpan.FromSeconds(20), lines[1].Timestamp);

        Assert.Equal("Chorus line", lines[2].Text);
        Assert.Equal(TimeSpan.FromSeconds(40), lines[2].Timestamp);
    }

    [Fact]
    public void ParseLrc_WithPlainText_ReturnsUnsynchronizedLines()
    {
        var plain = @"First verse line
Second verse line
Third verse line";

        var lines = LyricLine.ParseLrc(plain);

        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.False(l.IsSynchronized));
        Assert.Equal("First verse line", lines[0].Text);
        Assert.Equal("Second verse line", lines[1].Text);
        Assert.Equal("Third verse line", lines[2].Text);
    }

    [Fact]
    public async Task LoadLyricsAsync_FromLocalLrcFile_ReturnsParsedLyrics()
    {
        var tempAudio = Path.Combine(Path.GetTempPath(), $"test_song_{Guid.NewGuid():N}.mp3");
        var tempLrc = Path.ChangeExtension(tempAudio, ".lrc");

        try
        {
            await File.WriteAllTextAsync(tempLrc, "[00:05.00] Local lyric line\n[00:10.50] Another line");

            var lyrics = await MetadataService.LoadLyricsAsync(tempAudio, "Test Song");

            Assert.Equal(2, lyrics.Count);
            Assert.Equal("Local lyric line", lyrics[0].Text);
            Assert.Equal(TimeSpan.FromSeconds(5), lyrics[0].Timestamp);
            Assert.Equal("Another line", lyrics[1].Text);
        }
        finally
        {
            if (File.Exists(tempLrc)) File.Delete(tempLrc);
        }
    }
}
