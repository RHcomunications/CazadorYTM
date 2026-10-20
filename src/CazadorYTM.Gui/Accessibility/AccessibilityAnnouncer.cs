namespace CazadorYTM.Gui.Accessibility;

using System;
using System.Windows;
using System.Windows.Threading;

public static class AccessibilityAnnouncer
{
    public static event Action<string>? AnnouncementRequested;

    public static void Announce(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        if (app.Dispatcher.CheckAccess())
        {
            AnnouncementRequested?.Invoke(message);
        }
        else
        {
            app.Dispatcher.InvokeAsync(
                () => AnnouncementRequested?.Invoke(message),
                DispatcherPriority.Background);
        }
    }
}
