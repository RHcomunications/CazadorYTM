namespace CazadorYTM.Gui.Accessibility;

using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Threading;

public static class LiveRegion
{
    public static readonly DependencyProperty AnnounceProperty =
        DependencyProperty.RegisterAttached(
            "Announce",
            typeof(string),
            typeof(LiveRegion),
            new PropertyMetadata(null, OnAnnounceChanged));

    public static string? GetAnnounce(DependencyObject element) =>
        (string?)element.GetValue(AnnounceProperty);

    public static void SetAnnounce(DependencyObject element, string? value) =>
        element.SetValue(AnnounceProperty, value);

    public static void Raise(FrameworkElement element)
    {
        var peer = UIElementAutomationPeer.FromElement(element)
                   ?? UIElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static void OnAnnounceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element ||
            e.NewValue is not string text ||
            string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        element.Dispatcher.InvokeAsync(() =>
        {
            if (element.IsLoaded)
            {
                Raise(element);
            }
        }, DispatcherPriority.Background);
    }
}
