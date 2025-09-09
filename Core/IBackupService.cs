using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public interface IBackupService
{
    Task<BackupResult> RunBackupAsync(BackupOptions options);
    Task<BackupResult> SimpleBackupAsync(BackupOptions options);
}
