namespace CazadorYTM.Gui.Accessibility;

using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Threading;

public static class LiveRegion
{
    private static readonly ConditionalWeakTable<FrameworkElement, DispatcherTimer> DebounceTimers = new();
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

        var timer = DebounceTimers.GetValue(element, el =>
        {
            var t = new DispatcherTimer(DispatcherPriority.Background, el.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (el.IsLoaded)
                {
                    Raise(el);
                }
            };
            el.Unloaded += (_, _) => t.Stop();
            return t;
        });

        timer.Stop();
        timer.Start();
    }
}
