namespace CazadorYTM.Core.Services;

using System;
using System.IO;
using System.Text.RegularExpressions;
using CazadorYTM.Core.Models;

public static class MetadataHelpers
{
    public static int Score(DownloadEntry entry)
    {
        var title = entry.Title ?? "";
        var uploader = entry.Uploader ?? "";
        var score = 0;

        if (uploader.Contains(" - Topic", StringComparison.OrdinalIgnoreCase)) score += 50;
        if (title.Contains(" - Topic", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (title.Contains("(Audio)", StringComparison.OrdinalIgnoreCase) || title.Contains("[Audio]", StringComparison.OrdinalIgnoreCase) || title.EndsWith(" - Audio", StringComparison.OrdinalIgnoreCase)) score += 35;
        if (title.Contains("Official Audio", StringComparison.OrdinalIgnoreCase) || title.Contains("Audio Oficial", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (uploader.Contains("VEVO", StringComparison.OrdinalIgnoreCase) || uploader.Contains("Official", StringComparison.OrdinalIgnoreCase)) score += 20;

        if (title.Contains("Official Music Video", StringComparison.OrdinalIgnoreCase) || title.Contains("Video Oficial", StringComparison.OrdinalIgnoreCase) || title.Contains("Video Clip", StringComparison.OrdinalIgnoreCase)) score -= 10;
        if (title.Contains("Live", StringComparison.OrdinalIgnoreCase) || title.Contains("En Vivo", StringComparison.OrdinalIgnoreCase) || title.Contains("Concert", StringComparison.OrdinalIgnoreCase)) score -= 20;
        if (title.Contains("Cover", StringComparison.OrdinalIgnoreCase) || title.Contains("Tribute", StringComparison.OrdinalIgnoreCase)) score -= 30;
        if (title.Contains("Remix", StringComparison.OrdinalIgnoreCase) || title.Contains("Mix", StringComparison.OrdinalIgnoreCase) || title.Contains("Mashup", StringComparison.OrdinalIgnoreCase)) score -= 15;
        if (title.Contains("Karaoke", StringComparison.OrdinalIgnoreCase) || title.Contains("Instrumental", StringComparison.OrdinalIgnoreCase)) score -= 25;
        if (title.Contains("Teaser", StringComparison.OrdinalIgnoreCase) || title.Contains("Trailer", StringComparison.OrdinalIgnoreCase) || title.Contains("Preview", StringComparison.OrdinalIgnoreCase) || title.Contains("Behind The Scenes", StringComparison.OrdinalIgnoreCase)) score -= 50;

        return score;
    }

    public static (string title, string artist) ExtractTitleArtist(string filename)
    {
        var stem = Path.GetFileNameWithoutExtension(filename);
        stem = Regex.Replace(stem, @"^\d+[\s\.\-_]+", "");

        foreach (var pattern in Constants.TitleCleaningPatterns)
        {
            stem = Regex.Replace(stem, pattern, "", RegexOptions.IgnoreCase);
        }

        var parts = stem.Split(new[] { " - ", " _ ", " – " }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (parts[1].Trim(), parts[0].Trim());
        }

        return (stem.Trim(), "");
    }

    public static string CleanTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var cleaned = title;
        foreach (var pattern in Constants.TitleCleaningPatterns)
        {
            cleaned = Regex.Replace(cleaned, pattern, "", RegexOptions.IgnoreCase);
        }
        return cleaned.Trim();
    }
}
