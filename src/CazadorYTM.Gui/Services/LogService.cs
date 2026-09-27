namespace CazadorYTM.Gui.Services;

using System;
using System.IO;
using System.Text;
using System.Windows;
using CazadorYTM.Core;

public interface ILogService
{
    event Action<string, string>? OnLogReceived;
    void Log(string message, string level = "INFO");
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public class LogService : ILogService
{
    private static readonly Lazy<LogService> _instance = new(() => new LogService());
    public static LogService Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly string _logFilePath;

    public event Action<string, string>? OnLogReceived;

    public LogService()
    {
        _logFilePath = Path.Combine(Helpers.GetBaseDir(), "casador.log");
    }

    public void Log(string message, string level = "INFO")
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var line = $"[{timestamp}] [{level.ToUpper()}] {message}";

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        if (Application.Current?.Dispatcher != null)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                OnLogReceived?.Invoke(message, level);
            });
        }
        else
        {
            OnLogReceived?.Invoke(message, level);
        }
    }

    public void Info(string message) => Log(message, "INFO");
    public void Warn(string message) => Log(message, "WARN");
    public void Error(string message) => Log(message, "ERROR");
}
