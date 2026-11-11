namespace CazadorYTM.Core.Models;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

public class LyricLine
{
    private static readonly Regex TimestampRegex = new(@"\[(\d{1,2}):(\d{2})(?:\.(\d{1,3}))?\]", RegexOptions.Compiled);

    public TimeSpan Timestamp { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsSynchronized { get; set; } = true;

    public LyricLine() { }

    public LyricLine(TimeSpan timestamp, string text, bool isSynchronized = true)
    {
        Timestamp = timestamp;
        Text = text;
        IsSynchronized = isSynchronized;
    }

    /// <summary>
    /// Parses LRC or plain text lyrics into an ordered list of LyricLine instances.
    /// Handles single timestamps, multiple timestamps per line, and plain text fallback.
    /// </summary>
    public static List<LyricLine> ParseLrc(string? rawLyrics)
    {
        var result = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(rawLyrics))
            return result;

        var lines = rawLyrics.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var hasAnyTimestamp = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Check if this line is LRC metadata (e.g., [ar:Artist], [ti:Title], [al:Album], [by:...], [offset:...])
            if (trimmed.StartsWith('[') && !char.IsDigit(trimmed.Length > 1 ? trimmed[1] : ' '))
            {
                // Metadata header tag, ignore for lyric presentation
                continue;
            }

            var matches = TimestampRegex.Matches(trimmed);
            if (matches.Count > 0)
            {
                hasAnyTimestamp = true;
                // Extract lyric text after all timestamps
                var text = TimestampRegex.Replace(trimmed, string.Empty).Trim();

                foreach (Match match in matches)
                {
                    if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
                        int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                    {
                        var millis = 0;
                        if (match.Groups[3].Success)
                        {
                            var fractionStr = match.Groups[3].Value;
                            if (fractionStr.Length == 1) fractionStr += "00";
                            else if (fractionStr.Length == 2) fractionStr += "0";
                            else if (fractionStr.Length > 3) fractionStr = fractionStr.Substring(0, 3);

                            int.TryParse(fractionStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out millis);
                        }

                        var ts = new TimeSpan(0, 0, minutes, seconds, millis);
                        result.Add(new LyricLine(ts, text, isSynchronized: true));
                    }
                }
            }
            else if (!hasAnyTimestamp)
            {
                // Potential plain lyric line before any timestamp is encountered
                result.Add(new LyricLine(TimeSpan.Zero, trimmed, isSynchronized: false));
            }
        }

        if (hasAnyTimestamp)
        {
            // Filter out any unsynchronized lines collected before the first timestamp and sort chronologically
            return result
                .Where(l => l.IsSynchronized)
                .OrderBy(l => l.Timestamp)
                .ToList();
        }

        // Entirely plain lyrics without timestamps
        return result;
    }
}
