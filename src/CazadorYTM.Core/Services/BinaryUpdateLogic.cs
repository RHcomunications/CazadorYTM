namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.Text.Json;

public class GithubAssetInfo
{
    public string DownloadUrl { get; set; } = string.Empty;
    public string? Sha256 { get; set; }
}

public static class BinaryUpdateLogic
{
    public static GithubAssetInfo? ParseGithubAsset(string json, string assetName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                if (!string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var downloadUrl = asset.TryGetProperty("browser_download_url", out var urlProp)
                    ? urlProp.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(downloadUrl))
                    return null;

                string? sha256 = null;
                if (asset.TryGetProperty("digest", out var digestProp))
                {
                    DownloadIntegrity.TryNormalizeSha256(digestProp.GetString(), out var normalized);
                    sha256 = string.IsNullOrEmpty(normalized) ? null : normalized;
                }

                return new GithubAssetInfo
                {
                    DownloadUrl = downloadUrl,
                    Sha256 = sha256
                };
            }
        }
        catch { }

        return null;
    }

    public static string? ParseYtdlpGithubTag(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            if (!string.IsNullOrWhiteSpace(tag))
                return tag.Trim().TrimStart('v');
        }
        catch { }
        return null;
    }

    public static string? ParseYtdlpPyPiVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("info").GetProperty("version").GetString();
        }
        catch { return null; }
    }

    public static string? ParseGithubTag(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            return tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;
        }
        catch { return null; }
    }

    public static string? ParseFfmpegBuildDate(string json)
    {
        try
        {
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

    public static List<string> BuildUpdatesNeeded(
        bool ytdlpInstalled, string? localYt, string? remoteYt,
        bool denoInstalled, string? localDeno, string? remoteDeno,
        bool ffmpegInstalled, string? localFf, string? remoteFf)
    {
        var updatesNeeded = new List<string>();

        if (!ytdlpInstalled || string.IsNullOrEmpty(localYt))
        {
            updatesNeeded.Add("yt-dlp (Faltante)");
        }
        else if (!string.IsNullOrEmpty(remoteYt) && remoteYt != localYt)
        {
            if (BinaryUpdateHelpers.CompareVersionStrings(remoteYt, localYt) > 0)
            {
                updatesNeeded.Add($"yt-dlp (Instalado: {localYt} -> Disponible: {remoteYt})");
            }
        }

        if (!denoInstalled || string.IsNullOrEmpty(localDeno))
        {
            updatesNeeded.Add("Deno (Faltante)");
        }
        else if (!string.IsNullOrEmpty(remoteDeno) && remoteDeno != localDeno)
        {
            if (BinaryUpdateHelpers.CompareVersionStrings(remoteDeno, localDeno) > 0)
            {
                updatesNeeded.Add($"Deno (Instalado: {localDeno} -> Disponible: {remoteDeno})");
            }
        }

        if (!ffmpegInstalled)
        {
            updatesNeeded.Add("FFmpeg (Faltante)");
        }
        else if (!string.IsNullOrEmpty(remoteFf) && !string.IsNullOrEmpty(localFf) && localFf != "Instalado")
        {
            if (BinaryUpdateHelpers.CompareVersionStrings(remoteFf, localFf) > 0)
            {
                updatesNeeded.Add($"FFmpeg (Instalado: {localFf} -> Disponible: {remoteFf})");
            }
        }

        return updatesNeeded;
    }
}
