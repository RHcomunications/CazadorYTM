namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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

    private static bool DownloadFileRobust(string url, string finalTargetPath, Action<int>? progressCallback = null, bool validatePe = false, int maxRetries = 3)
    {
        var stagedPath = finalTargetPath + ".download";
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var response = HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                var totalSize = response.Content.Headers.ContentLength ?? -1L;
                var downloaded = 0L;
                var chunkSize = 64 * 1024;

                using (var stream = response.Content.ReadAsStream())
                using (var outFs = new FileStream(stagedPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[chunkSize];
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        outFs.Write(buffer, 0, bytesRead);
                        downloaded += bytesRead;
                        if (totalSize > 0 && progressCallback is not null)
                        {
                            var pct = Math.Min((int)(downloaded * 100 / totalSize), 100);
                            progressCallback(pct);
                        }
                    }
                }

                if (validatePe && Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    using var peFs = new FileStream(stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var header = new byte[2];
                    var readCount = peFs.Read(header, 0, 2);
                    if (readCount < 2 || header[0] != 0x4D || header[1] != 0x5A)
                        throw new InvalidOperationException("Descarga inválida: encabezado PE esperado 'MZ', recibido " + BitConverter.ToString(header));
                }

                SafeReplaceFile(stagedPath, finalTargetPath);
                BinaryManager.ClearCache();
                return true;
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

    private static void SafeReplaceFile(string sourcePath, string targetPath)
    {
        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        if (File.Exists(targetPath))
        {
            try
            {
                File.Replace(sourcePath, targetPath, null);
                return;
            }
            catch
            {
                var oldPath = targetPath + ".old";
                if (File.Exists(oldPath))
                {
                    try { File.Delete(oldPath); }
                    catch { }
                }
                try { File.Move(targetPath, oldPath); }
                catch { }
                File.Move(sourcePath, targetPath);
            }
        }
        else
        {
            File.Move(sourcePath, targetPath);
        }
    }

    public static string? GetLocalYtdlpVersion(string baseDir)
    {
        try
        {
            var exe = BinaryManager.GetYtDlpPath(baseDir);
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("--version");
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            var exited = process.WaitForExit(10000);
            if (!exited)
            {
                DownloadEngine.KillProcessTree(process.Id);
                return null;
            }
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

            var psi = new ProcessStartInfo
            {
                FileName = denoPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("--version");
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            var exited = process.WaitForExit(10000);
            if (!exited)
            {
                DownloadEngine.KillProcessTree(process.Id);
                return null;
            }

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

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("-version");
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            var exited = process.WaitForExit(10000);
            if (!exited)
            {
                DownloadEngine.KillProcessTree(process.Id);
                return null;
            }

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
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                return tag[1..];
            return tag;
        }
        catch { return null; }
    }

    public static string? GetRemoteYtdlpVersion()
    {
        try
        {
            var url = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";
            var json = HttpClient.GetStringAsync(url).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            if (!string.IsNullOrWhiteSpace(tag))
                return tag.Trim().TrimStart('v');
        }
        catch { }

        try
        {
            var url = "https://pypi.org/pypi/yt-dlp/json";
            var json = HttpClient.GetStringAsync(url).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("info").GetProperty("version").GetString();
        }
        catch { return null; }
    }

    public static string? GetRemoteFfmpegVersion()
    {
        try
        {
            var url = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest";
            var json = HttpClient.GetStringAsync(url).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("published_at", out var pubProp))
            {
                var pubStr = pubProp.GetString();
                if (!string.IsNullOrEmpty(pubStr) && DateTime.TryParse(pubStr, out var pubDate))
                {
                    return pubDate.ToString("yyyy.MM.dd");
                }
            }
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
        var updatesNeeded = new List<string>();

        // 1. yt-dlp check
        var localYt = GetLocalYtdlpVersion(baseDir);
        var remoteYt = GetRemoteYtdlpVersion();

        if (!IsYtdlpInstalled(baseDir) || string.IsNullOrEmpty(localYt))
        {
            updatesNeeded.Add("yt-dlp (Faltante)");
        }
        else if (!string.IsNullOrEmpty(remoteYt) && remoteYt != localYt)
        {
            if (CompareVersionStrings(remoteYt, localYt) > 0)
            {
                updatesNeeded.Add($"yt-dlp (Instalado: {localYt} -> Disponible: {remoteYt})");
            }
        }

        // 2. Deno check
        var localDeno = GetLocalDenoVersion(baseDir);
        var remoteDeno = GetRemoteDenoVersion();

        if (!IsDenoInstalled(baseDir) || string.IsNullOrEmpty(localDeno))
        {
            updatesNeeded.Add("Deno (Faltante)");
        }
        else if (!string.IsNullOrEmpty(remoteDeno) && remoteDeno != localDeno)
        {
            if (CompareVersionStrings(remoteDeno, localDeno) > 0)
            {
                updatesNeeded.Add($"Deno (Instalado: {localDeno} -> Disponible: {remoteDeno})");
            }
        }

        // 3. FFmpeg check
        var localFf = GetLocalFfmpegVersion(baseDir);
        var remoteFf = GetRemoteFfmpegVersion();

        if (!IsFfmpegInstalled(baseDir))
        {
            updatesNeeded.Add("FFmpeg (Faltante)");
        }
        else if (!string.IsNullOrEmpty(remoteFf) && !string.IsNullOrEmpty(localFf) && localFf != "Instalado")
        {
            if (CompareVersionStrings(remoteFf, localFf) > 0)
            {
                updatesNeeded.Add($"FFmpeg (Instalado: {localFf} -> Disponible: {remoteFf})");
            }
        }

        return updatesNeeded;
    }

    private static int CompareVersionStrings(string remote, string local)
    {
        try
        {
            var cleanR = remote.Replace("-", ".").Trim();
            var cleanL = local.Replace("-", ".").Trim();

            var partsR = cleanR.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
            var partsL = cleanL.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

            var maxLen = Math.Max(partsR.Length, partsL.Length);
            for (var i = 0; i < maxLen; i++)
            {
                var vr = i < partsR.Length ? partsR[i] : 0;
                var vl = i < partsL.Length ? partsL[i] : 0;
                if (vr > vl) return 1;
                if (vr < vl) return -1;
            }
            return 0;
        }
        catch
        {
            return string.Compare(remote, local, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void DownloadFfmpegWithProgress(string baseDir, Action<string>? callback, Action<int>? progressCallback)
    {
        var targetDirs = new List<string> { baseDir };
        var internalDir = Path.Combine(baseDir, "internal");
        if (Directory.Exists(internalDir))
            targetDirs.Add(internalDir);

        var zipPath = Path.Combine(baseDir, "ffmpeg_temp.zip");
        var extractDir = Path.Combine(baseDir, "ffmpeg_temp_extract");

        callback?.Invoke("[SISTEMA] Iniciando descarga del paquete completo de FFmpeg...");

        try
        {
            DownloadFileRobust("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip", zipPath, progressCallback: progressCallback, validatePe: false);

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
                    SafeReplaceFile(ffmpegSrc, destFfmpeg);
                    SafeReplaceFile(ffprobeSrc, destFfprobe);
                    if (ffplaySrc is not null)
                    {
                        var destFfplay = Path.Combine(tDir, "ffplay.exe");
                        SafeReplaceFile(ffplaySrc, destFfplay);
                    }
                }
                BinaryManager.ClearCache();
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
    }

    public static void DownloadDenoWithProgress(string baseDir, Action<string>? callback, Action<int>? progressCallback)
    {
        var targetDirs = new List<string> { baseDir };
        var internalDir = Path.Combine(baseDir, "internal");
        if (Directory.Exists(internalDir))
            targetDirs.Add(internalDir);

        var zipPath = Path.Combine(baseDir, "deno_temp.zip");
        var extractDir = Path.Combine(baseDir, "deno_temp_extract");

        callback?.Invoke("[SISTEMA] Iniciando descarga de Deno (Motor JavaScript)...");

        try
        {
            DownloadFileRobust("https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip", zipPath, progressCallback: progressCallback, validatePe: false);

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
                    SafeReplaceFile(denoSrc, destDeno);
                }
                BinaryManager.ClearCache();
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
    }

    public static void InstallOrUpdateYtdlp(string baseDir, Action<string>? callback, Action<int>? progressCallback)
    {
        var targetDirs = new List<string> { baseDir };
        var internalDir = Path.Combine(baseDir, "internal");
        if (Directory.Exists(internalDir))
            targetDirs.Add(internalDir);

        callback?.Invoke("[SISTEMA] Descargando última versión de yt-dlp desde GitHub Releases...");

        var primaryExePath = Path.Combine(baseDir, "yt-dlp.exe");
        try
        {
            DownloadFileRobust("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", primaryExePath, progressCallback: progressCallback, validatePe: true);

            foreach (var tDir in targetDirs.Where(d => d != baseDir))
            {
                Directory.CreateDirectory(tDir);
                var destYt = Path.Combine(tDir, "yt-dlp.exe");
                SafeReplaceFile(primaryExePath, destYt);
            }

            BinaryManager.ClearCache();
            callback?.Invoke("[SISTEMA] yt-dlp.exe actualizado a la última versión disponible.");
        }
        catch (Exception e)
        {
            callback?.Invoke($"[ERROR] No se pudo descargar yt-dlp.exe: {e.Message}");
        }
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

        foreach (var u in updates)
        {
            callback?.Invoke($"[INFO] Se actualizará: {u}");
            if (u.Contains("yt-dlp"))
                InstallOrUpdateYtdlp(baseDir, callback, progressCallback);
        }

        if (updates.Any(u => u.Contains("FFmpeg")))
            DownloadFfmpegWithProgress(baseDir, callback, progressCallback);

        if (updates.Any(u => u.Contains("Deno")))
            DownloadDenoWithProgress(baseDir, callback, progressCallback);

        BinaryManager.ClearCache();
        progressCallback?.Invoke(100);
        callback?.Invoke("[SISTEMA] Secuencia de actualización finalizada exitosamente.");
    }
}