using System.ComponentModel.DataAnnotations;

namespace BackupSynchronizer.Models;

public class FileHistory
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(500)]
    public string OriginalPath { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(64)]
    public string FileHash { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(20)]
    public string ChangeType { get; set; } = string.Empty; // Modified, Deleted, Renamed
    
    public DateTime Timestamp { get; set; }
    
    [MaxLength(500)]
    public string HistoryPath { get; set; } = string.Empty;
    
    public long FileSize { get; set; }
    
    [MaxLength(100)]
    public string Reason { get; set; } = string.Empty; // Backup, Sync, Manual, etc.
    
    public bool IsDeleted { get; set; } = false;
}

public class HistoryOptions
{
    public bool EnableHistoryTracking { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = 30;
    public string HistoryDirectory { get; set; } = ".history";
    public string DatabasePath { get; set; } = "file_history.db";
    public bool AutoCleanup { get; set; } = true;
    public int CleanupIntervalHours { get; set; } = 24;
}

public class RestoreOptions
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime? RestoreToDate { get; set; }
    public bool ListDeleted { get; set; } = false;
    public bool ListHistory { get; set; } = false;
    public string OutputFormat { get; set; } = "table"; // table, json, csv
}

public enum ChangeType
{
    Modified,
    Deleted,
    Renamed,
    Created,
    Moved
}
