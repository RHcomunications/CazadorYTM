namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CazadorYTM.Core;

public class UpdateCheckResult
{
    public List<string> UpdatesNeeded { get; set; } = new();
    public bool HasUpdates => UpdatesNeeded.Count > 0;
}

public class BinaryUpdater
{
    private readonly string _baseDir;
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    static BinaryUpdater()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"CazadorYTM/{Constants.AppVersion} (Windows NT 10.0; Win64; x64)");
        HttpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json, application/json, text/plain, */*");
    }

    public BinaryUpdater(string? baseDir = null)
    {
        _baseDir = baseDir ?? Helpers.GetBaseDir();
    }

    public UpdateCheckResult CheckUpdates()
    {
        return new UpdateCheckResult { UpdatesNeeded = CheckUpdates(_baseDir) };
    }

    public void UpdateAll(Action<string>? callback = null, Action<int>? progressCallback = null, bool forceFfmpeg = false, Action<string>? loggerCallback = null)
    {
        if (loggerCallback is not null && callback is null)
            callback = loggerCallback;

        UpdateAll(_baseDir, callback, progressCallback, forceFfmpeg, loggerCallback);
    }

    private static bool DownloadFileRobust(
        string url,
        string finalTargetPath,
        string? expectedSha256,
        Action<int>? progressCallback = null,
        bool validatePe = false,
        int maxRetries = 3)
    {
        if (!DownloadIntegrity.TryNormalizeSha256(expectedSha256, out var expectedHash))
        {
            throw new InvalidOperationException("No se pudo obtener un checksum SHA-256 válido para la descarga.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("La URL de descarga no es válida.");
        }

        const long maxDownloadBytes = 512L * 1024 * 1024;
        var stagedPath = finalTargetPath + ".download";
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var response = HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidDataException("La descarga del componente no usa HTTPS.");
                }

                var totalSize = response.Content.Headers.ContentLength ?? -1L;
                if (totalSize > maxDownloadBytes)
                {
                    throw new InvalidDataException("La descarga excede el tamaño máximo permitido.");
                }

                var downloaded = 0L;
                var chunkSize = 64 * 1024;

                using (var stream = response.Content.ReadAsStream())
                using (var outFs = new FileStream(stagedPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[chunkSize];
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        downloaded += bytesRead;
                        if (downloaded > maxDownloadBytes)
                        {
                            throw new InvalidDataException("La descarga excede el tamaño máximo permitido.");
                        }

                        outFs.Write(buffer, 0, bytesRead);
                        if (totalSize > 0 && progressCallback is not null)
                        {
                            var pct = Math.Min((int)(downloaded * 100 / totalSize), 100);
                            progressCallback(pct);
                        }
                    }
                }

                DownloadIntegrity.VerifyFileAsync(stagedPath, expectedHash).GetAwaiter().GetResult();

                if (validatePe && Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    using var peFs = new FileStream(stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var header = new byte[2];
                    var readCount = peFs.Read(header, 0, 2);
                    if (readCount < 2 || header[0] != 0x4D || header[1] != 0x5A)
                        throw new InvalidDataException("Descarga inválida: encabezado PE esperado 'MZ', recibido " + BitConverter.ToString(header));
                }

                BinaryUpdateHelpers.SafeReplaceFile(stagedPath, finalTargetPath);
                BinaryManager.ClearCache();
                return true;
            }
            catch (InvalidDataException)
            {
                if (File.Exists(stagedPath))
                {
                    try { File.Delete(stagedPath); }
                    catch (IOException) { }
                }
                throw;
            }
            catch (Exception e)
            {
                lastException = e;
                if (File.Exists(stagedPath))
                {
                    try { File.Delete(stagedPath); }
                    catch (IOException) { }
                }
                if (attempt < maxRetries)
                    Thread.Sleep((int)Math.Pow(2, attempt) * 1000);
            }
        }

        if (lastException is not null)
            throw lastException;
        return false;
    }

    public static string? GetLocalYtdlpVersion(string baseDir)
    {
        try
        {
            var exe = BinaryManager.GetYtDlpPath(baseDir);
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                return null;

            var result = ProcessRunner.RunAsync(exe, new[] { "--version" }, timeoutMs: 10000).GetAwaiter().GetResult();
            if (!result.Success)
                return null;

            var output = result.StandardOutput.Trim();
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch { return null; }
    }

    public static string? GetLocalDenoVersion(string baseDir)
    {
        try
        {
            var denoPath = BinaryManager.GetDenoPath(baseDir);
            if (string.IsNullOrEmpty(denoPath) || !File.Exists(denoPath)) return null;

            var result = ProcessRunner.RunAsync(denoPath, new[] { "--version" }, timeoutMs: 10000).GetAwaiter().GetResult();
            if (!result.Success) return null;

            var output = result.StandardOutput;
            var match = Regex.Match(output, @"deno\s+([0-9]+\.[0-9]+\.[0-9]+)");
            if (match.Success)
                return match.Groups[1].Value;

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0 && lines[0].Trim().StartsWith("deno "))
            {
                var parts = lines[0].Trim().Split(' ');
                if (parts.Length > 1) return parts[1];
            }
            return null;
        }
        catch { return null; }
    }

    public static string? GetLocalFfmpegVersion(string baseDir)
    {
        try
        {
            var ffmpegDir = BinaryManager.FindFfmpegPath(baseDir);
            if (string.IsNullOrEmpty(ffmpegDir)) return null;
            var ffmpegExe = Path.Combine(ffmpegDir, Environment.OSVersion.Platform == PlatformID.Win32NT ? "ffmpeg.exe" : "ffmpeg");
            if (!File.Exists(ffmpegExe)) return null;

            var result = ProcessRunner.RunAsync(ffmpegExe, new[] { "-version" }, timeoutMs: 10000).GetAwaiter().GetResult();
            if (!result.Success) return null;

            var output = result.StandardOutput;

            // Extract date or version string like 2026-07-21 or N-125708-20260721
            var matchDate = Regex.Match(output, @"(202[0-9][-.]?[0-1][0-9][-.]?[0-3][0-9])");
            if (matchDate.Success)
            {
                var d = matchDate.Groups[1].Value.Replace("-", "").Replace(".", "");
                if (d.Length == 8)
                    return $"{d.Substring(0, 4)}.{d.Substring(4, 2)}.{d.Substring(6, 2)}";
            }

            var matchVer = Regex.Match(output, @"ffmpeg version (\S+)");
            if (matchVer.Success)
                return matchVer.Groups[1].Value;

            return "Instalado";
        }
        catch { return null; }
    }

    public static string? GetRemoteDenoVersion()
    {
        try
        {
            var url = "https://api.github.com/repos/denoland/deno/releases/latest";
            var json = HttpClient.GetStringAsync(url).GetAwaiter().GetResult();
            return BinaryUpdateLogic.ParseGithubTag(json);
        }
        catch { return null; }
    }

    public static string? GetRemoteYtdlpVersion()
    {
        try
        {
            var url = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";
            var json = HttpClient.GetStringAsync(url).GetAwaiter().GetResult();
            var tag = BinaryUpdateLogic.ParseYtdlpGithubTag(json);
            if (tag is not null)
                return tag;
        }
        catch { }

        try
        {
            var url = "https://pypi.org/pypi/yt-dlp/json";
            var json = HttpClient.GetStringAsync(url).GetAwaiter().GetResult();
            return BinaryUpdateLogic.ParseYtdlpPyPiVersion(json);
        }
        catch { return null; }
    }

    private static GithubAssetInfo GetLatestGithubAsset(string repo, string assetName)
    {
        var url = $"https://api.github.com/repos/{repo}/releases/latest";
        var json = HttpClient.GetStringAsync(url).GetAwaiter().GetResult();
        var asset = BinaryUpdateLogic.ParseGithubAsset(json, assetName);
        if (asset is null || string.IsNullOrEmpty(asset.Sha256))
        {
            throw new InvalidOperationException(
                $"No se pudo verificar el checksum SHA-256 del componente '{assetName}'.");
        }

        return asset;
    }

    public static string? GetRemoteFfmpegVersion()
    {
        try
        {
            var url = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest";
            var json = HttpClient.GetStringAsync(url).GetAwaiter().GetResult();
            return BinaryUpdateLogic.ParseFfmpegBuildDate(json);
        }
        catch { }

        return null;
    }

    public static bool IsFfmpegInstalled(string baseDir)
    {
        return BinaryManager.FindFfmpegPath(baseDir) is not null;
    }

    public static bool IsYtdlpInstalled(string baseDir)
    {
        try
        {
            BinaryManager.GetYtDlpPath(baseDir);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    public static bool IsDenoInstalled(string baseDir)
    {
        return BinaryManager.GetDenoPath(baseDir) is not null;
    }

    public static List<string> CheckUpdates(string baseDir)
    {
        return BinaryUpdateLogic.BuildUpdatesNeeded(
            IsYtdlpInstalled(baseDir), GetLocalYtdlpVersion(baseDir), GetRemoteYtdlpVersion(),
            IsDenoInstalled(baseDir), GetLocalDenoVersion(baseDir), GetRemoteDenoVersion(),
            IsFfmpegInstalled(baseDir), GetLocalFfmpegVersion(baseDir), GetRemoteFfmpegVersion());
    }

    public static bool DownloadFfmpegWithProgress(string baseDir, Action<string>? callback, Action<int>? progressCallback)
    {
        var targetDirs = new List<string> { baseDir };
        var internalDir = Path.Combine(baseDir, "internal");
        if (Directory.Exists(internalDir))
            targetDirs.Add(internalDir);

        var zipPath = Path.Combine(baseDir, "ffmpeg_temp.zip");
        var extractDir = Path.Combine(baseDir, "ffmpeg_temp_extract");

        callback?.Invoke("[SISTEMA] Iniciando descarga del paquete completo de FFmpeg...");
        var installed = false;

        try
        {
            var asset = GetLatestGithubAsset("BtbN/FFmpeg-Builds", "ffmpeg-master-latest-win64-gpl.zip");
            DownloadFileRobust(asset.DownloadUrl, zipPath, asset.Sha256, progressCallback: progressCallback, validatePe: false);

            callback?.Invoke("[SISTEMA] Extrayendo ejecutables de FFmpeg...");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            string? ffmpegSrc = null, ffprobeSrc = null, ffplaySrc = null;
            foreach (var rootDir in Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories).Concat(new[] { extractDir }))
            {
                foreach (var file in Directory.GetFiles(rootDir))
                {
                    var name = Path.GetFileName(file);
                    if (name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase)) ffmpegSrc = file;
                    if (name.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase)) ffprobeSrc = file;
                    if (name.Equals("ffplay.exe", StringComparison.OrdinalIgnoreCase)) ffplaySrc = file;
                }
            }

            if (ffmpegSrc is not null && ffprobeSrc is not null)
            {
                foreach (var tDir in targetDirs)
                {
                    Directory.CreateDirectory(tDir);
                    var destFfmpeg = Path.Combine(tDir, "ffmpeg.exe");
                    var destFfprobe = Path.Combine(tDir, "ffprobe.exe");
                    BinaryUpdateHelpers.SafeCopyFile(ffmpegSrc, destFfmpeg);
                    BinaryUpdateHelpers.SafeCopyFile(ffprobeSrc, destFfprobe);
                    if (ffplaySrc is not null)
                    {
                        var destFfplay = Path.Combine(tDir, "ffplay.exe");
                        BinaryUpdateHelpers.SafeCopyFile(ffplaySrc, destFfplay);
                    }
                }
                BinaryManager.ClearCache();
                installed = true;
                callback?.Invoke("[SISTEMA] FFmpeg (ffmpeg, ffprobe, ffplay) instalado y actualizado correctamente.");
            }
            else
            {
                callback?.Invoke("[ERROR] Faltan archivos esenciales en el paquete de FFmpeg.");
            }
        }
        catch (Exception e)
        {
            callback?.Invoke($"[ERROR] No se pudo instalar FFmpeg: {e.Message}");
        }
        finally
        {
            if (File.Exists(zipPath)) try { File.Delete(zipPath); } catch { }
            if (Directory.Exists(extractDir)) try { Directory.Delete(extractDir, true); } catch { }
        }

        return installed;
    }

    public static bool DownloadDenoWithProgress(string baseDir, Action<string>? callback, Action<int>? progressCallback)
    {
        var targetDirs = new List<string> { baseDir };
        var internalDir = Path.Combine(baseDir, "internal");
        if (Directory.Exists(internalDir))
            targetDirs.Add(internalDir);

        var zipPath = Path.Combine(baseDir, "deno_temp.zip");
        var extractDir = Path.Combine(baseDir, "deno_temp_extract");

        callback?.Invoke("[SISTEMA] Iniciando descarga de Deno (Motor JavaScript)...");
        var installed = false;

        try
        {
            var asset = GetLatestGithubAsset("denoland/deno", "deno-x86_64-pc-windows-msvc.zip");
            DownloadFileRobust(asset.DownloadUrl, zipPath, asset.Sha256, progressCallback: progressCallback, validatePe: false);

            callback?.Invoke("[SISTEMA] Extrayendo Deno...");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            string? denoSrc = null;
            foreach (var rootDir in Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories).Concat(new[] { extractDir }))
            {
                foreach (var file in Directory.GetFiles(rootDir))
                {
                    var name = Path.GetFileName(file);
                    if (name.Equals("deno.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("deno", StringComparison.OrdinalIgnoreCase))
                        denoSrc = file;
                }
            }

            if (denoSrc is not null)
            {
                foreach (var tDir in targetDirs)
                {
                    Directory.CreateDirectory(tDir);
                    var destDeno = Path.Combine(tDir, Environment.OSVersion.Platform == PlatformID.Win32NT ? "deno.exe" : "deno");
                    BinaryUpdateHelpers.SafeCopyFile(denoSrc, destDeno);
                }
                BinaryManager.ClearCache();
                installed = true;
                callback?.Invoke("[SISTEMA] Deno instalado y actualizado correctamente.");
            }
            else
            {
                callback?.Invoke("[ERROR] Faltan archivos en el paquete de Deno.");
            }
        }
        catch (Exception e)
        {
            callback?.Invoke($"[ERROR] No se pudo instalar Deno: {e.Message}");
        }
        finally
        {
            if (File.Exists(zipPath)) try { File.Delete(zipPath); } catch { }
            if (Directory.Exists(extractDir)) try { Directory.Delete(extractDir, true); } catch { }
        }

        return installed;
    }

    public static bool InstallOrUpdateYtdlp(string baseDir, Action<string>? callback, Action<int>? progressCallback)
    {
        var targetDirs = new List<string> { baseDir };
        var internalDir = Path.Combine(baseDir, "internal");
        if (Directory.Exists(internalDir))
            targetDirs.Add(internalDir);

        callback?.Invoke("[SISTEMA] Descargando última versión de yt-dlp desde GitHub Releases...");

        var primaryExePath = Path.Combine(baseDir, "yt-dlp.exe");
        var installed = false;
        try
        {
            var asset = GetLatestGithubAsset("yt-dlp/yt-dlp", "yt-dlp.exe");
            DownloadFileRobust(asset.DownloadUrl, primaryExePath, asset.Sha256, progressCallback: progressCallback, validatePe: true);

            foreach (var tDir in targetDirs.Where(d => d != baseDir))
            {
                Directory.CreateDirectory(tDir);
                var destYt = Path.Combine(tDir, "yt-dlp.exe");
                BinaryUpdateHelpers.SafeCopyFile(primaryExePath, destYt);
            }

            BinaryManager.ClearCache();
            installed = true;
            callback?.Invoke("[SISTEMA] yt-dlp.exe actualizado a la última versión disponible.");
        }
        catch (Exception e)
        {
            callback?.Invoke($"[ERROR] No se pudo descargar yt-dlp.exe: {e.Message}");
        }

        return installed;
    }

    public static void UpdateAll(string baseDir, Action<string>? callback = null, Action<int>? progressCallback = null, bool forceFfmpeg = false, Action<string>? loggerCallback = null)
    {
        if (loggerCallback is not null && callback is null)
            callback = loggerCallback;

        var updates = CheckUpdates(baseDir);

        if (forceFfmpeg && !updates.Any(u => u.Contains("FFmpeg")))
            updates.Add("FFmpeg (Forzado)");

        if (!updates.Any())
        {
            callback?.Invoke("[SISTEMA] Todos los componentes están en su versión más reciente.");
            progressCallback?.Invoke(100);
            return;
        }

        var failed = new List<string>();

        foreach (var u in updates)
        {
            callback?.Invoke($"[INFO] Se actualizará: {u}");
            if (u.Contains("yt-dlp") && !InstallOrUpdateYtdlp(baseDir, callback, progressCallback))
                failed.Add("yt-dlp");
        }

        if (updates.Any(u => u.Contains("FFmpeg")) &&
            !DownloadFfmpegWithProgress(baseDir, callback, progressCallback))
        {
            failed.Add("FFmpeg");
        }

        if (updates.Any(u => u.Contains("Deno")) &&
            !DownloadDenoWithProgress(baseDir, callback, progressCallback))
        {
            failed.Add("Deno");
        }

        BinaryManager.ClearCache();
        progressCallback?.Invoke(100);

        if (failed.Count > 0)
        {
            callback?.Invoke($"[ERROR] Actualización incompleta. Componentes con error: {string.Join(", ", failed)}.");
        }
        else
        {
            callback?.Invoke("[SISTEMA] Secuencia de actualización finalizada exitosamente.");
        }
    }
}