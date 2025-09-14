using System.Text.Json.Serialization;

namespace BackupSynchronizer.Models;

public class ScheduleOptions
{
    public string ScheduleType { get; set; } = string.Empty; // daily, weekly, monthly, custom
    public string CronExpression { get; set; } = string.Empty; // For custom schedules
    public string SourcePath { get; set; } = string.Empty;
    public string ArchivePath { get; set; } = string.Empty;
    public string ScheduleName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime? NextRun { get; set; }
    public DateTime? LastRun { get; set; }
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(5);
    public bool DeleteSourceAfterArchive { get; set; } = false;
    public bool CreateTimestampedArchives { get; set; } = true;
    public string EmailNotification { get; set; } = string.Empty;
    public bool NotifyOnSuccess { get; set; } = false;
    public bool NotifyOnFailure { get; set; } = true;
    
    // Archive-specific options
    public string CompressionLevel { get; set; } = "Optimal";
    public long? SplitSizeBytes { get; set; } = null;
    public List<string> IncludePatterns { get; set; } = new();
    public List<string> ExcludePatterns { get; set; } = new();
    
    // Schedule-specific settings
    public ScheduleTime DailyTime { get; set; } = new() { Hour = 2, Minute = 0 }; // 2:00 AM
    public ScheduleDay WeeklyDay { get; set; } = ScheduleDay.Sunday;
    public int MonthlyDay { get; set; } = 1; // 1st of month
    public string TimeZone { get; set; } = "UTC";
}

public class ScheduleTime
{
    public int Hour { get; set; } = 0;
    public int Minute { get; set; } = 0;
    public int Second { get; set; } = 0;
}

public enum ScheduleDay
{
    Sunday = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 3,
    Thursday = 4,
    Friday = 5,
    Saturday = 6
}

public class ScheduleConfig
{
    public List<ScheduleOptions> Schedules { get; set; } = new();
    public string ConfigPath { get; set; } = "schedules.json";
    public bool AutoSave { get; set; } = true;
    public bool EnableLogging { get; set; } = true;
    public string LogPath { get; set; } = "schedule.log";
}

public class ScheduleResult
{
    public bool Success { get; set; }
    public string ScheduleName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string ArchivePath { get; set; } = string.Empty;
    public long FilesArchived { get; set; }
    public long OriginalSizeBytes { get; set; }
    public long CompressedSizeBytes { get; set; }
    public double CompressionRatio { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public DateTime? NextRun { get; set; }
    public bool RetryScheduled { get; set; } = false;
}
