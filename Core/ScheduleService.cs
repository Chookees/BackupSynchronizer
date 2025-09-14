using System.Text.Json;
using Microsoft.Extensions.Logging;
using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public class ScheduleService : IScheduleService
{
    private readonly ILogger<ScheduleService> _logger;
    private readonly IArchiveService _archiveService;
    private readonly IBackupService _backupService;
    private readonly ISyncService _syncService;
    private readonly IFileHistoryService _fileHistoryService;
    private readonly List<ScheduleResult> _scheduleHistory = new();

    public ScheduleService(
        ILogger<ScheduleService> logger,
        IArchiveService archiveService,
        IBackupService backupService,
        ISyncService syncService,
        IFileHistoryService fileHistoryService)
    {
        _logger = logger;
        _archiveService = archiveService;
        _backupService = backupService;
        _syncService = syncService;
        _fileHistoryService = fileHistoryService;
    }

    public async Task<ScheduleResult> ExecuteScheduledArchiveAsync(ScheduleOptions schedule)
    {
        var result = new ScheduleResult
        {
            ScheduleName = schedule.ScheduleName,
            StartTime = DateTime.Now
        };

        try
        {
            _logger.LogInformation("Executing scheduled archive: {ScheduleName}", schedule.ScheduleName);

            // Create archive options
            var archiveOptions = new ArchiveOptions
            {
                SourcePath = schedule.SourcePath,
                OutputPath = schedule.CreateTimestampedArchives 
                    ? CreateTimestampedPath(schedule.ArchivePath)
                    : schedule.ArchivePath,
                CompressionLevel = Enum.Parse<Models.CompressionLevel>(schedule.CompressionLevel),
                SplitSizeBytes = schedule.SplitSizeBytes,
                IncludePatterns = schedule.IncludePatterns,
                ExcludePatterns = schedule.ExcludePatterns,
                CreateLogFile = true,
                LogFilePath = $"schedule_{schedule.ScheduleName}_{DateTime.Now:yyyyMMdd_HHmmss}.log"
            };

            // Execute archive creation
            var archiveResult = await _archiveService.CreateArchiveAsync(archiveOptions);
            
            result.Success = archiveResult.Success;
            result.ArchivePath = archiveResult.ArchivePath;
            result.FilesArchived = archiveResult.FilesArchived;
            result.OriginalSizeBytes = archiveResult.OriginalSizeBytes;
            result.CompressedSizeBytes = archiveResult.CompressedSizeBytes;
            result.CompressionRatio = archiveResult.CompressionRatio;
            result.Errors = archiveResult.Errors;

            // Delete source if requested
            if (result.Success && schedule.DeleteSourceAfterArchive && Directory.Exists(schedule.SourcePath))
            {
                try
                {
                    Directory.Delete(schedule.SourcePath, true);
                    _logger.LogInformation("Deleted source directory after successful archive: {SourcePath}", schedule.SourcePath);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to delete source directory: {ex.Message}");
                    _logger.LogWarning(ex, "Failed to delete source directory: {SourcePath}", schedule.SourcePath);
                }
            }

            // Calculate next run
            result.NextRun = await CalculateNextRunAsync(schedule);
            schedule.NextRun = result.NextRun;
            schedule.LastRun = result.StartTime;

            _logger.LogInformation("Scheduled archive completed: {ScheduleName}, Success: {Success}, Next run: {NextRun}",
                schedule.ScheduleName, result.Success, result.NextRun);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
            _logger.LogError(ex, "Scheduled archive failed: {ScheduleName}", schedule.ScheduleName);
        }
        finally
        {
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            
            // Store in history
            _scheduleHistory.Add(result);
            if (_scheduleHistory.Count > 1000) // Keep last 1000 results
            {
                _scheduleHistory.RemoveAt(0);
            }
        }

        return result;
    }

    public async Task<ScheduleResult> ExecuteScheduledBackupAsync(ScheduleOptions schedule)
    {
        var result = new ScheduleResult
        {
            ScheduleName = schedule.ScheduleName,
            StartTime = DateTime.Now
        };

        try
        {
            _logger.LogInformation("Executing scheduled backup: {ScheduleName}", schedule.ScheduleName);

            var backupOptions = new BackupOptions
            {
                SourcePath = schedule.SourcePath,
                TargetPath = schedule.ArchivePath,
                IncludePatterns = schedule.IncludePatterns,
                ExcludePatterns = schedule.ExcludePatterns,
                CreateLogFile = true,
                LogFilePath = $"schedule_{schedule.ScheduleName}_{DateTime.Now:yyyyMMdd_HHmmss}.log"
            };

            var backupResult = await _backupService.SimpleBackupAsync(backupOptions);
            
            result.Success = backupResult.Success;
            result.FilesArchived = backupResult.FilesCopied;
            result.Errors = backupResult.ErrorMessages;

            result.NextRun = await CalculateNextRunAsync(schedule);
            schedule.NextRun = result.NextRun;
            schedule.LastRun = result.StartTime;

            _logger.LogInformation("Scheduled backup completed: {ScheduleName}, Success: {Success}",
                schedule.ScheduleName, result.Success);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
            _logger.LogError(ex, "Scheduled backup failed: {ScheduleName}", schedule.ScheduleName);
        }
        finally
        {
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            _scheduleHistory.Add(result);
        }

        return result;
    }

    public async Task<ScheduleResult> ExecuteScheduledSyncAsync(ScheduleOptions schedule)
    {
        var result = new ScheduleResult
        {
            ScheduleName = schedule.ScheduleName,
            StartTime = DateTime.Now
        };

        try
        {
            _logger.LogInformation("Executing scheduled sync: {ScheduleName}", schedule.ScheduleName);

            var syncOptions = new SyncOptions
            {
                SourcePath = schedule.SourcePath,
                TargetPath = schedule.ArchivePath,
                SyncMode = SyncMode.Bidirectional,
                IncludePatterns = schedule.IncludePatterns,
                ExcludePatterns = schedule.ExcludePatterns,
                CreateLogFile = true,
                LogFilePath = $"schedule_{schedule.ScheduleName}_{DateTime.Now:yyyyMMdd_HHmmss}.log"
            };

            var syncResult = await _syncService.RunSyncAsync(syncOptions);
            
            result.Success = syncResult.Success;
            result.FilesArchived = syncResult.FilesCopied + syncResult.FilesDeleted;
            result.Errors = syncResult.ErrorMessages;

            result.NextRun = await CalculateNextRunAsync(schedule);
            schedule.NextRun = result.NextRun;
            schedule.LastRun = result.StartTime;

            _logger.LogInformation("Scheduled sync completed: {ScheduleName}, Success: {Success}",
                schedule.ScheduleName, result.Success);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
            _logger.LogError(ex, "Scheduled sync failed: {ScheduleName}", schedule.ScheduleName);
        }
        finally
        {
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            _scheduleHistory.Add(result);
        }

        return result;
    }

    public async Task<List<ScheduleOptions>> LoadSchedulesAsync(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                _logger.LogInformation("Schedule config file not found: {ConfigPath}", configPath);
                return new List<ScheduleOptions>();
            }

            var json = await File.ReadAllTextAsync(configPath);
            var schedules = JsonSerializer.Deserialize<List<ScheduleOptions>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogInformation("Loaded {Count} schedules from {ConfigPath}", schedules?.Count ?? 0, configPath);
            return schedules ?? new List<ScheduleOptions>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load schedules from {ConfigPath}", configPath);
            return new List<ScheduleOptions>();
        }
    }

    public async Task SaveSchedulesAsync(List<ScheduleOptions> schedules, string configPath)
    {
        try
        {
            var json = JsonSerializer.Serialize(schedules, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(configPath, json);
            _logger.LogInformation("Saved {Count} schedules to {ConfigPath}", schedules.Count, configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save schedules to {ConfigPath}", configPath);
            throw;
        }
    }

    public async Task<ScheduleOptions> CreateScheduleAsync(string name, string type, string source, string target)
    {
        var schedule = new ScheduleOptions
        {
            ScheduleName = name,
            ScheduleType = type,
            SourcePath = source,
            ArchivePath = target,
            Enabled = true,
            NextRun = await CalculateNextRunAsync(new ScheduleOptions { ScheduleType = type })
        };

        _logger.LogInformation("Created schedule: {Name}, Type: {Type}, Source: {Source}, Target: {Target}",
            name, type, source, target);

        return schedule;
    }

    public async Task<bool> DeleteScheduleAsync(string name)
    {
        var schedules = await LoadSchedulesAsync("schedules.json");
        var schedule = schedules.FirstOrDefault(s => s.ScheduleName.Equals(name, StringComparison.OrdinalIgnoreCase));
        
        if (schedule == null)
        {
            _logger.LogWarning("Schedule not found for deletion: {Name}", name);
            return false;
        }

        schedules.Remove(schedule);
        await SaveSchedulesAsync(schedules, "schedules.json");
        
        _logger.LogInformation("Deleted schedule: {Name}", name);
        return true;
    }

    public async Task<ScheduleOptions?> GetScheduleAsync(string name)
    {
        var schedules = await LoadSchedulesAsync("schedules.json");
        return schedules.FirstOrDefault(s => s.ScheduleName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<ScheduleOptions>> GetUpcomingSchedulesAsync(TimeSpan timeWindow)
    {
        var schedules = await LoadSchedulesAsync("schedules.json");
        var now = DateTime.Now;
        var endTime = now.Add(timeWindow);

        return schedules.Where(s => s.Enabled && s.NextRun.HasValue && s.NextRun.Value >= now && s.NextRun.Value <= endTime)
                       .OrderBy(s => s.NextRun)
                       .ToList();
    }

    public async Task<DateTime> CalculateNextRunAsync(ScheduleOptions schedule)
    {
        var now = DateTime.Now;
        var nextRun = now;

        switch (schedule.ScheduleType.ToLower())
        {
            case "daily":
                nextRun = new DateTime(now.Year, now.Month, now.Day, schedule.DailyTime.Hour, schedule.DailyTime.Minute, schedule.DailyTime.Second);
                if (nextRun <= now)
                {
                    nextRun = nextRun.AddDays(1);
                }
                break;

            case "weekly":
                var daysUntilTarget = ((int)schedule.WeeklyDay - (int)now.DayOfWeek + 7) % 7;
                nextRun = new DateTime(now.Year, now.Month, now.Day, schedule.DailyTime.Hour, schedule.DailyTime.Minute, schedule.DailyTime.Second);
                nextRun = nextRun.AddDays(daysUntilTarget);
                if (nextRun <= now)
                {
                    nextRun = nextRun.AddDays(7);
                }
                break;

            case "monthly":
                nextRun = new DateTime(now.Year, now.Month, schedule.MonthlyDay, schedule.DailyTime.Hour, schedule.DailyTime.Minute, schedule.DailyTime.Second);
                if (nextRun <= now)
                {
                    nextRun = nextRun.AddMonths(1);
                }
                break;

            case "custom":
                if (!string.IsNullOrEmpty(schedule.CronExpression))
                {
                    // For now, return a simple daily schedule
                    // In a real implementation, you'd use a cron parser library
                    nextRun = now.AddDays(1);
                }
                break;

            default:
                nextRun = now.AddDays(1);
                break;
        }

        return await Task.FromResult(nextRun);
    }

    public async Task<bool> ValidateScheduleAsync(ScheduleOptions schedule)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(schedule.ScheduleName))
            errors.Add("Schedule name is required");

        if (string.IsNullOrEmpty(schedule.SourcePath))
            errors.Add("Source path is required");

        if (string.IsNullOrEmpty(schedule.ArchivePath))
            errors.Add("Archive path is required");

        if (!Directory.Exists(schedule.SourcePath))
            errors.Add($"Source directory does not exist: {schedule.SourcePath}");

        if (!new[] { "daily", "weekly", "monthly", "custom" }.Contains(schedule.ScheduleType.ToLower()))
            errors.Add("Invalid schedule type. Must be: daily, weekly, monthly, or custom");

        if (errors.Any())
        {
            _logger.LogWarning("Schedule validation failed for {Name}: {Errors}", schedule.ScheduleName, string.Join(", ", errors));
            return false;
        }

        return await Task.FromResult(true);
    }

    public async Task<List<ScheduleResult>> GetScheduleHistoryAsync(string scheduleName, int limit = 50)
    {
        var history = _scheduleHistory
            .Where(r => r.ScheduleName.Equals(scheduleName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.StartTime)
            .Take(limit)
            .ToList();

        return await Task.FromResult(history);
    }

    public async Task<string> GenerateWindowsTaskSchedulerXmlAsync(ScheduleOptions schedule, string executablePath)
    {
        var nextRun = await CalculateNextRunAsync(schedule);
        
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>BackupSynchronizer scheduled task for {schedule.ScheduleName}</Description>
  </RegistrationInfo>
  <Triggers>
    <TimeTrigger>
      <StartBoundary>{nextRun:yyyy-MM-ddTHH:mm:ss}</StartBoundary>
      <Enabled>true</Enabled>
      <Repetition>
        <Interval>P1D</Interval>
      </Repetition>
    </TimeTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT72H</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{executablePath}</Command>
      <Arguments>--schedule-execute ""{schedule.ScheduleName}""</Arguments>
    </Exec>
  </Actions>
</Task>";

        return await Task.FromResult(xml);
    }

    public async Task<string> GenerateCronExpressionAsync(ScheduleOptions schedule)
    {
        var cronExpression = schedule.ScheduleType.ToLower() switch
        {
            "daily" => $"{schedule.DailyTime.Minute} {schedule.DailyTime.Hour} * * *",
            "weekly" => $"{schedule.DailyTime.Minute} {schedule.DailyTime.Hour} * * {(int)schedule.WeeklyDay}",
            "monthly" => $"{schedule.DailyTime.Minute} {schedule.DailyTime.Hour} {schedule.MonthlyDay} * *",
            "custom" => schedule.CronExpression,
            _ => "0 2 * * *" // Default to daily at 2 AM
        };

        return await Task.FromResult(cronExpression);
    }

    private string CreateTimestampedPath(string basePath)
    {
        var directory = Path.GetDirectoryName(basePath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(basePath);
        var extension = Path.GetExtension(basePath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        
        return Path.Combine(directory, $"{fileName}_{timestamp}{extension}");
    }
}
