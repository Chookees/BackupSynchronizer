namespace BackupSynchronizer.Models;

public class SyncResult : BackupResult
{
    public int FilesSynchronized { get; set; }
    public int FilesDeleted { get; set; }
    public int ConflictsDetected { get; set; }
    public List<ConflictInfo> Conflicts { get; set; } = new();
    public List<string> DeletedFiles { get; set; } = new();
    public List<string> SkippedIdentical { get; set; } = new();
}

public class ConflictInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public DateTime SourceModified { get; set; }
    public DateTime TargetModified { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public string TargetHash { get; set; } = string.Empty;
    public string ConflictBackupPath { get; set; } = string.Empty;
    public DateTime ConflictDetectedAt { get; set; } = DateTime.Now;
}
