namespace CazadorYTM.Gui.Services;

using System;
using System.Windows.Threading;
using CazadorYTM.Core;

public sealed class ClipboardLinkMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Func<string?> _readClipboardText;
    private readonly Action<string> _onLinkDetected;
    private readonly Func<bool> _canMonitor;
    private string _lastUrl = string.Empty;

    public ClipboardLinkMonitor(
        Func<string?> readClipboardText,
        Action<string> onLinkDetected,
        Func<bool> canMonitor,
        TimeSpan? interval = null)
    {
        _readClipboardText = readClipboardText;
        _onLinkDetected = onLinkDetected;
        _canMonitor = canMonitor;
        _timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromSeconds(1.5) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void Poll()
    {
        if (!_canMonitor()) return;

        try
        {
            var text = _readClipboardText()?.Trim();
            if (string.IsNullOrEmpty(text) || text == _lastUrl || !IsSupportedLink(text)) return;

            _lastUrl = text;
            _onLinkDetected(text);
        }
        catch { }
    }

    public static bool IsSupportedLink(string text) =>
        Helpers.IsUrl(text) &&
        (text.Contains("youtube.com/") || text.Contains("youtu.be/") || text.Contains("soundcloud.com/"));

    public void Dispose() => _timer.Stop();
}
