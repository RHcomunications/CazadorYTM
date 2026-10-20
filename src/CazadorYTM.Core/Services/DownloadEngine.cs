namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

public static class DownloadEngine
{
    public delegate int DownloadOperation(CancellationToken? cancelEvent, Action<string>? loggerCallback, Action<Dictionary<string, string>>? progressCallback);

    private static readonly Regex AnsiEscapeRegex = new(@"\x1b\[[0-9;]*[a-zA-ZK]|\x1b\[[0-9;]*[A-Za-z]");
    private static readonly Regex ProgressRegex = new(@"\[download\]\s+(\d+(?:\.\d+)?)%");
    private static readonly Regex DetailRegex = new(@"\[download\]\s+(\d+(?:\.\d+)?)%\s+of\s+(\S+)\s+at\s+(\S+)\s+ETA\s+(\S+)");
    private static readonly Regex DestRegex = new(@"Destination:\s*""?([^""\n]+)""?");
    private static readonly Regex AlreadyDownloadedRegex = new(@"\[download\]\s+""?([^""\n]+?)""?\s+has already been downloaded");
    private static readonly Regex MergingRegex = new(@"(?:\[Merger\]\s+)?Merging formats into\s+""?([^""\n]+)""?");
    private static readonly Regex AddingThumbnailRegex = new(@"Adding thumbnail to\s*""?([^""\n]+)""?");
    private const int DownloadTimeoutMs = 2 * 60 * 60 * 1000;

    private static readonly string[] NonRetryablePatterns =
    {
        "http error 404", "404 not found", "video unavailable",
        "private video", "this video is private", "confirm your age"
    };

