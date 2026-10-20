namespace CazadorYTM.Gui.Tests;

using CazadorYTM.Core.Models;
using CazadorYTM.Gui.Services;

public class RadarActionServiceTests
{
    [Theory]
    [InlineData("Y8ZApXcnhy4", "Y8ZApXcnhy4")]
    [InlineData("https://music.youtube.com/watch?v=Y8ZApXcnhy4", "Y8ZApXcnhy4")]
    [InlineData("https://www.youtube.com/watch?v=Y8ZApXcnhy4&list=RDAMVM", "Y8ZApXcnhy4")]
    [InlineData("https://youtu.be/Y8ZApXcnhy4", "Y8ZApXcnhy4")]
    [InlineData("https://www.youtube.com/shorts/Y8ZApXcnhy4", "Y8ZApXcnhy4")]
    public void GetVideoId_FromYouTubeEntry_ReturnsId(string value, string expected)
    {
        var entry = new DownloadEntry { Id = value, Url = value };

        Assert.Equal(expected, RadarActionService.GetVideoId(entry));
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("https://soundcloud.com/artist/track")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("")]
    public void GetVideoId_FromNonYouTubeEntry_ReturnsNull(string value)
    {
        var entry = new DownloadEntry { Id = value, Url = value };

        Assert.Null(RadarActionService.GetVideoId(entry));
    }
}
