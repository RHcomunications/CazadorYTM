namespace CazadorYTM.Tests;

using Xunit;
using CazadorYTM.Core;

public class ParsersTests
{
    [Fact]
    public void ParseSpotifyCsv_CommaDelimited_ExtractsTrackAndArtist()
    {
        var csv = "\"Track Name\",\"Artist Name(s)\",\"Album Name\"\n" +
                  "\"Bohemian Rhapsody\",\"Queen\",\"A Night at the Opera\"\n" +
                  "\"Hotel California\",\"Eagles\",\"Hotel California\"";

        var results = Parsers.ParseSpotifyCsv(csv);

        Assert.Equal(2, results.Count);
        Assert.Equal("Bohemian Rhapsody Queen", results[0]);
        Assert.Equal("Hotel California Eagles", results[1]);
    }

    [Fact]
    public void ParseSpotifyCsv_SemicolonDelimited_ExtractsCorrectly()
    {
        var csv = "Track Name;Artist Name;Album\n" +
                  "Billie Jean;Michael Jackson;Thriller\n" +
                  "Beat It;Michael Jackson;Thriller";

        var results = Parsers.ParseSpotifyCsv(csv);

        Assert.Equal(2, results.Count);
        Assert.Equal("Billie Jean Michael Jackson", results[0]);
        Assert.Equal("Beat It Michael Jackson", results[1]);
    }

    [Fact]
    public void ParsePlainTextSongs_FiltersEmptyAndComments()
    {
        var txt = "# Comentario de lista\n" +
                  "Queen - Bohemian Rhapsody\n" +
                  "\n" +
                  "   \n" +
                  "Eagles - Hotel California\n" +
                  "# Otro comentario";

        var results = Parsers.ParsePlainTextSongs(txt);

        Assert.Equal(2, results.Count);
        Assert.Equal("Queen - Bohemian Rhapsody", results[0]);
        Assert.Equal("Eagles - Hotel California", results[1]);
    }

    [Fact]
    public void ParseSongList_AutoDetectsCsvAndPlainText()
    {
        var csv = "Title,Artist\nStairway to Heaven,Led Zeppelin";
        var txt = "Pink Floyd - Comfortably Numb";

        var csvResults = Parsers.ParseSongList(csv);
        var txtResults = Parsers.ParseSongList(txt);

        Assert.Single(csvResults);
        Assert.Equal("Stairway to Heaven Led Zeppelin", csvResults[0]);

        Assert.Single(txtResults);
        Assert.Equal("Pink Floyd - Comfortably Numb", txtResults[0]);
    }
}
