namespace CazadorYTM.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CazadorYTM.Core.Models;

public class HistoryManager
{
    private static readonly object _fileLock = new();
    private static readonly object _instancesLock = new();
    private static readonly List<WeakReference<HistoryManager>> _instances = new();
    private readonly string _path;

    private readonly object _stateLock = new();
    private List<DownloadHistoryRecord>? _cachedRecords;
    private bool _hasPendingChanges;
    private Timer? _debounceTimer;
    private readonly int _debounceMilliseconds;

    public static event Action? GlobalHistoryChanged;

    public HistoryManager(string? baseDir = null, int debounceMilliseconds = 1500)
    {
        _debounceMilliseconds = debounceMilliseconds;
        if (baseDir is not null && (File.Exists(baseDir) || baseDir.EndsWith(".json")))
        {
            _path = baseDir;
        }
        else
        {
            var appData = Helpers.GetAppDataDir();
            _path = Path.Combine(appData, Constants.HistoryFilename);
        }

        lock (_instancesLock)
        {
            _instances.Add(new WeakReference<HistoryManager>(this));
        }
    }

    public static void FlushAll()
    {
        List<HistoryManager> alive = new();
        lock (_instancesLock)
        {
            for (var i = _instances.Count - 1; i >= 0; i--)
            {
                if (_instances[i].TryGetTarget(out var manager))
                {
                    alive.Add(manager);
                }
                else
                {
                    _instances.RemoveAt(i);
                }
            }
        }

        foreach (var manager in alive)
        {
            manager.Flush();
        }
    }

    private List<DownloadHistoryRecord> EnsureLoaded()
    {
        lock (_stateLock)
        {
            if (_cachedRecords != null)
            {
                return _cachedRecords;
            }

            _cachedRecords = LoadFromDisk();
            return _cachedRecords;
        }
    }

    private List<DownloadHistoryRecord> LoadFromDisk()
    {
        lock (_fileLock)
        {
            if (File.Exists(_path))
            {
                try
                {
                    if (new FileInfo(_path).Length > 20 * 1024 * 1024) return new List<DownloadHistoryRecord>();
                    var json = File.ReadAllText(_path);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var data = JsonHelper.Deserialize<List<DownloadHistoryRecord>>(json);
                        if (data != null)
                        {
                            return data.Count > 500
                                ? data.GetRange(data.Count - 500, 500)
                                : data;
                        }
                    }
                }
                catch { }
            }
            return new List<DownloadHistoryRecord>();
        }
    }

    private void SaveToDiskInternal(List<DownloadHistoryRecord> records)
    {
        lock (_fileLock)
        {
            var tmpPath = _path + ".tmp";
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonHelper.Serialize(records);
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _path, overwrite: true);
            }
            catch
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { }
                }
            }
        }
    }

    private void ScheduleFlush()
    {
        lock (_stateLock)
        {
            _hasPendingChanges = true;
            if (_debounceMilliseconds <= 0)
            {
                FlushInternal();
                return;
            }

            if (_debounceTimer == null)
            {
                _debounceTimer = new Timer(_ => FlushInternal(), null, _debounceMilliseconds, Timeout.Infinite);
            }
            else
            {
                _debounceTimer.Change(_debounceMilliseconds, Timeout.Infinite);
            }
        }
    }

    private void FlushInternal()
    {
        List<DownloadHistoryRecord>? toWrite = null;
        lock (_stateLock)
        {
            if (!_hasPendingChanges || _cachedRecords == null) return;
            toWrite = new List<DownloadHistoryRecord>(_cachedRecords);
            _hasPendingChanges = false;
        }

        if (toWrite != null)
        {
            SaveToDiskInternal(toWrite);
        }
    }

    public void Flush()
    {
        lock (_stateLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
        FlushInternal();
    }

    public void Add(string title, string url, string fmt, string status, bool batchMode = false)
    {
        AddEntry(new DownloadEntry
        {
            Title = title,
            Url = url,
            Format = fmt,
            Status = status,
            Timestamp = DateTime.Now
        }, batchMode);
    }

    public void AddEntry(DownloadEntry entry, bool batchMode = false)
    {
        lock (_stateLock)
        {
            var records = EnsureLoaded();

            // Avoid exact duplicate entries
            records.RemoveAll(d =>
                (!string.IsNullOrEmpty(entry.FilePath) && d.FilePath == entry.FilePath) ||
                (!string.IsNullOrEmpty(entry.Title) && d.Title == entry.Title &&
                 !string.IsNullOrEmpty(entry.Url) && d.Url == entry.Url));

            records.Add(DownloadHistoryRecord.FromDownloadEntry(entry));

            if (records.Count > 500)
            {
                _cachedRecords = records.GetRange(records.Count - 500, 500);
            }

            ScheduleFlush();
        }

        if (!batchMode)
        {
            GlobalHistoryChanged?.Invoke();
        }
    }

    public List<DownloadEntry> GetEntries()
    {
        List<DownloadHistoryRecord> snapshot;
        lock (_stateLock)
        {
            snapshot = new List<DownloadHistoryRecord>(EnsureLoaded());
        }

        var list = snapshot.Select(r => r.ToDownloadEntry()).ToList();
        list.Reverse(); // Most recent first
        return list;
    }

    public void RemoveEntry(DownloadEntry entry)
    {
        lock (_stateLock)
        {
            var records = EnsureLoaded();

            records.RemoveAll(d =>
                (!string.IsNullOrEmpty(entry.Title) && d.Title == entry.Title) ||
                (!string.IsNullOrEmpty(entry.FilePath) && d.FilePath == entry.FilePath) ||
                (!string.IsNullOrEmpty(entry.Url) && d.Url == entry.Url));

            ScheduleFlush();
        }

        GlobalHistoryChanged?.Invoke();
    }

    public int RemoveEntries(IEnumerable<DownloadEntry> toRemove)
    {
        int removed;
        lock (_stateLock)
        {
            var records = EnsureLoaded();
            var toRemoveList = toRemove.ToList();
            if (toRemoveList.Count == 0) return 0;

            var initialCount = records.Count;
            records.RemoveAll(d =>
                toRemoveList.Any(entry =>
                    (!string.IsNullOrEmpty(entry.Title) && d.Title == entry.Title) ||
                    (!string.IsNullOrEmpty(entry.FilePath) && d.FilePath == entry.FilePath) ||
                    (!string.IsNullOrEmpty(entry.Url) && d.Url == entry.Url)));

            removed = initialCount - records.Count;
            if (removed > 0)
            {
                ScheduleFlush();
            }
        }

        if (removed > 0)
        {
            GlobalHistoryChanged?.Invoke();
        }
        return removed;
    }

    public void Clear()
    {
        lock (_stateLock)
        {
            _cachedRecords = new List<DownloadHistoryRecord>();
            ScheduleFlush();
        }

        GlobalHistoryChanged?.Invoke();
    }

    public List<DownloadHistoryRecord> GetAllRecords()
    {
        lock (_stateLock)
        {
            return new List<DownloadHistoryRecord>(EnsureLoaded());
        }
    }
}