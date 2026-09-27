namespace CazadorYTM.Core.Models;

using System.Collections.Generic;

public class SearchResult
{
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<DownloadEntry> Entries { get; set; } = new();
}