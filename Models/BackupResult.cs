namespace BackupSynchronizer.Models;

public class BackupResult
{
    public bool Success { get; set; }
    public int FilesCopied { get; set; }
    public int FilesSkipped { get; set; }
    public int Errors { get; set; }
    public List<string> CopiedFiles { get; set; } = new();
    public List<string> SkippedFiles { get; set; } = new();
    public List<string> ErrorMessages { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}
