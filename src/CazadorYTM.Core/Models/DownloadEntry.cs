namespace CazadorYTM.Core.Models;

using System;

public class DownloadEntry
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
    public int? Duration { get; set; }
    public string? Uploader { get; set; }
    public string? FilePath { get; set; }
    public string? Format { get; set; }
    public string? Status { get; set; }
    public DateTime? Timestamp { get; set; } = DateTime.Now;

    public string DisplayText
    {
        get
        {
            var title = string.IsNullOrWhiteSpace(Title) ? "Canción descargada" : Title;
            var uploader = !string.IsNullOrWhiteSpace(Uploader) ? $" ({Uploader})" : "";
            var duration = Duration.HasValue ? $" [{Helpers.FormatDuration(Duration.Value)}]" : "";
            var format = !string.IsNullOrWhiteSpace(Format) ? $" [{Format.ToUpper()}]" : "";
            return $"{title}{uploader}{duration}{format}";
        }
    }

    public override string ToString() => DisplayText;
}