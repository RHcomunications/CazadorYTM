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

    public async Task<string?> TryTranscodeToTempWavAsync(string sourcePath, System.Threading.CancellationToken cancellationToken = default)
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

            var args = new[]
            {
                "-y", "-v", "error", "-i", sourcePath,
                "-vn", "-c:a", "pcm_s16le", tempWav
            };

            var runResult = await ProcessRunner.RunAsync(ffmpegExe, args, timeoutMs: 15000, cancellationToken: cancellationToken);

            if (runResult.Success && File.Exists(tempWav) && new FileInfo(tempWav).Length > 0)
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

            try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"Fallo al decodificar audio con FFmpeg: {ex.Message}");
        }
        return null;
    }

    public string? TryTranscodeToTempWav(string sourcePath)
    {
        return TryTranscodeToTempWavAsync(sourcePath).GetAwaiter().GetResult();
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
    }

    public static void PurgeStaleTempFiles()
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "CazadorYTM_Player");
            if (!Directory.Exists(tempDir)) return;

            foreach (var wav in Directory.EnumerateFiles(tempDir, "cache_*.wav"))
            {
                try { File.Delete(wav); } catch { }
            }
        }
        catch { }
    }
}
