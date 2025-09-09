using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public interface IFileComparisonService
{
    Task<string> CalculateFileHashAsync(string filePath);
    Task<FileComparisonResult> CompareFilesAsync(string sourcePath, string targetPath);
    bool IsFileNewer(string sourcePath, string targetPath);
    Task<bool> AreFilesIdenticalAsync(string sourcePath, string targetPath);
}

public class FileComparisonResult
{
    public bool AreIdentical { get; set; }
    public bool SourceIsNewer { get; set; }
    public bool TargetIsNewer { get; set; }
    public DateTime SourceModified { get; set; }
    public DateTime TargetModified { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public string TargetHash { get; set; } = string.Empty;
    public bool HasConflict { get; set; }
}
