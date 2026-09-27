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

    private static readonly string[] NonRetryablePatterns =
    {
        "http error 404", "404 not found", "video unavailable",
        "private video", "this video is private", "confirm your age"
    };

    public static List<string> GetCliArgs(string formatType = "mp3", string bitrate = "320k", bool sponsorblock = false,
        string? cookiesBrowser = null, string? playlistItems = null, string? rawArgs = null,
        string? ffmpegLocation = null, string? proxy = null, bool normalize = false,
        bool embedLyrics = false, string? cookiesPath = null)
    {
        var args = new List<string>
        {
            "--concurrent-fragments", "5",
            "--embed-thumbnail",
            "--embed-metadata",
            "--no-mtime",
            "--no-warnings",
            "--convert-thumbnails", "jpg",
            "--newline"
        };

        foreach (var pattern in Constants.TitleCleaningPatterns)
        {
            args.AddRange(new[] { "--parse-metadata", $"title:{pattern}:" });
        }

        if (embedLyrics)
        {
            args.AddRange(new[] { "--embed-subs", "--write-subs", "--sub-langs", "all,-live_chat" });
        }

        if (!string.IsNullOrEmpty(ffmpegLocation))
        {
            args.AddRange(new[] { "--ffmpeg-location", ffmpegLocation });
        }

        if (sponsorblock)
        {
            args.AddRange(new[] { "--sponsorblock-remove", "sponsor,intro,outro,selfpromo,preview,filler,interaction,music_offtopic" });
        }

        if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
        {
            args.AddRange(new[] { "--cookies", cookiesPath });
        }
        else if (!string.IsNullOrEmpty(cookiesBrowser) && cookiesBrowser.ToLower() != "ninguno")
        {
            args.AddRange(new[] { "--cookies-from-browser", cookiesBrowser.ToLower() });
        }

        args.AddRange(new[] { "--extractor-args", "youtube:player_client=android,web;po_token=web+" });

        var baseDir = Helpers.GetBaseDir();
        var denoPath = BinaryManager.GetDenoPath(baseDir);
        if (!string.IsNullOrEmpty(denoPath))
        {
            args.AddRange(new[] { "--js-runtimes", $"deno:{denoPath}" });
        }

        if (!string.IsNullOrEmpty(proxy))
        {
            args.AddRange(new[] { "--proxy", proxy });
        }

        if (!string.IsNullOrEmpty(playlistItems))
        {
            args.AddRange(new[] { "-I", playlistItems });
        }

        if (new[] { "mp3", "flac", "m4a", "wav", "opus" }.Contains(formatType, StringComparer.OrdinalIgnoreCase))
        {
            args.AddRange(new[] { "-f", "bestaudio/best/ba/b/*" });
            args.AddRange(new[] { "-x", "--audio-format", formatType });
            if (Constants.LossyCodecs.Contains(formatType))
            {
                args.AddRange(new[] { "--audio-quality", bitrate });
            }
            if (normalize)
            {
                args.AddRange(new[] { "--postprocessor-args", "ExtractAudio:-af loudnorm=I=-14:TP=-1.0:LRA=11" });
            }
        }
        else if (formatType == "mp4")
        {
            args.AddRange(new[] { "-f", "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/bestvideo+bestaudio/best/bestaudio/b" });
            args.AddRange(new[] { "--merge-output-format", "mp4" });
            args.AddRange(new[] { "--recode-video", "mp4" });
        }

        if (!string.IsNullOrEmpty(rawArgs))
        {
            var rawArgList = SplitRawArgs(rawArgs);
            args.AddRange(rawArgList);
        }

        return args;
    }

    private static List<string> SplitRawArgs(string rawArgs)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in rawArgs)
        {
            if (ch == '"') { inQuotes = !inQuotes; }
            else if (ch == ' ' && !inQuotes)
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
            }
            else { current.Append(ch); }
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

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
                var files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories);
                foreach (var file in files)
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
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            try
            {
                var psi = new ProcessStartInfo("taskkill", $"/F /T /PID {pid}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi)?.WaitForExit();
            }
            catch { }
        }
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

        var cmd = new List<string> { ffmpegExe, "-y", "-v", "error", "-i", filepath,
            "-af", "silenceremove=start_threshold=-50dB:start_duration=0.5:stop_threshold=-50dB:stop_duration=0.5:stop_periods=-1" };
        cmd.AddRange(qualityArgs);
        cmd.Add(tempFilepath);

        var fileSizeMb = new FileInfo(filepath).Length / (1024.0 * 1024.0);
        var dynamicTimeout = Math.Max(60, (int)(fileSizeMb * 15) + 30);

        try
        {
            CheckCancelled(cancelEvent);
            var psi = new ProcessStartInfo
            {
                FileName = cmd[0],
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in cmd.Skip(1))
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi)!;
            process.WaitForExit();
            CheckCancelled(cancelEvent);

            if (process.ExitCode == 0 && File.Exists(tempFilepath) && new FileInfo(tempFilepath).Length > 0)
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
        var args = GetCliArgs(formatType, bitrate, sponsorblock, cookiesBrowser,
            playlistItems, rawArgs, ffmpegLocation, proxy, normalize, embedLyrics,
            cookiesPath: cookiesPath);

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

        psi.ArgumentList.Add(args[0]);
        foreach (var arg in args.Skip(1))
            psi.ArgumentList.Add(arg);
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outputTemplate);
        psi.ArgumentList.Add("--no-playlist");
        psi.ArgumentList.Add(url);

        using var process = Process.Start(psi)!;

        var finalFilepath = string.Empty;
        var lastLogPct = -10;
        var nonRetryableFound = false;
        var lastErrorLine = string.Empty;

        try
        {
            double lastDispatchedPct = -1;
            var progressDict = new Dictionary<string, string>(4);

            void ProcessLine(string? line)
            {
                if (string.IsNullOrEmpty(line)) return;

                var cleanLine = AnsiEscapeRegex.Replace(line, "").Trim();
                if (string.IsNullOrEmpty(cleanLine)) return;

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

            process.OutputDataReceived += (s, e) => ProcessLine(e.Data);
            process.ErrorDataReceived += (s, e) => ProcessLine(e.Data);

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            while (!process.WaitForExit(150))
            {
                if (cancelEvent != null && cancelEvent.Value.IsCancellationRequested)
                {
                    KillProcessTree(process.Id);
                    throw new DownloadCancelled("Descarga cancelada por el usuario.");
                }
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                KillProcessTree(process.Id);
            }
            process.WaitForExit();

            if (progressCallback != null && (cancelEvent == null || !cancelEvent.Value.IsCancellationRequested))
            {
                progressCallback?.Invoke(new Dictionary<string, string> { { "status", "finished" } });
            }
        }

        var retCode = process.ExitCode;

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
                    var (titleEx, artistEx) = MetadataService.ExtractTitleArtistFromFilename(finalFilepath);
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

        var scoredEntries = metadata.Entries.Select(e => (MetadataService.ScoreResult(e), e)).ToList();
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

        psi.ArgumentList.Add("-g");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("ba/b");
        psi.ArgumentList.Add("--no-playlist");

        if (!string.IsNullOrEmpty(cookiesBrowser) && cookiesBrowser.ToLower() != "ninguno")
        {
            psi.ArgumentList.Add("--cookies-from-browser");
            psi.ArgumentList.Add(cookiesBrowser.ToLower());
        }

        var isUrl = Helpers.IsUrl(queryOrUrl);
        if (isUrl)
        {
            psi.ArgumentList.Add(queryOrUrl);
        }
        else
        {
            psi.ArgumentList.Add($"ytsearch1:{queryOrUrl}");
        }

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var output = proc.StandardOutput.ReadToEnd();
            var exited = proc.WaitForExit(15000);

            if (!exited)
            {
                KillProcessTree(proc.Id);
                return null;
            }

            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
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