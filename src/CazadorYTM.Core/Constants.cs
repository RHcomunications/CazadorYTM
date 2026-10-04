namespace CazadorYTM.Core;

public static class Constants
{
    public const string AppVersion = "1.2.1";
    public const string AppName = "Cazador YTM";
    public const string AppTitle = $"{AppName} v{AppVersion}";
    public const string AppWindowTitle = $"{AppName} v{AppVersion}";
    public const string Company = "narayan project's";
    public const string Author = "narayan project's";
    public const string Copyright = "Copyright © 2026 narayan project's";
    public const string GitHubRepo = "RHcomunications/CazadorYTM";

    public static readonly string[] FormatsRaw = { "mp3", "flac", "m4a", "wav", "opus", "mp4" };
    public static readonly string[] FormatsDisplay = { "mp3", "flac", "m4a", "wav", "opus", "mp4" };
    public static readonly string[] Bitrates = { "128K", "192K", "256K", "320K" };
    public const int DefaultBitrateIndex = 3;

    public static readonly HashSet<string> LossyCodecs = new(StringComparer.OrdinalIgnoreCase) { "mp3", "m4a", "opus" };

    public static readonly string[] Browsers = { "Ninguno", "Chrome", "Firefox", "Edge", "Opera", "Brave", "Vivaldi" };
    public static readonly string[] SearchSources = { "YouTube Music", "YouTube (General)", "SoundCloud", "Universal (yt-dlp)" };

    public const int DefaultMaxConcurrent = 3;
    public static readonly string[] MaxConcurrentOptions = { "1 (Secuencial)", "2", "3", "4", "5" };
    public static readonly int[] MaxConcurrentValues = { 1, 2, 3, 4, 5 };

    public const int MaxRetries = 3;
    public const int RetryBackoffBase = 2;

    public static readonly string[] ThemeOptions = { "Automático", "Oscuro", "Claro" };
    public static readonly string[] LanguageOptions = { "Español", "English" };
    public static readonly string[] FontSizeOptions = { "Normal", "Grande", "Muy Grande" };

    public const string ConfigFilename = "cazador_config.json";
    public const string HistoryFilename = "downloads_history.json";

    public static readonly HashSet<string> ExcludedTxtFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ytdlp_help.txt", "ytdlp_help_utf8.txt"
    };

    public const string MultiDownloadFolder = "Cazador_YTM";
    public const string SingleDownloadFolder = "Descargas_Individuales";
    public const string FallbackMultiFolder = "Descargas_Multiples";

    public static readonly string[] TitleCleaningPatterns =
    {
        @" \(Official .*Video\)", @" \[Official .*Video\]",
        @" \(Lyric .*Video\)", @" \(Extended Mix\)",
        @" \[Extended Mix\]", @" \(Original Mix\)",
        @" \[ANJUNABEATS\]", @" \(Visualizer\)",
        @" - Official Audio"
    };
}