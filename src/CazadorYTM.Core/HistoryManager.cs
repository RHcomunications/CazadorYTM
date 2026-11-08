namespace CazadorYTM.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CazadorYTM.Core.Models;

public class HistoryManager
{
    /// <summary>
    /// Estado compartido por todas las instancias que apuntan al mismo archivo.
    /// Evita que dos ViewModels mantengan cachés divergentes y se pisen al guardar.
    /// </summary>
    private sealed class SharedState
    {
        public readonly object Lock = new();
        public List<DownloadHistoryRecord>? CachedRecords;
        public bool HasPendingChanges;
        public Timer? DebounceTimer;
    }

    private static readonly object _fileLock = new();
    private static readonly object _registryLock = new();
    private static readonly List<WeakReference<HistoryManager>> _instances = new();
    private static readonly Dictionary<string, SharedState> _sharedStates =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _path;
    private readonly SharedState _state;
    private readonly int _debounceMilliseconds;

    public static event Action? GlobalHistoryChanged;

    public HistoryManager(string? baseDir = null, int debounceMilliseconds = 1500)
    {
        _debounceMilliseconds = debounceMilliseconds;
        _path = ResolvePath(baseDir);

        lock (_registryLock)
        {
            PruneDeadInstances();

            if (!_sharedStates.TryGetValue(_path, out var state))
            {
                state = new SharedState();
                _sharedStates[_path] = state;
            }

            _state = state;
            _instances.Add(new WeakReference<HistoryManager>(this));
        }
    }

    private static string ResolvePath(string? baseDir)
    {
        string path;
        if (baseDir is not null && (File.Exists(baseDir) || baseDir.EndsWith(".json")))
        {
            path = baseDir;
        }
        else
        {
            var appData = Helpers.GetAppDataDir();
            path = Path.Combine(appData, Constants.HistoryFilename);
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    /// <summary>Debe llamarse con <see cref="_registryLock"/> tomado.</summary>
    private static void PruneDeadInstances()
    {
        var livePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = _instances.Count - 1; i >= 0; i--)
        {
            if (_instances[i].TryGetTarget(out var manager))
            {
                livePaths.Add(manager._path);
            }
            else
            {
                _instances.RemoveAt(i);
            }
        }

        if (_sharedStates.Count == 0) return;

        var orphaned = _sharedStates.Keys
            .Where(path => !livePaths.Contains(path))
            .ToList();

        foreach (var path in orphaned)
        {
            _sharedStates.Remove(path);
        }
    }

    public static void FlushAll()
    {
        List<HistoryManager> alive = new();
        lock (_registryLock)
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

    /// <summary>Debe llamarse con <see cref="SharedState"/> (y por tanto su lock) tomado.</summary>
    private List<DownloadHistoryRecord> EnsureLoadedLocked()
    {
        if (_state.CachedRecords != null)
        {
            return _state.CachedRecords;
        }

        _state.CachedRecords = LoadFromDisk();
        return _state.CachedRecords;
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
            var tmpPath = $"{_path}.{Guid.NewGuid():N}.tmp";
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
        lock (_state.Lock)
        {
            _state.HasPendingChanges = true;
            if (_debounceMilliseconds <= 0)
            {
                FlushInternal();
                return;
            }

            if (_state.DebounceTimer == null)
            {
                _state.DebounceTimer = new Timer(_ => FlushInternal(), null, _debounceMilliseconds, Timeout.Infinite);
            }
            else
            {
                _state.DebounceTimer.Change(_debounceMilliseconds, Timeout.Infinite);
            }
        }
    }

    private void FlushInternal()
    {
        List<DownloadHistoryRecord>? toWrite = null;
        lock (_state.Lock)
        {
            if (!_state.HasPendingChanges || _state.CachedRecords == null) return;
            toWrite = new List<DownloadHistoryRecord>(_state.CachedRecords);
            _state.HasPendingChanges = false;
        }

        if (toWrite != null)
        {
            SaveToDiskInternal(toWrite);
        }
    }

    public void Flush()
    {
        lock (_state.Lock)
        {
            _state.DebounceTimer?.Dispose();
            _state.DebounceTimer = null;
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

    private static bool IsSpecificFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Directory.Exists(path)) return false;
        return !string.IsNullOrEmpty(Path.GetExtension(path));
    }

    private static bool MatchesRecord(DownloadHistoryRecord record, DownloadEntry entry)
    {
        if (IsSpecificFilePath(entry.FilePath) && IsSpecificFilePath(record.FilePath) &&
            string.Equals(record.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(entry.Title) && !string.IsNullOrEmpty(entry.Url))
        {
            return record.Title == entry.Title && record.Url == entry.Url;
        }

        if (!string.IsNullOrEmpty(entry.Title) && record.Title == entry.Title)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(entry.Url) && record.Url == entry.Url)
        {
            return true;
        }

        return false;
    }

    public void AddEntry(DownloadEntry entry, bool batchMode = false)
    {
        lock (_state.Lock)
        {
            var records = EnsureLoadedLocked();

            // Avoid exact duplicate entries (without matching shared folder paths)
            records.RemoveAll(d =>
                (IsSpecificFilePath(entry.FilePath) && IsSpecificFilePath(d.FilePath) &&
                 string.Equals(d.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(entry.Title) && d.Title == entry.Title &&
                 !string.IsNullOrEmpty(entry.Url) && d.Url == entry.Url));

            records.Add(DownloadHistoryRecord.FromDownloadEntry(entry));

            if (records.Count > 500)
            {
                _state.CachedRecords = records.GetRange(records.Count - 500, 500);
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
        lock (_state.Lock)
        {
            snapshot = new List<DownloadHistoryRecord>(EnsureLoadedLocked());
        }

        var list = snapshot.Select(r => r.ToDownloadEntry()).ToList();
        list.Reverse(); // Most recent first
        return list;
    }

    public void RemoveEntry(DownloadEntry entry)
    {
        lock (_state.Lock)
        {
            var records = EnsureLoadedLocked();

            records.RemoveAll(d => MatchesRecord(d, entry));

            ScheduleFlush();
        }

        GlobalHistoryChanged?.Invoke();
    }

    public int RemoveEntries(IEnumerable<DownloadEntry> toRemove)
    {
        int removed;
        lock (_state.Lock)
        {
            var records = EnsureLoadedLocked();
            var toRemoveList = toRemove.ToList();
            if (toRemoveList.Count == 0) return 0;

            var initialCount = records.Count;
            records.RemoveAll(d => toRemoveList.Any(entry => MatchesRecord(d, entry)));

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
        lock (_state.Lock)
        {
            _state.CachedRecords = new List<DownloadHistoryRecord>();
            ScheduleFlush();
        }

        GlobalHistoryChanged?.Invoke();
    }

    public List<DownloadHistoryRecord> GetAllRecords()
    {
        lock (_state.Lock)
        {
            return new List<DownloadHistoryRecord>(EnsureLoadedLocked());
        }
    }
}
