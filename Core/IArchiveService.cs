using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public interface IArchiveService
{
    Task<ArchiveResult> CreateArchiveAsync(ArchiveOptions options);
    Task<ArchiveResult> CreateZipArchiveAsync(ArchiveOptions options);
    Task<bool> ExtractArchiveAsync(string archivePath, string extractPath);
    Task<List<string>> ListArchiveContentsAsync(string archivePath);
}
