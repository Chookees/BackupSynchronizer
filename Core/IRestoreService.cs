using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public interface IRestoreService
{
    Task<RestoreResult> RestoreFileAsync(RestoreOptions options);
    Task<List<FileHistory>> ListDeletedFilesAsync();
    Task<List<FileHistory>> ListFileHistoryAsync(string filePath);
    Task<RestoreResult> RestoreToDateAsync(string filePath, DateTime restoreDate);
}

public class RestoreResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RestoredFilePath { get; set; } = string.Empty;
    public string SourceHistoryPath { get; set; } = string.Empty;
    public DateTime RestoreTimestamp { get; set; }
    public List<string> Errors { get; set; } = new();
}
