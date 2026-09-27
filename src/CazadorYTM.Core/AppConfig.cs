namespace CazadorYTM.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

public class AppConfig
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly string _path;
    private readonly string _baseDir;
    private Dictionary<string, object> _data;

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
            _path = baseDir;
            _baseDir = Path.GetDirectoryName(_path) ?? string.Empty;
        }
        else
        {
            var appData = Helpers.GetAppDataDir();
            _path = Path.Combine(appData, Constants.ConfigFilename);
        }

        var defaultFolder = GetDefaultDownloadsFolder();
        _data = new Dictionary<string, object>(Defaults, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(_data["carpeta_destino"] as string))
        {
            _data["carpeta_destino"] = defaultFolder;
            _data["destination_folder"] = defaultFolder;
        }
        if (string.IsNullOrEmpty(_data["cookies_path"] as string))
            _data["cookies_path"] = Path.Combine(Helpers.GetAppDataDir(), "cookies.txt");

        Load();
    }

    public void Load()
    {
        _lock.EnterWriteLock();
        try
        {
            if (!File.Exists(_path)) return;
            try
            {
                var json = File.ReadAllText(_path);
                var saved = JsonHelper.Deserialize<Dictionary<string, JsonElement>>(json);
                if (saved != null)
                {
                    foreach (var (key, val) in saved)
                    {
                        if (val.ValueKind == JsonValueKind.String)
                            _data[key] = val.GetString() ?? "";
                        else if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var intVal))
                            _data[key] = intVal;
                        else if (val.ValueKind == JsonValueKind.Number && val.TryGetInt64(out var longVal))
                            _data[key] = (int)longVal;
                        else if (val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False)
                            _data[key] = val.GetBoolean();
                        else
                        {
                            var obj = val.Deserialize<object>();
                            if (obj != null) _data[key] = obj;
                        }

                        if (Synonyms.TryGetValue(key, out var syn) && !_data.ContainsKey(syn))
                        {
                            _data[syn] = _data[key];
                        }
                    }
                    Validate();
                }
            }
            catch (JsonException)
            {
                var defaultFolder = GetDefaultDownloadsFolder();
                if (File.Exists(_path))
                {
                    try { File.Copy(_path, _path + ".bak", true); }
                    catch { }
                }
                _data = new Dictionary<string, object>(Defaults, StringComparer.OrdinalIgnoreCase)
                {
                    ["carpeta_destino"] = defaultFolder,
                    ["destination_folder"] = defaultFolder,
                    ["cookies_path"] = Path.Combine(Helpers.GetAppDataDir(), "cookies.txt")
                };
            }
        }
        finally { _lock.ExitWriteLock(); }
    }

    private void Validate()
    {
        var dest = (_data.TryGetValue("destination_folder", out var dObj1) ? dObj1 as string : null) ??
                   (_data.TryGetValue("carpeta_destino", out var dObj2) ? dObj2 as string : null) ?? "";
        dest = dest.Trim();
        if (string.IsNullOrEmpty(dest) || !Directory.Exists(dest))
            dest = GetDefaultDownloadsFolder();

        _data["destination_folder"] = dest;
        _data["carpeta_destino"] = dest;

        var fmt = (_data.TryGetValue("format", out var fObj1) ? fObj1 as string : null) ??
                  (_data.TryGetValue("formato", out var fObj2) ? fObj2 as string : null) ?? "mp3";
        if (!Constants.FormatsRaw.Contains(fmt.ToLower()))
            fmt = "mp3";

        _data["format"] = fmt;
        _data["formato"] = fmt;

        var cb = (_data.TryGetValue("cookies_browser", out var bObj1) ? bObj1 as string : null) ??
                 (_data.TryGetValue("browser_cookies", out var bObj2) ? bObj2 as string : null) ?? "Ninguno";
        if (!Constants.Browsers.Contains(cb))
            cb = "Ninguno";

        _data["cookies_browser"] = cb;
        _data["browser_cookies"] = cb;

        if (_data.TryGetValue("max_concurrent", out var mcObj))
        {
            if (mcObj is int mc && (mc < 1 || mc > 5))
                _data["max_concurrent"] = 3;
        }
    }

    public void Save()
    {
        _lock.EnterWriteLock();
        try
        {
            var tmpPath = _path + ".tmp";
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonHelper.Serialize(_data);
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _path, overwrite: true);
            }
            catch (IOException)
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); }
                    catch { }
                }
            }
        }
        finally { _lock.ExitWriteLock(); }
    }

    public T Get<T>(string key, T defaultValue = default!)
    {
        _lock.EnterReadLock();
        try
        {
            if (_data.TryGetValue(key, out var val) && val is not null)
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

            if (Synonyms.TryGetValue(key, out var syn) && _data.TryGetValue(syn, out var synVal) && synVal is not null)
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
        finally { _lock.ExitReadLock(); }
    }

    public void Set<T>(string key, T value)
    {
        _lock.EnterWriteLock();
        try
        {
            _data[key] = value!;
            if (Synonyms.TryGetValue(key, out var syn))
            {
                _data[syn] = value!;
            }
        }
        finally { _lock.ExitWriteLock(); }
    }

    public Dictionary<string, object> GetAll()
    {
        _lock.EnterReadLock();
        try { return new Dictionary<string, object>(_data); }
        finally { _lock.ExitReadLock(); }
    }
}