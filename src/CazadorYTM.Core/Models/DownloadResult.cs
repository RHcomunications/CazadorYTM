namespace CazadorYTM.Core.Models;

public class DownloadResult
{
    public string Status { get; set; } = "completed";
    public string? TargetFolder { get; set; }
    public string? FilePath { get; set; }
    public int Total { get; set; } = 1;
    public string? Error { get; set; }
    public string? ErrorMessage { get => Error; set => Error = value; }
    public bool Success { get => Status == "completed" && string.IsNullOrEmpty(Error); set => Status = value ? "completed" : "error"; }
}