    public static void CleanupTempFiles(string directory = ".", string? prefix = null)
    {
        var patterns = new[]
        {
            "*.live_chat.json", "*.live_chat.json.part", "*.live_chat.json.part-Frag*",
            "*.json.part", "*.part", "*.vtt", "*.meta",
            "_ERRORES_DE_DESCARGA.txt"
        };

        foreach (var pattern in patterns)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
                {
                    if (prefix != null)
                    {
                        var fileName = Path.GetFileName(file);
                        var cleanName = fileName.Split(new[] { ".part" }, StringSplitOptions.None)[0]
                            .Split(new[] { ".f" }, StringSplitOptions.None)[0];
                        if (!cleanName.StartsWith(prefix)) continue;
                    }
                    try { File.Delete(file); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (DirectoryNotFoundException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static void KillProcessTree(int pid)
    {
        if (pid <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(3000);
            return;
        }
        catch
        {
        }

        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return;
        }

        try
        {
            var psi = new ProcessStartInfo("taskkill", $"/F /T /PID {pid}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var taskkill = Process.Start(psi);
            if (taskkill is not null && !taskkill.WaitForExit(5000))
            {
                try { taskkill.Kill(entireProcessTree: true); } catch { }
            }
        }
        catch { }
    }

    public static bool TrimSilenceFfmpeg(string filepath, string? ffmpegLoc = null,
        CancellationToken? cancelEvent = null, string? bitrate = null)
    {
        CheckCancelled(cancelEvent);
        if (string.IsNullOrEmpty(filepath) || !File.Exists(filepath))
            return false;

        if (string.IsNullOrEmpty(ffmpegLoc))
            ffmpegLoc = BinaryManager.FindFfmpegPath(Helpers.GetBaseDir());
        if (string.IsNullOrEmpty(ffmpegLoc))
            return false;

        var ffmpegExe = Path.Combine(ffmpegLoc, Environment.OSVersion.Platform == PlatformID.Win32NT ? "ffmpeg.exe" : "ffmpeg");
        if (!File.Exists(ffmpegExe))
            return false;

        var tempFilepath = filepath + ".tmp_silence" + Path.GetExtension(filepath);

        var ext = Path.GetExtension(filepath).ToLower();
        var qualityArgs = new List<string>();
        if (ext == ".mp3")
            qualityArgs.AddRange(new[] { "-codec:a", "libmp3lame", "-b:a", bitrate ?? "320k" });
        else if (ext == ".flac")
            qualityArgs.AddRange(new[] { "-codec:a", "flac" });
        else if (ext == ".wav")
            qualityArgs.AddRange(new[] { "-codec:a", "pcm_s16le" });
        else if (ext == ".m4a")
            qualityArgs.AddRange(new[] { "-codec:a", "aac", "-b:a", bitrate ?? "256k" });
        else if (ext == ".opus")
            qualityArgs.AddRange(new[] { "-codec:a", "libopus", "-b:a", bitrate ?? "190k" });
        else
            qualityArgs.AddRange(bitrate != null ? new[] { "-b:a", bitrate } : new[] { "-q:a", "0" });

        var args = new List<string>
        {
            "-y", "-v", "error", "-i", filepath,
            "-af", "silenceremove=start_threshold=-50dB:start_duration=0.5:stop_threshold=-50dB:stop_duration=0.5:stop_periods=-1"
        };
        args.AddRange(qualityArgs);
        args.Add(tempFilepath);

        var fileSizeMb = new FileInfo(filepath).Length / (1024.0 * 1024.0);
        var dynamicTimeout = Math.Max(60, (int)(fileSizeMb * 15) + 30);

        try
        {
            CheckCancelled(cancelEvent);
            var result = ProcessRunner.RunAsync(
                ffmpegExe,
                args,
                timeoutMs: dynamicTimeout * 1000,
                cancellationToken: cancelEvent ?? CancellationToken.None).GetAwaiter().GetResult();

            CheckCancelled(cancelEvent);

            if (result.Success && File.Exists(tempFilepath) && new FileInfo(tempFilepath).Length > 0)
            {
                File.Replace(tempFilepath, filepath, null);
                return true;
            }
        }
        catch (DownloadCancelled) { throw; }
        catch { }
        finally
        {
            if (File.Exists(tempFilepath))
            {
                try { File.Delete(tempFilepath); }
                catch (IOException) { }
            }
        }

        return false;
    }

    private static void CheckCancelled(CancellationToken? cancelEvent)
    {
        if (cancelEvent.HasValue && cancelEvent.Value.IsCancellationRequested)
            throw new DownloadCancelled("Descarga cancelada por el usuario.");
    }

    public static int DownloadUrl(string url, string outputTemplate, string formatType = "mp3", string bitrate = "320k",
        bool sponsorblock = false, string? cookiesBrowser = null, string? playlistItems = null,
        string? rawArgs = null, Action<string>? loggerCallback = null,
        Action<Dictionary<string, string>>? progressCallback = null,
        string? ffmpegLocation = null, string? proxy = null, bool normalize = false,
        bool embedLyrics = false, CancellationToken? cancelEvent = null,
        bool trimSilence = false, bool lyricsApiFallback = true, string? cookiesPath = null)
    {
        CheckCancelled(cancelEvent);

        var exe = BinaryManager.GetYtDlpPath();
        if (string.IsNullOrEmpty(exe))
        {
            throw new InvalidOperationException("No se encontró yt-dlp.");
        }

        var args = YtDlpArgumentBuilder.Build(formatType, bitrate, sponsorblock, cookiesBrowser,
            playlistItems, rawArgs, ffmpegLocation, proxy, normalize, embedLyrics,
            cookiesPath: cookiesPath);

        args.Add("-o");
        args.Add(outputTemplate);
        args.Add("--no-playlist");
        args.Add("--");
        args.Add(url);

        var finalFilepath = string.Empty;
        var lastLogPct = -10;
        var nonRetryableFound = false;
        var lastErrorLine = string.Empty;
        var lineLock = new object();
        double lastDispatchedPct = -1;
        var progressDict = new Dictionary<string, string>(4);

        void ProcessLine(string? line)
        {
            if (string.IsNullOrEmpty(line)) return;

            var cleanLine = AnsiEscapeRegex.Replace(line, "").Trim();
            if (string.IsNullOrEmpty(cleanLine)) return;

            lock (lineLock)
            {
                if (cleanLine.StartsWith("[download]", StringComparison.OrdinalIgnoreCase))
                {
                    if (cleanLine.Contains("Destination:", StringComparison.OrdinalIgnoreCase))
                    {
                        var mDest = DestRegex.Match(cleanLine);
                        if (mDest.Success) finalFilepath = mDest.Groups[1].Value.Trim();
                    }
                    else if (cleanLine.Contains("has already been downloaded", StringComparison.OrdinalIgnoreCase))
                    {
                        var mAlready = AlreadyDownloadedRegex.Match(cleanLine);
                        if (mAlready.Success) finalFilepath = mAlready.Groups[1].Value.Trim();
                    }
                    else
                    {
                        var mDetail = DetailRegex.Match(cleanLine);
                        if (mDetail.Success && float.TryParse(mDetail.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct))
                        {
                            var size = mDetail.Groups[2].Value;
                            var speed = mDetail.Groups[3].Value;
                            var eta = mDetail.Groups[4].Value;

                            if (Math.Abs(pct - lastDispatchedPct) >= 1.0 || pct >= 100.0)
                            {
                                lastDispatchedPct = pct;
                                if (progressCallback != null)
                                {
                                    progressDict["percent"] = pct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                                    progressDict["speed"] = speed;
                                    progressDict["eta"] = eta;
                                    progressDict["size"] = size;
                                    progressCallback(new Dictionary<string, string>(progressDict));
                                }
                            }

                            if (pct >= lastLogPct + 10 || pct >= 100)
                            {
                                lastLogPct = (int)(pct / 10) * 10;
                                loggerCallback?.Invoke(Helpers.SanitizeLogMessage($"[SISTEMA] Descargando: {pct:F1}% de {size} a {speed} (Restan {eta})"));
                            }
                        }
                    }
                }
                else
                {
                    var lowerLine = cleanLine.ToLower();
                    if (NonRetryablePatterns.Any(p => lowerLine.Contains(p)))
                    {
                        nonRetryableFound = true;
                        lastErrorLine = cleanLine;
                    }

                    if (cleanLine.StartsWith("[Merger]", StringComparison.OrdinalIgnoreCase))
                    {
                        var mMerge = MergingRegex.Match(cleanLine);
                        if (mMerge.Success) finalFilepath = mMerge.Groups[1].Value.Trim();
                    }
                    else if (cleanLine.StartsWith("[EmbedThumbnail]", StringComparison.OrdinalIgnoreCase))
                    {
                        var mThumb = AddingThumbnailRegex.Match(cleanLine);
                        if (mThumb.Success) finalFilepath = mThumb.Groups[1].Value.Trim();
                    }
                    else if (cleanLine.StartsWith("[ExtractAudio]", StringComparison.OrdinalIgnoreCase))
                    {
                        var mDest = DestRegex.Match(cleanLine);
                        if (mDest.Success) finalFilepath = mDest.Groups[1].Value.Trim();
                    }

                    loggerCallback?.Invoke(Helpers.SanitizeLogMessage($"[yt-dlp] {cleanLine}"));
                }
            }
        }

        var runResult = ProcessRunner.RunAsync(
            exe,
            args,
            timeoutMs: DownloadTimeoutMs,
            cancellationToken: cancelEvent ?? CancellationToken.None,
            outputLineCallback: ProcessLine,
            errorLineCallback: ProcessLine).GetAwaiter().GetResult();

        if (runResult.Canceled)
        {
            throw new DownloadCancelled("Descarga cancelada por el usuario.");
        }

        if (runResult.TimedOut)
        {
            loggerCallback?.Invoke("[AVISO] La descarga superó el tiempo máximo permitido y fue detenida.");
        }

        if (progressCallback != null && !runResult.TimedOut)
        {
            progressCallback(new Dictionary<string, string> { { "status", "finished" } });
        }

        var retCode = runResult.ExitCode;

        if (retCode == 0)
        {
            var targetCheck = string.IsNullOrEmpty(finalFilepath) ? outputTemplate : finalFilepath;
            if (File.Exists(targetCheck))
            {
                if (new FileInfo(targetCheck).Length == 0)
                    retCode = 1;
                else
                    finalFilepath = targetCheck;
            }
        }

        if (retCode != 0 && nonRetryableFound)
            throw new NonRetryableDownloadError(lastErrorLine ?? "Error no reintentable en yt-dlp");

        if (retCode == 0 && !string.IsNullOrEmpty(finalFilepath) && File.Exists(finalFilepath) && new FileInfo(finalFilepath).Length > 0)
        {
            if (trimSilence)
            {
                loggerCallback?.Invoke($"[SISTEMA] Recortando silencios de: {Path.GetFileName(finalFilepath)}");
                var trimmed = TrimSilenceFfmpeg(finalFilepath, ffmpegLocation, cancelEvent, bitrate);
                if (trimmed && loggerCallback != null)
                    loggerCallback?.Invoke("[SISTEMA] Silencios recortados correctamente.");
            }

            if (embedLyrics && lyricsApiFallback)
            {
                var lrcPath = Path.ChangeExtension(finalFilepath, ".lrc");
                if (!File.Exists(lrcPath))
                {
                    loggerCallback?.Invoke($"[SISTEMA] Buscando letras de respaldo en LRCLIB para: {Path.GetFileName(finalFilepath)}");
                    var (titleEx, artistEx) = MetadataHelpers.ExtractTitleArtist(finalFilepath);
                    MetadataService.TryWriteLyrics(finalFilepath, titleEx, artistEx);
                    if (loggerCallback != null)
                        loggerCallback?.Invoke("[SISTEMA] Letras de respaldo incrustadas en archivo .lrc.");
                }
            }
        }

        return retCode;
    }

    public static int SearchAndDownload(string query, string outputTemplate, string formatType = "mp3", string bitrate = "320k",
        bool sponsorblock = false, string? cookiesBrowser = null, string? playlistItems = null,
        string? rawArgs = null, Action<string>? loggerCallback = null,
        Action<Dictionary<string, string>>? progressCallback = null,
        string? ffmpegLocation = null, string? proxy = null, bool normalize = false,
        bool embedLyrics = false, CancellationToken? cancelEvent = null,
        bool trimSilence = false, bool lyricsApiFallback = true, string? cookiesPath = null,
        string searchSource = "YouTube Music")
    {
        CheckCancelled(cancelEvent);

        loggerCallback?.Invoke(Helpers.SanitizeLogMessage($"[SISTEMA] Buscando en {searchSource} para: {query}"));

        var searchLimit = query.Contains(" - Topic") ? 1 : 5;
        var metadata = MetadataService.GetMetadata(query, isSearch: true, limit: searchLimit,
            cookiesBrowser: cookiesBrowser, proxy: proxy,
            loggerCallback: loggerCallback, cookiesPath: cookiesPath, searchSource: searchSource);

        if (metadata == null || metadata.Entries.Count == 0)
        {
            loggerCallback?.Invoke("[ERROR] No se encontraron resultados.");
            throw new NoResultsFound("No se encontraron resultados para la búsqueda.");
        }

        var scoredEntries = metadata.Entries.Select(e => (MetadataHelpers.Score(e), e)).ToList();
        if (searchSource == "YouTube Music")
        {
            scoredEntries.Sort((a, b) => b.Item1.CompareTo(a.Item1));
        }
        var bestEntry = scoredEntries[0].Item2;

        loggerCallback?.Invoke(Helpers.SanitizeLogMessage($"[SISTEMA] Seleccionado: {bestEntry.Title}"));

        return DownloadUrl(bestEntry.Url ?? "", outputTemplate, formatType, bitrate,
            sponsorblock, cookiesBrowser, playlistItems, rawArgs,
            loggerCallback, progressCallback, ffmpegLocation, proxy,
            normalize, embedLyrics, cancelEvent, trimSilence, lyricsApiFallback,
            cookiesPath: cookiesPath);
    }

    public static bool DownloadWithRetry(DownloadOperation downloadFunc,
        CancellationToken? cancelEvent = null,
        Action<string>? loggerCallback = null,
        Action<Dictionary<string, string>>? progressCallback = null)
    {
        for (var attempt = 1; attempt <= Constants.MaxRetries; attempt++)
        {
            try
            {
                CheckCancelled(cancelEvent);
                var result = downloadFunc(cancelEvent, loggerCallback, progressCallback);
                if (result != 0)
                    throw new RuntimeError($"yt-dlp.exe retornó código {result}");
                return true;
            }
            catch (DownloadCancelled) { throw; }
            catch (NonRetryableDownloadError e)
            {
                loggerCallback?.Invoke($"[ERROR] Error crítico no reintentable: {e.Message}");
                return false;
            }
            catch (FileNotFoundException e)
            {
                loggerCallback?.Invoke($"[ERROR] Error crítico no reintentable: {e.Message}");
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                loggerCallback?.Invoke($"[ERROR] Error crítico no reintentable: {e.Message}");
                return false;
            }
            catch (NoResultsFound e)
            {
                loggerCallback?.Invoke($"[ERROR] {e.Message}");
                return false;
            }
            catch (Exception e)
            {
                var errLower = e.Message.ToLower();
                if (NonRetryablePatterns.Any(p => errLower.Contains(p)))
                {
                    loggerCallback?.Invoke($"[ERROR] Error crítico no reintentable: {e.Message}");
                    return false;
                }

                if (errLower.Contains("cookies") || errLower.Contains("dpapi"))
                {
                    loggerCallback?.Invoke($"[ERROR] Error al cargar cookies: {e.Message}");
                }

                if (attempt < Constants.MaxRetries)
                {
                    var waitTime = (int)Math.Pow(Constants.RetryBackoffBase, attempt);
                    loggerCallback?.Invoke($"[AVISO] Intento {attempt}/{Constants.MaxRetries} fallido: {e.Message}");
                    loggerCallback?.Invoke($"[SISTEMA] Reintentando en {waitTime}s...");
                    for (var i = 0; i < waitTime; i++)
                    {
                        CheckCancelled(cancelEvent);
                        Thread.Sleep(1000);
                    }
                }
                else
                {
                    loggerCallback?.Invoke($"[ERROR] Fallo tras {Constants.MaxRetries} intentos: {e.Message}");
                }
            }
        }

        return false;
    }

    public static Models.DownloadResult DownloadItem(
        string queryOrUrl,
        string formatType = "mp3",
        string bitrate = "320k",
        string? outFolder = null,
        bool isSearch = false,
        bool sponsorblock = false,
        string? cookiesBrowser = null,
        string? playlistItems = null,
        string? rawArgs = null,
        Action<string>? loggerCallback = null,
        Action<Dictionary<string, string>>? progressCallback = null,
        string? ffmpegLocation = null,
        string? proxy = null,
        bool normalize = false,
        bool embedLyrics = false,
        CancellationToken? cancelEvent = null,
        bool trimSilence = false,
        bool lyricsApiFallback = true,
        string? cookiesPath = null,
        bool isEnumerate = false,
        int itemIndex = 0,
        string searchSource = "YouTube Music")
    {
        var targetFolder = outFolder ?? AppConfig.GetDefaultDownloadsFolder();
        Directory.CreateDirectory(targetFolder);

        string filenamePattern;
        if (isEnumerate)
        {
            filenamePattern = itemIndex > 0 ? $"{itemIndex:D2} - %(title)s.%(ext)s" : "%(autonumber)02d - %(title)s.%(ext)s";
        }
        else
        {
            filenamePattern = "%(title)s.%(ext)s";
        }

        var outputTemplate = Path.Combine(targetFolder, filenamePattern);

        try
        {
            int exitCode;
            if (isSearch || !Helpers.IsUrl(queryOrUrl))
            {
                exitCode = SearchAndDownload(queryOrUrl, outputTemplate, formatType, bitrate,
                    sponsorblock, cookiesBrowser, playlistItems, rawArgs, loggerCallback,
                    progressCallback, ffmpegLocation, proxy, normalize, embedLyrics, cancelEvent,
                    trimSilence, lyricsApiFallback, cookiesPath, searchSource: searchSource);
            }
            else
            {
                exitCode = DownloadUrl(queryOrUrl, outputTemplate, formatType, bitrate,
                    sponsorblock, cookiesBrowser, playlistItems, rawArgs, loggerCallback,
                    progressCallback, ffmpegLocation, proxy, normalize, embedLyrics, cancelEvent,
                    trimSilence, lyricsApiFallback, cookiesPath);
            }

            return new Models.DownloadResult
            {
                Success = exitCode == 0,
                TargetFolder = targetFolder,
                FilePath = targetFolder,
                ErrorMessage = exitCode == 0 ? null : $"Código de salida: {exitCode}"
            };
        }
        catch (Exception ex)
        {
            return new Models.DownloadResult
            {
                Success = false,
                TargetFolder = targetFolder,
                ErrorMessage = ex.Message
            };
        }
    }

    public static string? ExtractStreamUrl(string queryOrUrl, string? cookiesBrowser = null)
    {
        var exe = BinaryManager.GetYtDlpPath();
        if (string.IsNullOrEmpty(exe))
        {
            return null;
        }

        var args = new List<string>
        {
            "--ignore-config",
            "-g",
            "-f",
            "ba/b",
            "--no-playlist"
        };

        if (!string.IsNullOrEmpty(cookiesBrowser) && cookiesBrowser.ToLower() != "ninguno")
        {
            args.Add("--cookies-from-browser");
            args.Add(cookiesBrowser.ToLower());
        }

        args.Add("--");
        var isUrl = Helpers.IsUrl(queryOrUrl);
        args.Add(isUrl ? queryOrUrl : $"ytsearch1:{queryOrUrl}");

        try
        {
            var result = ProcessRunner.RunAsync(
                exe,
                args,
                timeoutMs: 15000,
                maxCapturedChars: 1024 * 1024).GetAwaiter().GetResult();

            if (result.Success && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return result.StandardOutput
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
            }
        }
        catch { }

        return null;
    }
}

public class DownloadCancelled : Exception
{
    public DownloadCancelled(string message) : base(message) { }
}

public class NoResultsFound : Exception
{
    public NoResultsFound(string message) : base(message) { }
}

public class NonRetryableDownloadError : Exception
{
    public NonRetryableDownloadError(string message) : base(message) { }
}

public class RuntimeError : Exception
{
    public RuntimeError(string message) : base(message) { }
}