namespace CazadorYTM.Gui.Services;

using System;
using System.IO;
using System.Text;
using System.Windows;
using CazadorYTM.Core;

public class LogService
{
    private static readonly Lazy<LogService> _instance = new(() => new LogService());
    public static LogService Instance => _instance.Value;

    private const long MaxLogBytes = 5 * 1024 * 1024;
    private const int MaxMessageLength = 8192;
    private readonly object _lock = new();
    private readonly string _logFilePath;

    public event Action<string, string>? OnLogReceived;

    public LogService()
    {
        _logFilePath = Path.Combine(Helpers.GetBaseDir(), "casador.log");
    }

    public void Log(string message, string level = "INFO")
    {
        message = Helpers.SanitizeLogMessage(message ?? string.Empty);
        if (message.Length > MaxMessageLength)
        {
            message = message[..MaxMessageLength] + "…";
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var line = $"[{timestamp}] [{level.ToUpper()}] {message}";

        lock (_lock)
        {
            try
            {
                if (File.Exists(_logFilePath) && new FileInfo(_logFilePath).Length >= MaxLogBytes)
                {
                    RotateLog();
                }

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

    private void RotateLog()
    {
        var backupPath = _logFilePath + ".1";
        try
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(_logFilePath, backupPath);
        }
        catch
        {
            try
            {
                File.WriteAllText(_logFilePath, string.Empty, Encoding.UTF8);
            }
            catch { }
        }
    }

    public void Info(string message) => Log(message, "INFO");
    public void Warn(string message) => Log(message, "WARN");
    public void Error(string message) => Log(message, "ERROR");

    public string GetLogFilePath() => _logFilePath;

    public string GetAllLogsText()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    return File.ReadAllText(_logFilePath, Encoding.UTF8);
                }
            }
            catch { }
            return string.Empty;
        }
    }

    public bool ExportLogs(string destinationPath)
    {
        try
        {
            var content = GetAllLogsText();
            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(destinationPath, content, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
