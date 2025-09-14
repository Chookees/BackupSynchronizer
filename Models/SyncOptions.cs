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
    
    // Archive options
    public string ArchivePath { get; set; } = string.Empty;
    public long? SplitSizeBytes { get; set; } = null;
    public string CompressionLevel { get; set; } = "Optimal";
    public bool ExtractArchive { get; set; } = false;
    public bool ListArchive { get; set; } = false;
    
    // Schedule options
    public string ScheduleType { get; set; } = string.Empty; // daily, weekly, monthly, custom
    public string ScheduleName { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public bool CreateSchedule { get; set; } = false;
    public bool DeleteSchedule { get; set; } = false;
    public bool ListSchedules { get; set; } = false;
    public bool ExecuteSchedule { get; set; } = false;
    public bool GenerateTaskScheduler { get; set; } = false;
    public bool GenerateCron { get; set; } = false;
    public bool DeleteSourceAfterArchive { get; set; } = false;
    public bool CreateTimestampedArchives { get; set; } = true;
}

public enum SyncMode
{
    OneWay,        // Source -> Target (original backup mode)
    Bidirectional  // Two-way sync
}
