namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public static class YouTubeMusicResponseParser
{
    private static readonly Regex MetadataRunRegex = new(
        @"(?i)^\d[\d.,]*\s*(?:[KMB]|mil|millones)?\s*(?:de\s+)?(?:visualizaciones|reproducciones|vistas|views|plays)\b",
        RegexOptions.Compiled);

    private static readonly Regex RelativeDateRegex = new(
        @"(?i)^hace\s+\d",
        RegexOptions.Compiled);

    private static readonly Regex DurationRunRegex = new(
        @"^\d{1,2}:\d{2}(?::\d{2})?$",
        RegexOptions.Compiled);

    public static (List<RadarItem> Items, string? Continuation) Parse(JsonElement root, RadarSection section)
    {
        var items = new List<RadarItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        CollectItems(root, section, items, seen);
        return (items, FindContinuation(root));
    }

    private static void CollectItems(JsonElement node, RadarSection section, List<RadarItem> items, HashSet<string> seen)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                {
                    if (property.NameEquals("musicResponsiveListItemRenderer"))
                    {
                        if (TryParseTrack(property.Value, section, out var track) && seen.Add(track.Id))
                        {
                            items.Add(track);
                        }
                    }
                    else if (property.NameEquals("musicTwoRowItemRenderer"))
                    {
                        if (TryParsePlaylist(property.Value, section, out var playlist) && seen.Add(playlist.Id))
                        {
                            items.Add(playlist);
                        }
                    }
                    else
                    {
                        CollectItems(property.Value, section, items, seen);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var child in node.EnumerateArray())
                {
                    CollectItems(child, section, items, seen);
                }
                break;
        }
    }

    private static bool TryParseTrack(JsonElement renderer, RadarSection section, out RadarItem item)
    {
        item = null!;
        var videoId = GetString(renderer, "playlistItemData", "videoId");
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return false;
        }

        var title = JoinRuns(GetColumn(renderer, "flexColumns", 0));
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var artist = ArtistFromRuns(GetColumn(renderer, "flexColumns", 1));
        var duration = ParseDuration(JoinRuns(GetColumn(renderer, "fixedColumns", 0)));
        var thumbnail = LargestThumbnail(renderer, "thumbnail");

        item = new RadarItem
        {
            Id = videoId,
            Title = title,
            Artist = artist,
            Url = $"https://music.youtube.com/watch?v={videoId}",
            Duration = duration,
            ThumbnailUrl = thumbnail,
            IsPlaylist = false,
            Section = section
        };
        return true;
    }

    private static bool TryParsePlaylist(JsonElement renderer, RadarSection section, out RadarItem item)
    {
        item = null!;
        var browseId = GetString(renderer, "navigationEndpoint", "browseEndpoint", "browseId");
        if (string.IsNullOrWhiteSpace(browseId))
        {
            return false;
        }

        var title = JoinRuns(renderer, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var listId = browseId.StartsWith("VL", StringComparison.Ordinal) ? browseId.Substring(2) : browseId;
        item = new RadarItem
        {
            Id = browseId,
            Title = title,
            Artist = NullIfWhiteSpace(JoinRuns(renderer, "subtitle")),
            Url = $"https://music.youtube.com/playlist?list={listId}",
            ThumbnailUrl = LargestThumbnail(renderer, "thumbnailRenderer"),
            IsPlaylist = true,
            Section = section
        };
        return true;
    }

    private static string? FindContinuation(JsonElement node)
    {
        return FindContinuation(node, 0);
    }

    private static string? FindContinuation(JsonElement node, int depth)
    {
        if (depth > 30)
        {
            return null;
        }

        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                if (node.TryGetProperty("continuationCommand", out var command) &&
                    command.ValueKind == JsonValueKind.Object &&
                    !string.IsNullOrWhiteSpace(GetString(command, "token")))
                {
                    return GetString(command, "token");
                }

                if (node.TryGetProperty("nextContinuationData", out var next) &&
                    next.ValueKind == JsonValueKind.Object &&
                    !string.IsNullOrWhiteSpace(GetString(next, "continuation")))
                {
                    return GetString(next, "continuation");
                }

                foreach (var property in node.EnumerateObject())
                {
                    var found = FindContinuation(property.Value, depth + 1);
                    if (found != null)
                    {
                        return found;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var child in node.EnumerateArray())
                {
                    var found = FindContinuation(child, depth + 1);
                    if (found != null)
                    {
                        return found;
                    }
                }
                break;
        }

        return null;
    }

    private static JsonElement? GetColumn(JsonElement renderer, string columnsName, int index)
    {
        if (!renderer.TryGetProperty(columnsName, out var columns) || columns.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var entries = columns.EnumerateArray().ToList();
        if (index >= entries.Count)
        {
            return null;
        }

        var entry = entries[index];
        foreach (var property in entry.EnumerateObject())
        {
            if (property.Name.EndsWith("Renderer", StringComparison.Ordinal) &&
                property.Value.TryGetProperty("text", out var text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? ArtistFromRuns(JsonElement? text)
    {
        if (text == null || text.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!text.Value.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
        {
            return NullIfWhiteSpace(JoinRunsText(text.Value));
        }

        var builder = new StringBuilder();
        foreach (var run in runs.EnumerateArray())
        {
            var value = GetString(run, "text") ?? string.Empty;
            if (LooksLikeMetadata(value))
            {
                break;
            }

            builder.Append(value);
        }

        var joined = Regex.Replace(builder.ToString(), @"\s+", " ").Trim().TrimEnd(',', ' ', '·', '•', '/');
        return NullIfWhiteSpace(joined);
    }

    private static bool LooksLikeMetadata(string value)
    {
        var text = value.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        return MetadataRunRegex.IsMatch(text) || RelativeDateRegex.IsMatch(text) || DurationRunRegex.IsMatch(text);
    }

    private static int? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Trim().Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }

        var total = 0;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return null;
            }

            total = (total * 60) + number;
        }

        return total;
    }

    private static string? LargestThumbnail(JsonElement renderer, string thumbnailContainer)
    {
        if (!renderer.TryGetProperty(thumbnailContainer, out var container) ||
            !container.TryGetProperty("musicThumbnailRenderer", out var musicThumbnail) ||
            !musicThumbnail.TryGetProperty("thumbnail", out var thumbnail) ||
            !thumbnail.TryGetProperty("thumbnails", out var thumbnails) ||
            thumbnails.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? best = null;
        var bestWidth = -1;
        foreach (var candidate in thumbnails.EnumerateArray())
        {
            var url = GetString(candidate, "url");
            var width = candidate.TryGetProperty("width", out var widthValue) && widthValue.TryGetInt32(out var parsedWidth)
                ? parsedWidth
                : 0;
            if (!string.IsNullOrEmpty(url) && width >= bestWidth)
            {
                best = url;
                bestWidth = width;
            }
        }

        return best;
    }

    private static string JoinRuns(JsonElement renderer, string property)
    {
        return renderer.TryGetProperty(property, out var node) ? JoinRunsText(node) : string.Empty;
    }

    private static string JoinRuns(JsonElement? text)
    {
        return text != null && text.Value.ValueKind == JsonValueKind.Object ? JoinRunsText(text.Value) : string.Empty;
    }

    private static string JoinRunsText(JsonElement node)
    {
        if (node.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
        {
            return string.Concat(runs.EnumerateArray().Select(run => GetString(run, "text") ?? string.Empty));
        }

        return GetString(node, "simpleText") ?? string.Empty;
    }

    private static string? GetString(JsonElement node, params string[] path)
    {
        var current = node;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
