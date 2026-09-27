namespace CazadorYTM.Tests;

using Xunit;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;
using System.Collections.Generic;

public class HelpersTests
{
    [Theory]
    [InlineData("valid_filename.mp3", "valid_filename.mp3")]
    [InlineData("test/file:name*?.mp3", "test_file_name__.mp3")]
    [InlineData("", "pista_audio")]
    [InlineData("   ", "pista_audio")]
    [InlineData("track \u202Ereversed.mp3", "track reversed.mp3")]
    public void SanitizeFilename_CleansInvalidCharacters(string input, string expected)
    {
        var result = Helpers.SanitizeFilename(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(65, "01:05")]
    [InlineData(0, "00:00")]
    [InlineData(3665, "1:01:05")]
    [InlineData(-10, "00:00")]
    public void FormatDuration_FormatsCorrectly(int seconds, string expected)
    {
        var result = Helpers.FormatDuration(seconds);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", true)]
    [InlineData("http://music.youtube.com/watch?v=123", true)]
    [InlineData("just a search query", false)]
    [InlineData("Artist - Song Title", false)]
    public void IsUrl_IdentifiesUrlsCorrectly(string input, bool expected)
    {
        var result = Helpers.IsUrl(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SummarizeResults_AggregatesCountsCorrectly()
    {
        var list = new List<ResultadoArchivo>
        {
            new("file1.mp3"), // OK
            new("file2.mp3") { Advertencias = { "Aviso bitrate" } }, // Warning
            new("file3.mp3") { Errores = { "Corrupto" } }, // Error
            new("file4.mp3") // OK
        };

        var (ok, warn, err) = Helpers.SummarizeResults(list);

        Assert.Equal(2, ok);
        Assert.Equal(1, warn);
        Assert.Equal(1, err);
    }
}
