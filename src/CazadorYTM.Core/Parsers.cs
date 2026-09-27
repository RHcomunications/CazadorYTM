namespace CazadorYTM.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class Parsers
{
    private static readonly Regex ArtistSplitRegex = new(@"[,;]|\s+/\s+", RegexOptions.Compiled);

    public static List<string> ParseSongList(string filePathOrContent)
    {
        if (File.Exists(filePathOrContent))
        {
            if (filePathOrContent.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                var csvResult = ParseSpotifyCsv(filePathOrContent);
                if (csvResult.Count > 0) return csvResult;
            }
            return ParsePlainTextSongs(filePathOrContent);
        }

        // For raw multiline CSV text pasted directly
        var firstLine = filePathOrContent.Split('\n').FirstOrDefault()?.ToLower().Trim() ?? "";
        if ((firstLine.Contains("track") || firstLine.Contains("title") || firstLine.Contains("canción") || firstLine.Contains("nombre")) &&
            (firstLine.Contains("artist") || firstLine.Contains("artista") || firstLine.Contains("uri") || firstLine.Contains("spotify") || firstLine.Contains(',') || firstLine.Contains(';')))
        {
            var csvResult = ParseSpotifyCsv(filePathOrContent);
            if (csvResult.Count > 0) return csvResult;
        }

        return ParsePlainTextSongs(filePathOrContent);
    }

    public static List<string> ParseSpotifyCsv(string filePathOrContent)
    {
        string content;
        if (File.Exists(filePathOrContent))
        {
            content = File.ReadAllText(filePathOrContent, Encoding.UTF8);
        }
        else
        {
            content = filePathOrContent;
        }

        content = content.TrimStart('\ufeff');
        var lines = content.Split('\n').Select(l => l.Replace("\r", "")).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0)
            return new List<string>();

        var sample = content.Substring(0, Math.Min(4096, content.Length));
        var delimiter = sample.Count(c => c == ';') > sample.Count(c => c == ',') ? ';' : ',';

        var allRows = lines.Select(l => ParseCsvLine(l, delimiter)).ToList();
        if (allRows.Count == 0)
            return new List<string>();

        var header = allRows[0];
        var trackIdx = -1;
        var artistIdx = -1;

        for (var idx = 0; idx < header.Length; idx++)
        {
            var colClean = header[idx].Trim().ToLower();
            if (trackIdx == -1 && (colClean == "track name" || colClean == "track_name" || colClean == "title" || colClean == "nombre de la canción" || colClean == "track"))
                trackIdx = idx;
            else if (artistIdx == -1 && (colClean == "artist name(s)" || colClean == "artist name" || colClean == "artist_name" || colClean == "artist" || colClean == "artista"))
                artistIdx = idx;
        }

        if (trackIdx == -1)
        {
            for (var idx = 0; idx < header.Length; idx++)
            {
                if (header[idx].Trim().ToLower().Contains("track") || header[idx].Trim().ToLower().Contains("song") || header[idx].Trim().ToLower().Contains("title"))
                {
                    trackIdx = idx;
                    break;
                }
            }
        }

        if (artistIdx == -1)
        {
            for (var idx = 0; idx < header.Length; idx++)
            {
                if (header[idx].Trim().ToLower().Contains("artist"))
                {
                    artistIdx = idx;
                    break;
                }
            }
        }

        if (trackIdx == -1) trackIdx = header.Length > 1 ? 1 : 0;
        if (artistIdx == -1) artistIdx = header.Length > 3 ? 3 : (header.Length > 2 ? 2 : -1);

        var queries = new List<string>();
        var hasHeader = header.Any(kw => kw.ToLower().Contains("track") || kw.ToLower().Contains("title") || kw.ToLower().Contains("artist") || kw.ToLower().Contains("uri"));
        var startRow = hasHeader ? 1 : 0;

        for (var rowIdx = startRow; rowIdx < allRows.Count; rowIdx++)
        {
            var row = allRows[rowIdx];
            if (row.Length == 0) continue;

            var trackName = trackIdx >= 0 && trackIdx < row.Length ? row[trackIdx].Trim() : "";
            var artistRaw = artistIdx >= 0 && artistIdx < row.Length ? row[artistIdx].Trim() : "";

            if (string.IsNullOrEmpty(trackName)) continue;

            if (!string.IsNullOrEmpty(artistRaw))
            {
                var firstArtist = ArtistSplitRegex.Split(artistRaw)[0].Trim();
                if (!string.IsNullOrEmpty(firstArtist))
                    queries.Add($"{trackName} {firstArtist}");
                else
                    queries.Add(trackName);
            }
            else
            {
                queries.Add(trackName);
            }
        }

        return queries;
    }

    public static List<string> ParsePlainTextSongs(string filePathOrContent)
    {
        string content;
        if (File.Exists(filePathOrContent))
        {
            content = File.ReadAllText(filePathOrContent, Encoding.UTF8);
        }
        else
        {
            content = filePathOrContent;
        }

        content = content.TrimStart('\ufeff');
        var results = new List<string>();
        foreach (var line in content.Split('\n'))
        {
            var cleanLine = line.Replace("\r", "").Trim().TrimStart('\ufeff');
            if (!string.IsNullOrEmpty(cleanLine) && !cleanLine.StartsWith('#'))
                results.Add(cleanLine);
        }

        return results;
    }

    private static string[] ParseCsvLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
            else
            {
                if (ch == '"')
                {
                    inQuotes = true;
                }
                else if (ch == delimiter)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }
}