namespace CazadorYTM.Gui.Tests;

using CazadorYTM.Gui.Accessibility;
using Xunit;

[Collection("WPF")]
public class AccessibilityAnnouncerTests
{
    [Fact]
    public void Announce_ForwardsMessageToSubscribers()
    {
        string? received = null;

        WpfTestHost.Run(() =>
        {
            void Handler(string message) => received = message;

            AccessibilityAnnouncer.AnnouncementRequested += Handler;
            try
            {
                AccessibilityAnnouncer.Announce("Mensaje de prueba");
            }
            finally
            {
                AccessibilityAnnouncer.AnnouncementRequested -= Handler;
            }
        });

        Assert.Equal("Mensaje de prueba", received);
    }

    [Fact]
    public void Announce_IgnoresBlankMessages()
    {
        var announcements = 0;

        WpfTestHost.Run(() =>
        {
            void Handler(string message) => announcements++;

            AccessibilityAnnouncer.AnnouncementRequested += Handler;
            try
            {
                AccessibilityAnnouncer.Announce(" ");
                AccessibilityAnnouncer.Announce(string.Empty);
            }
            finally
            {
                AccessibilityAnnouncer.AnnouncementRequested -= Handler;
            }
        });

        Assert.Equal(0, announcements);
    }
}
