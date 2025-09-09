using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public interface ILoggingService
{
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message, Exception? exception = null);
    void LogFileOperation(string operation, string sourcePath, string targetPath);
    void WriteToLogFile(string message);
    void InitializeLogFile(string logFilePath);
}
