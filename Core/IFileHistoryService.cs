using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public interface IFileHistoryService
{
    Task TrackFileChangeAsync(string filePath, string originalPath, ChangeType changeType, string reason = "");
    Task<string> MoveToHistoryAsync(string filePath, string originalPath, ChangeType changeType, string reason = "");
    Task<List<FileHistory>> GetFileHistoryAsync(string filePath);
    Task<List<FileHistory>> GetDeletedFilesAsync();
    Task<FileHistory?> GetLatestVersionAsync(string filePath);
    Task<bool> RestoreFileAsync(string filePath, DateTime? restoreToDate = null);
    Task CleanupExpiredHistoryAsync(int retentionDays);
    Task InitializeDatabaseAsync();
    Task<List<FileHistory>> SearchHistoryAsync(string searchPattern, DateTime? fromDate = null, DateTime? toDate = null);
}
