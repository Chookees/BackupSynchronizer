using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public interface IScheduleService
{
    Task<ScheduleResult> ExecuteScheduledArchiveAsync(ScheduleOptions schedule);
    Task<ScheduleResult> ExecuteScheduledBackupAsync(ScheduleOptions schedule);
    Task<ScheduleResult> ExecuteScheduledSyncAsync(ScheduleOptions schedule);
    Task<List<ScheduleOptions>> LoadSchedulesAsync(string configPath);
    Task SaveSchedulesAsync(List<ScheduleOptions> schedules, string configPath);
    Task<ScheduleOptions> CreateScheduleAsync(string name, string type, string source, string target);
    Task<bool> DeleteScheduleAsync(string name);
    Task<ScheduleOptions?> GetScheduleAsync(string name);
    Task<List<ScheduleOptions>> GetUpcomingSchedulesAsync(TimeSpan timeWindow);
    Task<DateTime> CalculateNextRunAsync(ScheduleOptions schedule);
    Task<bool> ValidateScheduleAsync(ScheduleOptions schedule);
    Task<List<ScheduleResult>> GetScheduleHistoryAsync(string scheduleName, int limit = 50);
    Task<string> GenerateWindowsTaskSchedulerXmlAsync(ScheduleOptions schedule, string executablePath);
    Task<string> GenerateCronExpressionAsync(ScheduleOptions schedule);
}
