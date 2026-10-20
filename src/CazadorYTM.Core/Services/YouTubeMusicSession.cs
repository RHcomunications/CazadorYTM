namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public sealed class YouTubeMusicSession
{
    private const string DefaultOrigin = "https://music.youtube.com";

    public string CookieHeader { get; }
    public string Sapisid { get; }
    public int AccountIndex { get; }

    private YouTubeMusicSession(string cookieHeader, string sapisid, int accountIndex)
    {
        CookieHeader = cookieHeader;
        Sapisid = sapisid;
        AccountIndex = accountIndex;
    }

    public static YouTubeMusicSession? TryCreate(string? cookiesPath, int accountIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(cookiesPath) || !File.Exists(cookiesPath))
        {
            return null;
        }

        if (new FileInfo(cookiesPath).Length > 20 * 1024 * 1024)
        {
            return null;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(cookiesPath);
        }
        catch
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var line = rawLine.TrimStart('\uFEFF');
            if (line.StartsWith("#HttpOnly_", StringComparison.Ordinal))
            {
                line = line.Substring("#HttpOnly_".Length);
            }
            else if (line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 7)
            {
                continue;
            }

            var domain = parts[0].TrimStart('.').ToLowerInvariant();
            var belongsToAccount = domain == "youtube.com" || domain.EndsWith(".youtube.com", StringComparison.Ordinal) ||
                                   domain == "google.com" || domain.EndsWith(".google.com", StringComparison.Ordinal);
            if (!belongsToAccount)
            {
                continue;
            }

            var name = parts[5].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            values[name] = parts[6];
        }

        if (!values.TryGetValue("SAPISID", out var sapisid) || string.IsNullOrWhiteSpace(sapisid))
        {
            return null;
        }

        var cookieHeader = string.Join("; ", values.Select(pair => $"{pair.Key}={pair.Value}"));
        return new YouTubeMusicSession(cookieHeader, sapisid, Math.Max(0, accountIndex));
    }

    public string CreateAuthorizationHeader(DateTimeOffset now, string origin = DefaultOrigin)
    {
        var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var payload = $"{timestamp} {Sapisid} {origin}";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return $"SAPISIDHASH {timestamp}_{hash}";
    }
}
