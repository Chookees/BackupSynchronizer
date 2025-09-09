namespace BackupSynchronizer.Models;

public class SyncOptions : BackupOptions
{
    public bool EnableDeletionSync { get; set; } = false;
    public bool DryRun { get; set; } = false;
    public bool UseHashComparison { get; set; } = true;
    public bool CreateConflictBackups { get; set; } = true;
    public string ConflictBackupDirectory { get; set; } = "conflicts";
    public SyncMode SyncMode { get; set; } = SyncMode.Bidirectional;
    
    // History tracking options
    public bool EnableHistoryTracking { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = 30;
    public string HistoryDirectory { get; set; } = ".history";
    public bool AutoCleanup { get; set; } = true;
    
    // Restore options
    public string RestoreFilePath { get; set; } = string.Empty;
    public bool ListDeleted { get; set; } = false;
    public string ListHistoryPath { get; set; } = string.Empty;
    public bool CleanupHistory { get; set; } = false;
}

public enum SyncMode
{
    OneWay,        // Source -> Target (original backup mode)
    Bidirectional  // Two-way sync
}
