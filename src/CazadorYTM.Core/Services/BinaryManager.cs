namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

public static class BinaryManager
{
    private static readonly ReaderWriterLockSlim _cacheLock = new();

    private static Lazy<string?> _ffmpegPathLazy = null!;
    private static Lazy<string?> _ffprobePathLazy = null!;
    private static Lazy<string?> _denoPathLazy = null!;
    private static Lazy<string?> _ytDlpPathLazy = null!;
    private static Lazy<string?> _cookiesPathLazy = null!;

    private static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

    static BinaryManager()
    {
        ResetLazyCaches();
    }

    private static void ResetLazyCaches()
    {
        _ffmpegPathLazy = new Lazy<string?>(() => FindFfmpegPathImpl(null));
        _ffprobePathLazy = new Lazy<string?>(() => FindFfprobePathImpl(null));
        _denoPathLazy = new Lazy<string?>(() => GetDenoPathImpl(null));
        _ytDlpPathLazy = new Lazy<string?>(() => GetYtDlpPathImpl("."));
        _cookiesPathLazy = new Lazy<string?>(() => ResolveCookiesPathImpl(null));
    }

    public static void ClearCache()
    {
        _cacheLock.EnterWriteLock();
        try { ResetLazyCaches(); }
        finally { _cacheLock.ExitWriteLock(); }
    }

    public static string? FindFfmpegPath(string? extraDir = null)
    {
        if (!string.IsNullOrEmpty(extraDir))
            return FindFfmpegPathImpl(extraDir);

        _cacheLock.EnterReadLock();
        try { return _ffmpegPathLazy.Value; }
        finally { _cacheLock.ExitReadLock(); }
    }

    private static string? FindFfmpegPathImpl(string? extraDir)
    {
        var searchDirs = new List<string>();
        var baseDir = Helpers.GetBaseDir();
        searchDirs.Add(baseDir);
        var internalPath = Path.Combine(baseDir, "internal");
        if (Directory.Exists(internalPath))
            searchDirs.Add(internalPath);

        if (!string.IsNullOrEmpty(extraDir))
            searchDirs.Insert(0, extraDir);

        foreach (var d in searchDirs)
        {
            var ffmpegPath = Path.Combine(d, IsWindows ? "ffmpeg.exe" : "ffmpeg");
            var ffprobePath = Path.Combine(d, IsWindows ? "ffprobe.exe" : "ffprobe");
            if (File.Exists(ffmpegPath) && File.Exists(ffprobePath))
                return d;
        }

        var ffmpegSys = FindInPath("ffmpeg");
        var ffprobeSys = FindInPath("ffprobe");
        if (ffmpegSys != null && ffprobeSys != null)
            return Path.GetDirectoryName(ffmpegSys);

        return null;
    }

    public static string? FindFfprobePath(string? extraDir = null)
    {
        if (!string.IsNullOrEmpty(extraDir))
            return FindFfprobePathImpl(extraDir);

        _cacheLock.EnterReadLock();
        try { return _ffprobePathLazy.Value; }
        finally { _cacheLock.ExitReadLock(); }
    }

    private static string? FindFfprobePathImpl(string? extraDir)
    {
        var dirPath = FindFfmpegPath(extraDir);
        if (dirPath != null)
        {
            var exe = Path.Combine(dirPath, IsWindows ? "ffprobe.exe" : "ffprobe");
            if (File.Exists(exe))
                return exe;
        }

        var sysPath = FindInPath("ffprobe");
        return sysPath;
    }

    public static string? GetFfplayPath(string? extraDir = null)
    {
        var dirPath = FindFfmpegPath(extraDir);
        if (dirPath != null)
        {
            var exe = Path.Combine(dirPath, IsWindows ? "ffplay.exe" : "ffplay");
            if (File.Exists(exe))
                return exe;
        }

        return FindInPath("ffplay");
    }

    public static bool CheckFfmpeg() => FindFfmpegPath() != null;

    public static string? GetDenoPath(string? baseDir = null)
    {
        _cacheLock.EnterReadLock();
        try { return _denoPathLazy.Value; }
        finally { _cacheLock.ExitReadLock(); }
    }

    private static string? GetDenoPathImpl(string? baseDir)
    {
        var resolvedBase = string.IsNullOrEmpty(baseDir) ? Helpers.GetBaseDir() : baseDir;
        var searchDirs = new List<string> { resolvedBase };

        if (IsWindows)
        {
            searchDirs.Add(Path.Combine(resolvedBase, "internal"));
            if (Path.GetFileName(resolvedBase) == "internal")
                searchDirs.Add(Path.GetDirectoryName(resolvedBase) ?? string.Empty);
        }
        else
        {
            searchDirs.Add(Path.Combine(resolvedBase, "internal"));
        }

        foreach (var d in searchDirs)
        {
            var p = Path.Combine(d, IsWindows ? "deno.exe" : "deno");
            if (File.Exists(p))
                return p;
        }

        return FindInPath("deno");
    }

    public static string? GetYtDlpPath(string? baseDir = null)
    {
        _cacheLock.EnterReadLock();
        try { return _ytDlpPathLazy.Value; }
        finally { _cacheLock.ExitReadLock(); }
    }

    private static string? GetYtDlpPathImpl(string? baseDir)
    {
        var resolvedBase = string.IsNullOrEmpty(baseDir) || baseDir == "." ? Helpers.GetBaseDir() : baseDir;
        var searchDirs = new List<string> { resolvedBase };

        if (IsWindows)
        {
            searchDirs.Add(Path.Combine(resolvedBase, "internal"));
            if (Path.GetFileName(resolvedBase) == "internal")
                searchDirs.Add(Path.GetDirectoryName(resolvedBase) ?? string.Empty);
        }
        else
        {
            searchDirs.Add(Path.Combine(resolvedBase, "internal"));
        }

        foreach (var d in searchDirs)
        {
            var p = Path.Combine(d, IsWindows ? "yt-dlp.exe" : "yt-dlp");
            if (File.Exists(p))
                return p;
        }

        var sysPath = FindInPath("yt-dlp");
        if (sysPath != null)
            return sysPath;

        throw new FileNotFoundException("¡Ejecutable 'yt-dlp.exe' no encontrado! Haz clic en Actualizar Motor.");
    }

    public static string? ResolveCookiesPath(string? providedPath = null)
    {
        _cacheLock.EnterReadLock();
        try { return _cookiesPathLazy.Value; }
        finally { _cacheLock.ExitReadLock(); }
    }

    private static string? ResolveCookiesPathImpl(string? providedPath)
    {
        if (!string.IsNullOrEmpty(providedPath) && File.Exists(providedPath))
            return providedPath;

        try
        {
            var appDataCookies = Helpers.GetDefaultCookiesPath();
            if (File.Exists(appDataCookies))
                return appDataCookies;
        }
        catch { }

        try
        {
            var localCookies = Path.Combine(Helpers.GetBaseDir(), "cookies.txt");
            if (File.Exists(localCookies))
                return localCookies;
        }
        catch { }

        return null;
    }

    private static string? FindInPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var fullPath = Path.Combine(dir, fileName);
                if (File.Exists(fullPath))
                    return fullPath;
            }
            catch { }
        }

        return null;
    }
}