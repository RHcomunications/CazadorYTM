namespace CazadorYTM.Core.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CazadorYTM.Core;

public class AppReleaseInfo
{
    public bool HasUpdate { get; set; }
    public string CurrentVersion { get; set; } = Constants.AppVersion;
    public string LatestVersion { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string? AssetDownloadUrl { get; set; }
    public string? AssetName { get; set; }
    public string HtmlUrl { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
}

public static class AppUpdaterService
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    static AppUpdaterService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"CazadorYTM/{Constants.AppVersion} (Windows NT 10.0; Win64; x64)");
        HttpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json, application/json, text/plain, */*");
    }

    public static async Task<AppReleaseInfo> CheckAppUpdateAsync(string repo = Constants.GitHubRepo)
    {
        var result = new AppReleaseInfo();
        try
        {
            var url = $"https://api.github.com/repos/{repo}/releases/latest";
            using var response = await HttpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return result;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("tag_name", out var tagProp))
            {
                result.TagName = tagProp.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("body", out var bodyProp))
            {
                result.ReleaseNotes = FormatReleaseNotesForDisplay(bodyProp.GetString() ?? string.Empty);
            }

            if (root.TryGetProperty("html_url", out var htmlProp))
            {
                result.HtmlUrl = htmlProp.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var pubDate))
            {
                result.PublishedAt = pubDate;
            }

            // Clean version tag: "v1.2.0" -> "1.2.0"
            var cleanTag = Regex.Replace(result.TagName, @"^[^\d]*", "");
            result.LatestVersion = cleanTag;

            var cleanLocal = Regex.Replace(Constants.AppVersion, @"^[^\d]*", "");

            result.HasUpdate = IsRemoteVersionNewer(cleanTag, cleanLocal);

            // Find release zip asset
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameProp) &&
                        asset.TryGetProperty("browser_download_url", out var dlProp))
                    {
                        var name = nameProp.GetString() ?? string.Empty;
                        var dlUrl = dlProp.GetString() ?? string.Empty;

                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            result.AssetName = name;
                            result.AssetDownloadUrl = dlUrl;
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppUpdater] Error comprobando actualización: {ex.Message}");
        }

        return result;
    }

    public static bool IsRemoteVersionNewer(string remoteVersionStr, string localVersionStr)
    {
        try
        {
            var normRemote = NormalizeVersionString(remoteVersionStr);
            var normLocal = NormalizeVersionString(localVersionStr);

            if (Version.TryParse(normRemote, out var remoteVer) &&
                Version.TryParse(normLocal, out var localVer))
            {
                return remoteVer > localVer;
            }
        }
        catch { }

        // Fallback comparison
        return string.Compare(remoteVersionStr, localVersionStr, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static string NormalizeVersionString(string version)
    {
        var clean = Regex.Replace(version ?? string.Empty, @"^[^\d]*", "");
        var parts = clean.Split(new[] { '.', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var nums = parts.Where(p => int.TryParse(p, out _)).Take(4).ToList();
        while (nums.Count < 3) nums.Add("0");
        return string.Join(".", nums);
    }

    public static async Task DownloadAndApplyUpdateAsync(
        string assetUrl,
        string targetBaseDir,
        Action<string>? statusCallback = null,
        Action<int>? progressCallback = null)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CazadorYTM_Update");
        if (Directory.Exists(tempRoot))
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
        Directory.CreateDirectory(tempRoot);

        var tempZip = Path.Combine(tempRoot, "update_package.zip");
        var extractedDir = Path.Combine(tempRoot, "extracted");

        statusCallback?.Invoke("Descargando paquete de actualización desde GitHub...");

        using (var response = await HttpClient.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var downloadedBytes = 0L;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                downloadedBytes += read;
                if (totalBytes > 0 && progressCallback != null)
                {
                    var pct = (int)(downloadedBytes * 100 / totalBytes);
                    progressCallback(Math.Clamp(pct, 0, 100));
                }
            }
        }

        statusCallback?.Invoke("Extrayendo paquete de actualización...");
        Directory.CreateDirectory(extractedDir);
        ZipFile.ExtractToDirectory(tempZip, extractedDir, true);

        // If files are nested in a subfolder inside the zip, locate the real content root
        var actualSourceDir = extractedDir;
        var subDirs = Directory.GetDirectories(extractedDir);
        if (subDirs.Length == 1 && !File.Exists(Path.Combine(extractedDir, "CazadorYTM.Gui.exe")) && !File.Exists(Path.Combine(extractedDir, "CazadorYTM.Gui.dll")))
        {
            actualSourceDir = subDirs[0];
        }

        statusCallback?.Invoke("Preparando script de actualización y reinicio...");

        var batPath = Path.Combine(tempRoot, "apply_update.bat");
        var batContent = new StringBuilder();
        batContent.AppendLine("@echo off");
        batContent.AppendLine("chcp 65001 > nul");
        batContent.AppendLine(":WAIT_LOOP");
        batContent.AppendLine("tasklist /FI \"IMAGENAME eq CazadorYTM.Gui.exe\" 2>NUL | find /I /N \"CazadorYTM.Gui.exe\">NUL");
        batContent.AppendLine("if \"%ERRORLEVEL%\"==\"0\" (");
        batContent.AppendLine("    timeout /t 1 /nobreak > nul");
        batContent.AppendLine("    goto WAIT_LOOP");
        batContent.AppendLine(")");
        batContent.AppendLine("timeout /t 1 /nobreak > nul");
        batContent.AppendLine($"xcopy \"{actualSourceDir}\\*\" \"{targetBaseDir}\\\" /E /Y /Q /R /H");
        batContent.AppendLine($"cd /d \"{targetBaseDir}\"");
        batContent.AppendLine("if exist \"CazadorYTM.Gui.exe\" (");
        batContent.AppendLine("    start \"\" \"CazadorYTM.Gui.exe\"");
        batContent.AppendLine(")");
        batContent.AppendLine("exit");

        File.WriteAllText(batPath, batContent.ToString(), Encoding.GetEncoding(65001));

        statusCallback?.Invoke("Reiniciando Cazador YTM para completar la actualización...");

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = tempRoot
        };

        Process.Start(psi);
    }

    public static string FormatReleaseNotesForDisplay(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var text = markdown;
        // Strip markdown header indicators (e.g. ## Title -> Title)
        text = Regex.Replace(text, @"^#{1,6}\s*", "", RegexOptions.Multiline);
        // Strip bold and italic markdown markers
        text = Regex.Replace(text, @"\*\*([^*]+)\*\*", "$1");
        text = Regex.Replace(text, @"\*([^*]+)\*", "$1");
        // Strip inline code markers
        text = Regex.Replace(text, @"`([^`]+)`", "$1");
        // Replace markdown list dashes with clear bullet points
        text = Regex.Replace(text, @"^\s*-\s+", "  • ", RegexOptions.Multiline);
        return text.Trim();
    }
}
