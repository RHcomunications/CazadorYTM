namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public static class YtDlpArgumentBuilder
{
    public static List<string> Build(string formatType = "mp3", string bitrate = "320k", bool sponsorblock = false,
        string? cookiesBrowser = null, string? playlistItems = null, string? rawArgs = null,
        string? ffmpegLocation = null, string? proxy = null, bool normalize = false,
        bool embedLyrics = false, string? cookiesPath = null, string? playerClient = null)
    {
        var args = new List<string>
        {
            "--ignore-config",
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

        var clientExtractor = !string.IsNullOrEmpty(playerClient)
            ? $"youtube:player_client={playerClient}"
            : "youtube:player_client=android,web;po_token=web+";
        args.AddRange(new[] { "--extractor-args", clientExtractor });

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
            args.AddRange(SplitRawArgs(rawArgs));
        }

        return args;
    }

    private static readonly HashSet<string> BlockedRawOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--", "--config-location", "--exec", "--exec-before-download",
        "--batch-file", "--load-info-json", "--cookies", "--cookies-from-browser",
        "--output", "-o", "--paths", "-P", "--print-to-file",
        "--username", "-u", "--password", "-p", "--video-password", "--netrc",
        "--client-certificate", "--ca-certificate", "--proxy",
        "--downloader", "--external-downloader", "--downloader-args",
        "--postprocessor-args", "--update", "--update-to", "--plugin-dirs"
    };

    private static List<string> SplitRawArgs(string rawArgs)
    {
        if (rawArgs.Length > 4096)
        {
            throw new ArgumentException("Los argumentos adicionales superan el límite permitido.", nameof(rawArgs));
        }

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

        foreach (var arg in result)
        {
            var option = arg.Split('=', 2)[0];
            if (BlockedRawOptions.Contains(option))
            {
                throw new ArgumentException($"El argumento adicional '{option}' no está permitido.", nameof(rawArgs));
            }

            if (arg.Length > 1 && arg[0] == '-' && arg[1] != '-' &&
                "oPup".Contains(char.ToLowerInvariant(arg[1])))
            {
                throw new ArgumentException($"El argumento adicional '{arg[..2]}' no está permitido.", nameof(rawArgs));
            }
        }

        return result;
    }
}
