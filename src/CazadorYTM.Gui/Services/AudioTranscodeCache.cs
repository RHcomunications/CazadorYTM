namespace CazadorYTM.Gui.Services;

using System;
using System.Diagnostics;
using System.IO;
using CazadorYTM.Core.Services;

public class AudioTranscodeCache
{
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, string> _lruTranscodeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lruOrder = new();
    private const int MaxLruCachedTracks = 5;

    private string? _currentTempWav;

    public string? TryTranscodeToTempWav(string sourcePath)
    {
        try
        {
            lock (_cacheLock)
            {
                if (_lruTranscodeCache.TryGetValue(sourcePath, out var cachedWav) && File.Exists(cachedWav))
                {
                    _lruOrder.Remove(sourcePath);
                    _lruOrder.AddFirst(sourcePath);
                    _currentTempWav = cachedWav;
                    return cachedWav;
                }
            }

            var ffmpegDir = BinaryManager.FindFfmpegPath();
            if (string.IsNullOrEmpty(ffmpegDir)) return null;

            var ffmpegExe = Path.Combine(ffmpegDir, Environment.OSVersion.Platform == PlatformID.Win32NT ? "ffmpeg.exe" : "ffmpeg");
            if (!File.Exists(ffmpegExe)) return null;

            var tempDir = Path.Combine(Path.GetTempPath(), "CazadorYTM_Player");
            Directory.CreateDirectory(tempDir);

            var tempWav = Path.Combine(tempDir, $"cache_{Guid.NewGuid():N}.wav");

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = $"-y -v error -i \"{sourcePath}\" -vn -c:a pcm_s16le \"{tempWav}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var exited = process.WaitForExit(15000);
            if (!exited)
            {
                try { process.Kill(); } catch { }
                return null;
            }

            if (process.ExitCode == 0 && File.Exists(tempWav) && new FileInfo(tempWav).Length > 0)
            {
                lock (_cacheLock)
                {
                    // Evict oldest if exceeding capacity
                    while (_lruOrder.Count >= MaxLruCachedTracks)
                    {
                        var oldest = _lruOrder.Last?.Value;
                        if (oldest != null)
                        {
                            _lruOrder.RemoveLast();
                            if (_lruTranscodeCache.TryGetValue(oldest, out var oldestWav))
                            {
                                _lruTranscodeCache.Remove(oldest);
                                if (oldestWav != _currentTempWav && File.Exists(oldestWav))
                                {
                                    try { File.Delete(oldestWav); } catch { }
                                }
                            }
                        }
                        else break;
                    }

                    _lruTranscodeCache[sourcePath] = tempWav;
                    _lruOrder.AddFirst(sourcePath);
                    _currentTempWav = tempWav;
                }

                return tempWav;
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"Fallo al decodificar audio con FFmpeg: {ex.Message}");
        }
        return null;
    }

    public void CleanupAllTempCache()
    {
        lock (_cacheLock)
        {
            foreach (var wav in _lruTranscodeCache.Values)
            {
                try
                {
                    if (File.Exists(wav)) File.Delete(wav);
                }
                catch { }
            }
            _lruTranscodeCache.Clear();
            _lruOrder.Clear();
            _currentTempWav = null;
        }

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "CazadorYTM_Player");
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
        catch { }
    }
}
