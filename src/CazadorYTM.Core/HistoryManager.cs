namespace CazadorYTM.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using CazadorYTM.Core.Models;

public class HistoryManager
{
    private static readonly object _fileLock = new();
    private readonly string _path;

    public static event Action? GlobalHistoryChanged;

    public HistoryManager(string? baseDir = null)
    {
        if (baseDir is not null && (File.Exists(baseDir) || baseDir.EndsWith(".json")))
        {
            _path = baseDir;
        }
        else
        {
            var appData = Helpers.GetAppDataDir();
            _path = Path.Combine(appData, Constants.HistoryFilename);
        }
    }

    private List<Dictionary<string, object>> LoadFromDisk()
    {
        lock (_fileLock)
        {
            if (File.Exists(_path))
            {
                try
                {
                    var json = File.ReadAllText(_path);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var data = JsonHelper.Deserialize<List<Dictionary<string, object>>>(json);
                        if (data != null) return data;
                    }
                }
                catch { }
            }
            return new List<Dictionary<string, object>>();
        }
    }

    private void SaveToDisk(List<Dictionary<string, object>> entries)
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

                var json = JsonHelper.Serialize(entries);
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
        var entries = LoadFromDisk();

        // Avoid exact duplicate entries
        entries.RemoveAll(d =>
            (entry.FilePath != null && d.TryGetValue("file_path", out var fp) && fp?.ToString() == entry.FilePath) ||
            (entry.Title != null && d.TryGetValue("title", out var t) && t?.ToString() == entry.Title &&
             entry.Url != null && d.TryGetValue("url", out var u) && u?.ToString() == entry.Url));

        entries.Add(new Dictionary<string, object>
        {
            ["title"] = entry.Title ?? "",
            ["url"] = entry.Url ?? "",
            ["format"] = entry.Format ?? "mp3",
            ["status"] = entry.Status ?? "OK",
            ["file_path"] = entry.FilePath ?? "",
            ["date"] = (entry.Timestamp ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss")
        });

        if (entries.Count > 500)
        {
            entries = entries.GetRange(entries.Count - 500, 500);
        }

        SaveToDisk(entries);

        if (!batchMode)
        {
            GlobalHistoryChanged?.Invoke();
        }
    }

    public List<DownloadEntry> GetEntries()
    {
        var entries = LoadFromDisk();
        var list = new List<DownloadEntry>();

        foreach (var dict in entries)
        {
            var entry = new DownloadEntry
            {
                Title = dict.TryGetValue("title", out var t) ? t?.ToString() : null,
                Url = dict.TryGetValue("url", out var u) ? u?.ToString() : null,
                Format = dict.TryGetValue("format", out var f) ? f?.ToString() : null,
                Status = dict.TryGetValue("status", out var s) ? s?.ToString() : null,
                FilePath = dict.TryGetValue("file_path", out var fp) ? fp?.ToString() : null
            };

            if (dict.TryGetValue("date", out var d) && DateTime.TryParse(d?.ToString(), out var dt))
            {
                entry.Timestamp = dt;
            }

            list.Add(entry);
        }

        list.Reverse(); // Most recent first
        return list;
    }

    public void RemoveEntry(DownloadEntry entry)
    {
        var entries = LoadFromDisk();

        entries.RemoveAll(d =>
            (entry.Title != null && d.TryGetValue("title", out var t) && t?.ToString() == entry.Title) ||
            (entry.FilePath != null && d.TryGetValue("file_path", out var fp) && fp?.ToString() == entry.FilePath) ||
            (entry.Url != null && d.TryGetValue("url", out var u) && u?.ToString() == entry.Url));

        SaveToDisk(entries);
        GlobalHistoryChanged?.Invoke();
    }

    public int RemoveEntries(IEnumerable<DownloadEntry> toRemove)
    {
        var entries = LoadFromDisk();
        var toRemoveList = toRemove.ToList();
        if (toRemoveList.Count == 0) return 0;

        var initialCount = entries.Count;
        entries.RemoveAll(d =>
            toRemoveList.Any(entry =>
                (entry.Title != null && d.TryGetValue("title", out var t) && t?.ToString() == entry.Title) ||
                (entry.FilePath != null && d.TryGetValue("file_path", out var fp) && fp?.ToString() == entry.FilePath) ||
                (entry.Url != null && d.TryGetValue("url", out var u) && u?.ToString() == entry.Url)));

        var removed = initialCount - entries.Count;
        if (removed > 0)
        {
            SaveToDisk(entries);
            GlobalHistoryChanged?.Invoke();
        }
        return removed;
    }

    public void Clear()
    {
        SaveToDisk(new List<Dictionary<string, object>>());
        GlobalHistoryChanged?.Invoke();
    }

    public List<Dictionary<string, object>> GetAll() => LoadFromDisk();
}