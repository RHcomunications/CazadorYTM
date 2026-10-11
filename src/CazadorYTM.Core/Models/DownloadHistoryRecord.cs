namespace CazadorYTM.Core.Models;

using System;
using System.Text.Json.Serialization;

public class DownloadHistoryRecord
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("format")]
    public string Format { get; set; } = "mp3";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "OK";

    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    public DownloadEntry ToDownloadEntry()
    {
        var entry = new DownloadEntry
        {
            Title = Title,
            Url = Url,
            Format = Format,
            Status = Status,
            FilePath = FilePath
        };

        if (DateTime.TryParse(Date, out var dt))
        {
            entry.Timestamp = dt;
        }

        return entry;
    }

    public static DownloadHistoryRecord FromDownloadEntry(DownloadEntry entry)
    {
        return new DownloadHistoryRecord
        {
            Title = entry.Title ?? string.Empty,
            Url = entry.Url ?? string.Empty,
            Format = entry.Format ?? "mp3",
            Status = entry.Status ?? "OK",
            FilePath = entry.FilePath ?? string.Empty,
            Date = (entry.Timestamp ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss")
        };
    }
}
