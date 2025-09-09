using BackupSynchronizer.Models;
using Microsoft.Extensions.Logging;

namespace BackupSynchronizer.Core;

public class LoggingService : ILoggingService
{
    private readonly ILogger<LoggingService> _logger;
    private readonly object _lockObject = new();
    private string? _logFilePath;

    public LoggingService(ILogger<LoggingService> logger)
    {
        _logger = logger;
    }

    public void LogInfo(string message)
    {
        _logger.LogInformation(message);
        WriteToLogFile($"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
    }

    public void LogWarning(string message)
    {
        _logger.LogWarning(message);
        WriteToLogFile($"[WARNING] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
    }

    public void LogError(string message, Exception? exception = null)
    {
        _logger.LogError(exception, message);
        var errorMessage = exception != null 
            ? $"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message} - {exception.Message}"
            : $"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
        WriteToLogFile(errorMessage);
    }

    public void LogFileOperation(string operation, string sourcePath, string targetPath)
    {
        var message = $"{operation} file: {sourcePath} -> {targetPath}";
        _logger.LogInformation(message);
        WriteToLogFile($"[FILE] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
    }

    public void WriteToLogFile(string message)
    {
        if (string.IsNullOrEmpty(_logFilePath))
            return;

        lock (_lockObject)
        {
            try
            {
                File.AppendAllText(_logFilePath, message + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write to log file: {LogFilePath}", _logFilePath);
            }
        }
    }

    public void InitializeLogFile(string logFilePath)
    {
        _logFilePath = logFilePath;
        
        try
        {
            // Create log file with header
            var header = $"Backup Synchronizer Log - Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine;
            header += new string('=', 60) + Environment.NewLine;
            
            File.WriteAllText(_logFilePath, header);
            _logger.LogInformation("Log file initialized: {LogFilePath}", _logFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize log file: {LogFilePath}", _logFilePath);
        }
    }
}
