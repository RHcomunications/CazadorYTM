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
    public string? AssetSha256 { get; set; }
    public string? ChecksumDownloadUrl { get; set; }
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

                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            result.AssetName = name;
                            result.AssetDownloadUrl = dlUrl;

                            if (asset.TryGetProperty("digest", out var digestProp) &&
                                DownloadIntegrity.TryNormalizeSha256(digestProp.GetString(), out var digest))
                            {
                                result.AssetSha256 = digest;
                            }

                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(result.AssetName) &&
                    root.TryGetProperty("assets", out var checksumAssets) &&
                    checksumAssets.ValueKind == JsonValueKind.Array)
                {
                    var checksumName = result.AssetName + ".sha256";
                    foreach (var asset in checksumAssets.EnumerateArray())
                    {
                        var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                        if (!string.Equals(name, checksumName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        result.ChecksumDownloadUrl = asset.TryGetProperty("browser_download_url", out var checksumUrlProp)
                            ? checksumUrlProp.GetString()
                            : null;
                        break;
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
        Action<int>? progressCallback = null,
        string? expectedSha256 = null,
        string? checksumUrl = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out var assetUri) ||
            assetUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("La URL del paquete de actualización no es válida.");
        }

        if (!string.IsNullOrWhiteSpace(checksumUrl) &&
            (!Uri.TryCreate(checksumUrl, UriKind.Absolute, out var checksumUri) ||
             checksumUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("La URL del checksum de actualización no es válida.");
        }

        if (!DownloadIntegrity.TryNormalizeSha256(expectedSha256, out var verifiedSha256) &&
            !string.IsNullOrWhiteSpace(checksumUrl))
        {
            verifiedSha256 = await DownloadIntegrity.FetchSha256Async(
                HttpClient,
                checksumUrl,
                cancellationToken) ?? string.Empty;
        }

        if (!DownloadIntegrity.TryNormalizeSha256(verifiedSha256, out verifiedSha256))
        {
            throw new InvalidOperationException(
                "La actualización no incluye un checksum SHA-256 verificable y no se aplicará.");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "CazadorYTM_Update");
        if (Directory.Exists(tempRoot))
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
        Directory.CreateDirectory(tempRoot);

        var tempZip = Path.Combine(tempRoot, "update_package.zip");
        var extractedDir = Path.Combine(tempRoot, "extracted");

        statusCallback?.Invoke("Descargando paquete de actualización desde GitHub...");

        try
        {
            using (var response = await HttpClient.GetAsync(
                       assetUrl,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidDataException("La descarga del paquete no usa HTTPS.");
                }

                const long maxDownloadBytes = 1024L * 1024 * 1024;
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                if (totalBytes > maxDownloadBytes)
                {
                    throw new InvalidDataException("El paquete de actualización excede el tamaño máximo permitido.");
                }

                var downloadedBytes = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    downloadedBytes += read;
                    if (downloadedBytes > 1024L * 1024 * 1024)
                    {
                        throw new InvalidDataException("El paquete de actualización excede el tamaño máximo permitido.");
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    if (totalBytes > 0 && progressCallback != null)
                    {
                        var pct = (int)(downloadedBytes * 100 / totalBytes);
                        progressCallback(Math.Clamp(pct, 0, 100));
                    }
                }
            }

            statusCallback?.Invoke("Verificando integridad del paquete...");
            await DownloadIntegrity.VerifyFileAsync(tempZip, verifiedSha256, cancellationToken);
        }
        catch
        {
            if (File.Exists(tempZip))
            {
                try { File.Delete(tempZip); } catch { }
            }
            throw;
        }

        statusCallback?.Invoke("Extrayendo paquete de actualización...");
        Directory.CreateDirectory(extractedDir);
        ValidateZipArchive(tempZip, extractedDir);
        ZipFile.ExtractToDirectory(tempZip, extractedDir, true);

        // If files are nested in a subfolder inside the zip, locate the real content root
        var actualSourceDir = extractedDir;
        var subDirs = Directory.GetDirectories(extractedDir);
        if (subDirs.Length == 1 && !File.Exists(Path.Combine(extractedDir, "CazadorYTM.Gui.exe")) && !File.Exists(Path.Combine(extractedDir, "CazadorYTM.Gui.dll")))
        {
            actualSourceDir = subDirs[0];
        }

        if (!File.Exists(Path.Combine(actualSourceDir, "CazadorYTM.Gui.exe")) &&
            !File.Exists(Path.Combine(actualSourceDir, "CazadorYTM.Gui.dll")))
        {
            throw new InvalidDataException("El paquete de actualización no contiene CazadorYTM.Gui.exe ni CazadorYTM.Gui.dll.");
        }

        statusCallback?.Invoke("Preparando script de actualización y reinicio...");

        var batPath = Path.Combine(tempRoot, "apply_update.bat");
        var batContent = GenerateUpdateBatchScript(actualSourceDir, targetBaseDir, tempRoot);
        File.WriteAllText(batPath, batContent, Encoding.GetEncoding(65001));

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

    private static void ValidateZipArchive(string zipPath, string destinationDir)
    {
        var destinationRoot = Path.GetFullPath(destinationDir);
        long totalLength = 0;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            totalLength += entry.Length;
            if (totalLength > 2L * 1024 * 1024 * 1024)
            {
                throw new InvalidDataException("El paquete de actualización excede el tamaño máximo permitido.");
            }

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            var relativePath = Path.GetRelativePath(destinationRoot, targetPath);
            if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
            {
                throw new InvalidDataException("El paquete de actualización contiene una ruta no válida.");
            }
        }
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

    public static string GenerateUpdateBatchScript(string actualSourceDir, string targetBaseDir, string? tempRoot = null)
    {
        var batContent = new StringBuilder();
        batContent.AppendLine("@echo off");
        batContent.AppendLine("chcp 65001 > nul");
        batContent.AppendLine("set RETRIES=0");
        batContent.AppendLine(":WAIT_LOOP");
        batContent.AppendLine("tasklist /FI \"IMAGENAME eq CazadorYTM.Gui.exe\" 2>NUL | find /I /N \"CazadorYTM.Gui.exe\">NUL");
        batContent.AppendLine("if \"%ERRORLEVEL%\"==\"0\" (");
        batContent.AppendLine("    set /a RETRIES+=1");
        batContent.AppendLine("    if %RETRIES% geq 30 (");
        batContent.AppendLine("        taskkill /F /IM CazadorYTM.Gui.exe >nul 2>&1");
        batContent.AppendLine("        timeout /t 1 /nobreak > nul");
        batContent.AppendLine("        goto APPLY_COPY");
        batContent.AppendLine("    )");
        batContent.AppendLine("    timeout /t 1 /nobreak > nul");
        batContent.AppendLine("    goto WAIT_LOOP");
        batContent.AppendLine(")");
        batContent.AppendLine(":APPLY_COPY");
        batContent.AppendLine("timeout /t 1 /nobreak > nul");
        batContent.AppendLine($"xcopy \"{actualSourceDir}\\*\" \"{targetBaseDir}\\\" /E /Y /Q /R /H");
        batContent.AppendLine($"cd /d \"{targetBaseDir}\"");
        batContent.AppendLine("if exist \"CazadorYTM.Gui.exe\" (");
        batContent.AppendLine("    start \"\" \"CazadorYTM.Gui.exe\"");
        batContent.AppendLine(")");
        if (!string.IsNullOrEmpty(tempRoot))
        {
            batContent.AppendLine("cd /d \"%TEMP%\"");
            batContent.AppendLine($"start \"\" /b cmd /c \"timeout /t 3 /nobreak >nul & rd /s /q \\\"{tempRoot}\\\"\"");
        }
        batContent.AppendLine("exit");
        return batContent.ToString();
    }

    public static bool CleanStaleUpdateArtifacts(string? tempPath = null)
    {
        var cleaned = false;
        try
        {
            var root = tempPath ?? Path.Combine(Path.GetTempPath(), "CazadorYTM_Update");
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
                cleaned = true;
            }
        }
        catch { }

        return cleaned;
    }
}
