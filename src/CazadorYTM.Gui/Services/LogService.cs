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
    private StreamWriter? _writer;
    private long _currentLogBytes;
    private readonly System.Threading.Timer _flushTimer;

    public event Action<string, string>? OnLogReceived;

    public LogService()
    {
        _logFilePath = Path.Combine(Helpers.GetBaseDir(), "casador.log");
        try
        {
            if (File.Exists(_logFilePath))
            {
                _currentLogBytes = new FileInfo(_logFilePath).Length;
            }
        }
        catch { }

        _flushTimer = new System.Threading.Timer(_ => Flush(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }

    private void EnsureWriterLocked()
    {
        if (_writer != null) return;
        var fs = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(fs, Encoding.UTF8) { AutoFlush = false };
    }

    public void Flush()
    {
        lock (_lock)
        {
            try
            {
                _writer?.Flush();
            }
            catch { }
        }
    }

    public void Log(string message, string level = "INFO")
    {
        message = Helpers.SanitizeLogMessage(message ?? string.Empty);
        if (message.Length > MaxMessageLength)
        {
            message = message[..MaxMessageLength] + "…";
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var line = $"[{timestamp}] [{level.ToUpperInvariant()}] {message}";

        lock (_lock)
        {
            try
            {
                var lineBytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                if (_currentLogBytes + lineBytes >= MaxLogBytes)
                {
                    RotateLogLocked();
                }

                EnsureWriterLocked();
                _writer!.WriteLine(line);
                _currentLogBytes += lineBytes;
                _flushTimer.Change(200, System.Threading.Timeout.Infinite);
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

    private void RotateLogLocked()
    {
        if (_writer != null)
        {
            try { _writer.Flush(); } catch { }
            finally
            {
                try { _writer.Dispose(); } catch { }
                _writer = null;
            }
        }

        var backupPath = _logFilePath + ".1";
        try
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            if (File.Exists(_logFilePath))
            {
                File.Move(_logFilePath, backupPath);
            }
        }
        catch
        {
            try
            {
                File.WriteAllText(_logFilePath, string.Empty, Encoding.UTF8);
            }
            catch { }
        }

        _currentLogBytes = 0;
    }

    public void Info(string message) => Log(message, "INFO");
    public void Warn(string message) => Log(message, "WARN");
    public void Error(string message) => Log(message, "ERROR");

    public string GetLogFilePath() => _logFilePath;

    public string GetAllLogsText()
    {
        lock (_lock)
        {
            try { _writer?.Flush(); } catch { }
            try
            {
                if (File.Exists(_logFilePath))
                {
                    using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs, Encoding.UTF8);
                    return sr.ReadToEnd();
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
