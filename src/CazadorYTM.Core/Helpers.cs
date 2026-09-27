namespace CazadorYTM.Core;

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

public static class Helpers
{
    public static string GetBaseDir()
    {
        var entry = Assembly.GetEntryAssembly();
        if (entry?.GetName().Name == "CazadorYTM.Gui")
            return AppContext.BaseDirectory;
        return AppDomain.CurrentDomain.BaseDirectory;
    }

    public static string SanitizeFilename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "pista_audio";

        name = Regex.Replace(name, @"[\ud800-\udfff\u202e\u202a-\u202d\u200e\u200f\u2066-\u2069]", "");
        name = Regex.Replace(name, @"[\\/*?:""<>|]", "_");
        name = Regex.Replace(name, @"[\x00-\x1f\x7f]", "");

        string ext = Path.GetExtension(name);
        string stem = Path.GetFileNameWithoutExtension(name);

        string[] reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        if (reserved.Contains(stem.ToUpperInvariant().Trim()))
            stem += "_";

        stem = stem.Trim().TrimEnd('.');
        if (string.IsNullOrEmpty(stem))
            stem = "pista_audio";

        string result = stem + ext;
        if (result.Length > 120)
        {
            int extLen = ext.Length;
            result = stem.Substring(0, 120 - extLen) + ext;
        }
        return result.TrimEnd('.');
    }

    public static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Lista_Descargas";

        name = Regex.Replace(name, @"[\ud800-\udfff\u202e\u202a-\u202d\u200e\u200f\u2066-\u2069]", "");
        name = Regex.Replace(name, @"[\\/*?:""<>|]", "_");
        name = Regex.Replace(name, @"[\x00-\x1f\x7f]", "");

        string[] reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        if (reserved.Contains(name.ToUpperInvariant().Trim()))
            name += "_";

        name = name.Trim().TrimEnd('.');
        if (string.IsNullOrEmpty(name))
            name = "Lista_Descargas";

        if (name.Length > 150)
            name = name.Substring(0, 150).TrimEnd('.');

        return name;
    }

    public static bool IsUrl(string text)
    {
        try
        {
            var parsed = new Uri(text);
            return (parsed.Scheme == "http" || parsed.Scheme == "https") && !string.IsNullOrEmpty(parsed.Host);
        }
        catch
        {
            return false;
        }
    }

    public static string FormatDuration(double? seconds)
    {
        if (seconds is null) return string.Empty;
        try
        {
            int total = (int)Math.Floor(seconds.Value);
            if (total <= 0) return "00:00";
            int hours = total / 3600;
            int mins = (total % 3600) / 60;
            int secs = total % 60;
            return hours > 0 ? $"{hours}:{mins:D2}:{secs:D2}" : $"{mins:D2}:{secs:D2}";
        }
        catch
        {
            return string.Empty;
        }
    }

    public static (int ok, int warn, int err) SummarizeResults(List<Models.ResultadoArchivo> results)
    {
        int ok = 0, warn = 0, err = 0;
        foreach (var r in results)
        {
            if (r.Ok)
            {
                if (r.Advertencias.Count > 0) warn++;
                else ok++;
            }
            else err++;
        }
        return (ok, warn, err);
    }

    public static string GetAppDataDir()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string path = Path.Combine(appData, "CazadorYTM");
        try { Directory.CreateDirectory(path); }
        catch
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".CazadorYTM");
            try { Directory.CreateDirectory(path); }
            catch
            {
                path = Path.GetTempPath();
                Directory.CreateDirectory(path);
            }
        }
        return path;
    }

    public static string GetDefaultCookiesPath() => Path.Combine(GetAppDataDir(), "cookies.txt");

    public static string SanitizeLogMessage(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return string.Empty;
        msg = Regex.Replace(msg, @"(https?://[^:]+:)([^@\s]+)(@[a-zA-Z0-9.-]+)", "$1********$3");
        msg = Regex.Replace(msg, @"((?:token|key|sig|pass|password|signature|cookies)=)([^&\s#\)'\""]+)", "$1********", RegexOptions.IgnoreCase);
        return msg;
    }
}

public static class JsonHelper
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static string Serialize<T>(T obj) => JsonSerializer.Serialize(obj, _options);
    public static T? Deserialize<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, _options); }
        catch { return null; }
    }
}