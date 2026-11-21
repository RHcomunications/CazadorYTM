namespace CazadorYTM.Core.Services;

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Genera un reporte de diagnóstico seguro y sanitizado para soporte técnico,
/// ocultando credenciales de proxy, cookies/tokens de sesión y rutas de perfil de usuario.
/// </summary>
public static partial class DiagnosticReportService
{
    [GeneratedRegex(@"(https?|socks5h?)://[^/\s:@]+:[^/\s@]+@", RegexOptions.IgnoreCase)]
    private static partial Regex ProxyCredentialsRegex();

    [GeneratedRegex(@"(?i)\b(SAPISID|__Secure-3PAPISID|__Secure-1PAPISID|SID|HSID|SSID|LOGIN_INFO|APISID|Authorization)""?\s*[:=]\s*""?(?:(?:Bearer|Basic|Token)\s+)?[^\s;,""]+", RegexOptions.None)]
    private static partial Regex SensitiveTokenRegex();

    [GeneratedRegex(@"(?i)\bBearer\s+[^\s;,""]+", RegexOptions.None)]
    private static partial Regex BearerTokenRegex();

    public static string BuildReport(
        AppConfig config,
        string baseDir,
        string? recentLogs = null,
        int maxLogLines = 200)
    {
        ArgumentNullException.ThrowIfNull(config);
        baseDir = string.IsNullOrWhiteSpace(baseDir) ? Helpers.GetBaseDir() : baseDir;

        var sb = new StringBuilder(4096);
        sb.AppendLine("============================================================");
        sb.AppendLine($"  {Constants.AppTitle} — Diagnostic Report");
        sb.AppendLine("============================================================");
        sb.AppendLine($"Generated (UTC):   {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
        sb.AppendLine($"App Version:       {Constants.AppVersion}");
        sb.AppendLine($"OS:                {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($".NET Runtime:      {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})");
        sb.AppendLine($"UI Language:       {config.Get("language", "es")}");
        sb.AppendLine();

        sb.AppendLine("[Engines]");
        sb.AppendLine($"  yt-dlp:          {(BinaryUpdater.IsYtdlpInstalled(baseDir) ? BinaryUpdater.GetLocalYtdlpVersion(baseDir) ?? "Installed" : "Not installed")}");
        sb.AppendLine($"  FFmpeg:          {(BinaryUpdater.IsFfmpegInstalled(baseDir) ? BinaryUpdater.GetLocalFfmpegVersion(baseDir) ?? "Installed" : "Not installed")}");
        sb.AppendLine($"  Deno:            {(BinaryUpdater.IsDenoInstalled(baseDir) ? BinaryUpdater.GetLocalDenoVersion(baseDir) ?? "Installed" : "Not installed")}");
        sb.AppendLine();

        sb.AppendLine("[Configuration (Sanitized)]");
        sb.AppendLine($"  Format:          {config.Get("format", "mp3")}");
        sb.AppendLine($"  Bitrate:         {config.Get("bitrate", "320K")}");
        sb.AppendLine($"  Concurrency:     {config.Get("max_concurrent", 3)}");
        sb.AppendLine($"  SponsorBlock:    {config.Get("sponsorblock", true)}");
        sb.AppendLine($"  Normalize:       {config.Get("normalize", true)}");
        sb.AppendLine($"  Embed Lyrics:    {config.Get("embed_lyrics", false)}");
        sb.AppendLine($"  Trim Silence:    {config.Get("trim_silence", false)}");
        sb.AppendLine($"  LRCLIB Fallback: {config.Get("lyrics_api_fallback", true)}");
        sb.AppendLine($"  Startup Tab:     {config.Get("startup_tab", "Descarga")}");
        sb.AppendLine($"  Proxy Configured:{(!string.IsNullOrWhiteSpace(config.Get("proxy", string.Empty)) ? " Yes (redacted)" : " No")}");

        var cookiesBrowser = config.Get("cookies_browser", "Ninguno");
        var cookiesPath = config.Get("cookies_path", Helpers.GetDefaultCookiesPath());
        var cookiesFileExists = !string.IsNullOrWhiteSpace(cookiesPath) && File.Exists(cookiesPath);
        var cookiesBytes = cookiesFileExists ? new FileInfo(cookiesPath).Length : 0L;
        sb.AppendLine($"  Cookies Browser: {cookiesBrowser}");
        sb.AppendLine($"  Cookies File:    {(cookiesFileExists ? $"Present ({cookiesBytes} bytes, content redacted)" : "Absent")}");
        sb.AppendLine();

        sb.AppendLine($"[Recent Logs (up to last {Math.Max(1, maxLogLines)} lines, sanitized)]");
        if (string.IsNullOrWhiteSpace(recentLogs))
        {
            sb.AppendLine("  (no logs available)");
        }
        else
        {
            var lines = recentLogs
                .Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var tail = lines.Skip(Math.Max(0, lines.Length - Math.Max(1, maxLogLines)));
            foreach (var line in tail)
            {
                sb.AppendLine(SanitizeText(line));
            }
        }

        return sb.ToString();
    }

    public static string SanitizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = ProxyCredentialsRegex().Replace(text, "$1://<redacted>@");
        result = SensitiveTokenRegex().Replace(result, "$1=<redacted>");
        result = BearerTokenRegex().Replace(result, "Bearer <redacted>");

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            result = result.Replace(userProfile, "<USERPROFILE>", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
