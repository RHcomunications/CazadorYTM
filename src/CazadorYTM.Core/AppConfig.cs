namespace CazadorYTM.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

public class AppConfig
{
    private sealed class SharedConfigState
    {
        public readonly ReaderWriterLockSlim Lock = new();
        public readonly object SaveLock = new();
        public Dictionary<string, object> Data;
        public bool IsLoaded;

        public SharedConfigState(Dictionary<string, object> initialData)
        {
            Data = initialData;
        }
    }

    private static readonly object _registryLock = new();
    private static readonly List<WeakReference<AppConfig>> _instances = new();
    private static readonly Dictionary<string, SharedConfigState> _sharedStates =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _path;
    private readonly string _baseDir;
    private readonly SharedConfigState _state;

    private static string ResolveNormalizedPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    private static void PruneDeadInstancesLocked()
    {
        var livePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = _instances.Count - 1; i >= 0; i--)
        {
            if (_instances[i].TryGetTarget(out var cfg))
            {
                livePaths.Add(cfg._path);
            }
            else
            {
                _instances.RemoveAt(i);
            }
        }

        if (_sharedStates.Count == 0) return;

        var orphaned = _sharedStates.Keys.Where(p => !livePaths.Contains(p)).ToList();
        foreach (var path in orphaned)
        {
            if (_sharedStates.TryGetValue(path, out var state))
            {
                state.Lock.Dispose();
                _sharedStates.Remove(path);
            }
        }
    }

    private static Dictionary<string, object> CreateInitialData()
    {
        var defaultFolder = GetDefaultDownloadsFolder();
        var data = new Dictionary<string, object>(Defaults, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(data["carpeta_destino"] as string))
        {
            data["carpeta_destino"] = defaultFolder;
            data["destination_folder"] = defaultFolder;
        }
        if (string.IsNullOrEmpty(data["cookies_path"] as string))
        {
            data["cookies_path"] = Path.Combine(Helpers.GetAppDataDir(), "cookies.txt");
        }
        return data;
    }

    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["destination_folder"] = "carpeta_destino",
        ["carpeta_destino"] = "destination_folder",
        ["formato"] = "format",
        ["format"] = "formato",
        ["normalizar"] = "normalize",
        ["normalize"] = "normalizar",
        ["browser_cookies"] = "cookies_browser",
        ["cookies_browser"] = "browser_cookies",
        ["tema"] = "theme",
        ["theme"] = "tema",
        ["idioma"] = "language",
        ["language"] = "idioma"
    };

    public static string GetDefaultDownloadsFolder()
    {
        try
        {
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (!string.IsNullOrEmpty(music) && Directory.Exists(music))
            {
                var defaultDir = Path.Combine(music, Constants.AppName);
                if (!Directory.Exists(defaultDir))
                {
                    try { Directory.CreateDirectory(defaultDir); } catch { }
                }
                return defaultDir;
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloads = Path.Combine(userProfile, "Downloads", Constants.AppName);
            if (!Directory.Exists(downloads))
            {
                try { Directory.CreateDirectory(downloads); } catch { }
            }
            return downloads;
        }
        catch { }

        return Helpers.GetBaseDir();
    }

    private static readonly Dictionary<string, object> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["formato"] = "mp3",
        ["format"] = "mp3",
        ["bitrate"] = "320K",
        ["bitrate_index"] = 3,
        ["carpeta_destino"] = "",
        ["destination_folder"] = "",
        ["verificar"] = true,
        ["sponsorblock"] = false,
        ["browser_cookies"] = "Ninguno",
        ["cookies_browser"] = "Ninguno",
        ["enumerar"] = false,
        ["max_concurrent"] = 3,
        ["normalizar"] = false,
        ["normalize"] = false,
        ["embed_lyrics"] = false,
        ["tema"] = "Automático",
        ["theme"] = "Automático",
        ["language"] = "es",
        ["idioma"] = "es",
        ["auto_update"] = true,
        ["deep_verify"] = false,
        ["trim_silence"] = false,
        ["lyrics_api_fallback"] = true,
        ["font_size"] = "Normal",
        ["cookies_path"] = Helpers.GetDefaultCookiesPath(),
        ["proxy"] = ""
    };

    public AppConfig(string? baseDir = null)
    {
        _baseDir = baseDir ?? Helpers.GetBaseDir();
        if (baseDir != null && (File.Exists(baseDir) || baseDir.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            _path = ResolveNormalizedPath(baseDir);
            _baseDir = Path.GetDirectoryName(_path) ?? string.Empty;
        }
        else
        {
            var appData = Helpers.GetAppDataDir();
            _path = ResolveNormalizedPath(Path.Combine(appData, Constants.ConfigFilename));
        }

        lock (_registryLock)
        {
            PruneDeadInstancesLocked();
            if (!_sharedStates.TryGetValue(_path, out var existing))
            {
                existing = new SharedConfigState(CreateInitialData());
                _sharedStates[_path] = existing;
            }
            _state = existing;
            _instances.Add(new WeakReference<AppConfig>(this));
        }

        EnsureInitialLoad();
    }

    private void EnsureInitialLoad()
    {
        lock (_state.SaveLock)
        {
            _state.Lock.EnterWriteLock();
            try
            {
                if (_state.IsLoaded) return;
                LoadLocked();
            }
            finally { _state.Lock.ExitWriteLock(); }
        }
    }

    public void Load()
    {
        lock (_state.SaveLock)
        {
            _state.Lock.EnterWriteLock();
            try
            {
                LoadLocked();
            }
            finally { _state.Lock.ExitWriteLock(); }
        }
    }

    private void LoadLocked()
    {
        _state.IsLoaded = true;
        if (!File.Exists(_path)) return;
        try
        {
            if (new FileInfo(_path).Length > 10 * 1024 * 1024) return;
            var json = File.ReadAllText(_path);
            var saved = JsonHelper.Deserialize<Dictionary<string, JsonElement>>(json);
            if (saved != null)
            {
                foreach (var (key, val) in saved)
                {
                    if (val.ValueKind == JsonValueKind.String)
                        _state.Data[key] = val.GetString() ?? "";
                    else if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var intVal))
                        _state.Data[key] = intVal;
                    else if (val.ValueKind == JsonValueKind.Number && val.TryGetInt64(out var longVal))
                        _state.Data[key] = (int)longVal;
                    else if (val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False)
                        _state.Data[key] = val.GetBoolean();
                    else
                    {
                        var obj = val.Deserialize<object>();
                        if (obj != null) _state.Data[key] = obj;
                    }

                    if (Synonyms.TryGetValue(key, out var syn) && !_state.Data.ContainsKey(syn))
                    {
                        _state.Data[syn] = _state.Data[key];
                    }
                }
                Validate();
            }
        }
        catch (JsonException)
        {
            if (File.Exists(_path))
            {
                try { File.Copy(_path, _path + ".bak", true); }
                catch { }
            }
            _state.Data = CreateInitialData();
        }
    }

    private void Validate()
    {
        var dest = (_state.Data.TryGetValue("destination_folder", out var dObj1) ? dObj1 as string : null) ??
                   (_state.Data.TryGetValue("carpeta_destino", out var dObj2) ? dObj2 as string : null) ?? "";
        dest = dest.Trim();
        if (string.IsNullOrEmpty(dest) || !Directory.Exists(dest))
            dest = GetDefaultDownloadsFolder();

        _state.Data["destination_folder"] = dest;
        _state.Data["carpeta_destino"] = dest;

        var fmt = (_state.Data.TryGetValue("format", out var fObj1) ? fObj1 as string : null) ??
                  (_state.Data.TryGetValue("formato", out var fObj2) ? fObj2 as string : null) ?? "mp3";
        if (!Constants.FormatsRaw.Contains(fmt.ToLower()))
            fmt = "mp3";

        _state.Data["format"] = fmt;
        _state.Data["formato"] = fmt;

        var cb = (_state.Data.TryGetValue("cookies_browser", out var bObj1) ? bObj1 as string : null) ??
                 (_state.Data.TryGetValue("browser_cookies", out var bObj2) ? bObj2 as string : null) ?? "Ninguno";
        if (!Constants.Browsers.Contains(cb))
            cb = "Ninguno";

        _state.Data["cookies_browser"] = cb;
        _state.Data["browser_cookies"] = cb;

        if (_state.Data.TryGetValue("max_concurrent", out var mcObj))
        {
            if (mcObj is int mc && (mc < 1 || mc > 5))
                _state.Data["max_concurrent"] = 3;
        }
    }

    public void Save()
    {
        lock (_state.SaveLock)
        {
            string json;
            _state.Lock.EnterReadLock();
            try
            {
                json = JsonHelper.Serialize(_state.Data);
            }
            finally { _state.Lock.ExitReadLock(); }

            var tmpPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppConfig.Save] Error saving config: {ex.Message}");
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); }
                    catch { }
                }
            }
        }
    }

    public T Get<T>(string key, T defaultValue = default!)
    {
        _state.Lock.EnterReadLock();
        try
        {
            if (_state.Data.TryGetValue(key, out var val) && val is not null)
            {
                if (val is T tVal) return tVal;
                try
                {
                    return (T)Convert.ChangeType(val, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }

            if (Synonyms.TryGetValue(key, out var syn) && _state.Data.TryGetValue(syn, out var synVal) && synVal is not null)
            {
                if (synVal is T tVal) return tVal;
                try
                {
                    return (T)Convert.ChangeType(synVal, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }

            return defaultValue;
        }
        finally { _state.Lock.ExitReadLock(); }
    }

    public void Set<T>(string key, T value)
    {
        _state.Lock.EnterWriteLock();
        try
        {
            _state.Data[key] = value!;
            if (Synonyms.TryGetValue(key, out var syn))
            {
                _state.Data[syn] = value!;
            }
        }
        finally { _state.Lock.ExitWriteLock(); }
    }

    public Dictionary<string, object> GetAll()
    {
        _state.Lock.EnterReadLock();
        try { return new Dictionary<string, object>(_state.Data); }
        finally { _state.Lock.ExitReadLock(); }
    }
}