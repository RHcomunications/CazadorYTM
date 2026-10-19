namespace CazadorYTM.Gui.Tests;

using System;
using System.Collections.Generic;
using CazadorYTM.Gui.Services;

public class ClipboardLinkMonitorTests
{
    private static ClipboardLinkMonitor Create(
        Func<string?> readClipboard,
        Action<string> onLinkDetected,
        Func<bool>? canMonitor = null)
        => new(readClipboard, onLinkDetected, canMonitor ?? (() => true), TimeSpan.FromHours(1));

    [Fact]
    public void Poll_NewYoutubeLink_NotifiesOnce()
    {
        var pasted = new List<string>();
        var monitor = Create(() => "https://www.youtube.com/watch?v=abc", pasted.Add);

        monitor.Poll();
        monitor.Poll();

        Assert.Equal(new[] { "https://www.youtube.com/watch?v=abc" }, pasted);
    }

    [Fact]
    public void Poll_TrimsClipboardText()
    {
        var pasted = new List<string>();
        var monitor = Create(() => "  https://youtu.be/abc  ", pasted.Add);

        monitor.Poll();

        Assert.Equal(new[] { "https://youtu.be/abc" }, pasted);
    }

    [Fact]
    public void Poll_NotifiesAgainForDifferentLink()
    {
        var clipboard = "https://www.youtube.com/watch?v=abc";
        var pasted = new List<string>();
        var monitor = Create(() => clipboard, pasted.Add);

        monitor.Poll();
        clipboard = "https://soundcloud.com/artist/track";
        monitor.Poll();

        Assert.Equal(2, pasted.Count);
    }

    [Theory]
    [InlineData("https://example.com/video")]
    [InlineData("texto cualquiera")]
    [InlineData("")]
    [InlineData(null)]
    public void Poll_IgnoresUnsupportedContent(string? clipboard)
    {
        var pasted = new List<string>();
        var monitor = Create(() => clipboard, pasted.Add);

        monitor.Poll();

        Assert.Empty(pasted);
    }

    [Fact]
    public void Poll_RespectsCanMonitor()
    {
        var pasted = new List<string>();
        var monitor = Create(() => "https://youtu.be/abc", pasted.Add, () => false);

        monitor.Poll();

        Assert.Empty(pasted);
    }

    [Fact]
    public void Poll_SwallowsClipboardExceptions()
    {
        var monitor = Create(() => throw new InvalidOperationException("clipboard"), _ => { });

        monitor.Poll();
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc", true)]
    [InlineData("https://youtu.be/abc", true)]
    [InlineData("https://soundcloud.com/artist/track", true)]
    [InlineData("https://open.spotify.com/track/abc", false)]
    [InlineData("no es una url", false)]
    public void IsSupportedLink_DetectsSupportedHosts(string text, bool expected)
        => Assert.Equal(expected, ClipboardLinkMonitor.IsSupportedLink(text));
}
