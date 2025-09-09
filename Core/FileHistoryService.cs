using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BackupSynchronizer.Data;
using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public class FileHistoryService : IFileHistoryService
{
    private readonly ILogger<FileHistoryService> _logger;
    private readonly FileHistoryContext _context;
    private readonly IFileComparisonService _fileComparisonService;

    public FileHistoryService(
        ILogger<FileHistoryService> logger,
        FileHistoryContext context,
        IFileComparisonService fileComparisonService)
    {
        _logger = logger;
        _context = context;
        _fileComparisonService = fileComparisonService;
    }

    public async Task InitializeDatabaseAsync()
    {
        try
        {
            await _context.Database.EnsureCreatedAsync();
            _logger.LogInformation("File history database initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize file history database");
            throw;
        }
    }

    public async Task TrackFileChangeAsync(string filePath, string originalPath, ChangeType changeType, string reason = "")
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var fileHash = await _fileComparisonService.CalculateFileHashAsync(filePath);
            
            var history = new FileHistory
            {
                FilePath = filePath,
                OriginalPath = originalPath,
                FileHash = fileHash,
                ChangeType = changeType.ToString(),
                Timestamp = DateTime.UtcNow,
                FileSize = fileInfo.Exists ? fileInfo.Length : 0,
                Reason = reason,
                IsDeleted = changeType == ChangeType.Deleted
            };

            _context.FileHistories.Add(history);
            await _context.SaveChangesAsync();
            
            _logger.LogDebug("Tracked file change: {FilePath} - {ChangeType}", filePath, changeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track file change: {FilePath}", filePath);
            throw;
        }
    }

    public async Task<string> MoveToHistoryAsync(string filePath, string originalPath, ChangeType changeType, string reason = "")
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("File does not exist for history tracking: {FilePath}", filePath);
                return string.Empty;
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = Path.GetFileName(filePath);
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), originalPath);
            var historyPath = Path.Combine(".history", relativePath, $"{timestamp}_{fileName}");
            
            // Ensure history directory exists
            var historyDir = Path.GetDirectoryName(historyPath);
            if (!string.IsNullOrEmpty(historyDir) && !Directory.Exists(historyDir))
            {
                Directory.CreateDirectory(historyDir);
            }

            // Move file to history
            File.Move(filePath, historyPath);
            
            // Track in database
            await TrackFileChangeAsync(historyPath, originalPath, changeType, reason);
            
            _logger.LogInformation("Moved file to history: {FilePath} -> {HistoryPath}", filePath, historyPath);
            return historyPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move file to history: {FilePath}", filePath);
            throw;
        }
    }

    public async Task<List<FileHistory>> GetFileHistoryAsync(string filePath)
    {
        try
        {
            return await _context.FileHistories
                .Where(h => h.OriginalPath == filePath)
                .OrderByDescending(h => h.Timestamp)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get file history: {FilePath}", filePath);
            return new List<FileHistory>();
        }
    }

    public async Task<List<FileHistory>> GetDeletedFilesAsync()
    {
        try
        {
            return await _context.FileHistories
                .Where(h => h.IsDeleted)
                .OrderByDescending(h => h.Timestamp)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get deleted files");
            return new List<FileHistory>();
        }
    }

    public async Task<FileHistory?> GetLatestVersionAsync(string filePath)
    {
        try
        {
            return await _context.FileHistories
                .Where(h => h.OriginalPath == filePath && !h.IsDeleted && h.ChangeType != "Deleted")
                .OrderByDescending(h => h.Timestamp)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest version: {FilePath}", filePath);
            return null;
        }
    }

    public async Task<bool> RestoreFileAsync(string filePath, DateTime? restoreToDate = null)
    {
        try
        {
            var history = restoreToDate.HasValue
                ? await _context.FileHistories
                    .Where(h => h.OriginalPath == filePath && h.Timestamp <= restoreToDate.Value && !h.IsDeleted && !string.IsNullOrEmpty(h.HistoryPath))
                    .OrderByDescending(h => h.Timestamp)
                    .FirstOrDefaultAsync()
                : await _context.FileHistories
                    .Where(h => h.OriginalPath == filePath && !h.IsDeleted && !string.IsNullOrEmpty(h.HistoryPath))
                    .OrderByDescending(h => h.Timestamp)
                    .FirstOrDefaultAsync();

            if (history == null)
            {
                _logger.LogWarning("No history found for file: {FilePath}", filePath);
                return false;
            }

            if (!File.Exists(history.HistoryPath))
            {
                _logger.LogWarning("History file not found: {HistoryPath}", history.HistoryPath);
                return false;
            }

            // Ensure target directory exists
            var targetDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Copy file back to original location
            File.Copy(history.HistoryPath, filePath, true);
            
            // Track the restore operation
            await TrackFileChangeAsync(filePath, filePath, ChangeType.Created, "Restored from history");
            
            _logger.LogInformation("Restored file from history: {FilePath} from {HistoryPath}", filePath, history.HistoryPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore file: {FilePath}", filePath);
            return false;
        }
    }

    public async Task CleanupExpiredHistoryAsync(int retentionDays)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
            
            var expiredHistories = await _context.FileHistories
                .Where(h => h.Timestamp < cutoffDate)
                .ToListAsync();

            foreach (var history in expiredHistories)
            {
                try
                {
                    // Delete physical file if it exists
                    if (File.Exists(history.HistoryPath))
                    {
                        File.Delete(history.HistoryPath);
                    }
                    
                    // Remove from database
                    _context.FileHistories.Remove(history);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup history file: {HistoryPath}", history.HistoryPath);
                }
            }

            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Cleaned up {Count} expired history files", expiredHistories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired history");
            throw;
        }
    }

    public async Task<List<FileHistory>> SearchHistoryAsync(string searchPattern, DateTime? fromDate = null, DateTime? toDate = null)
    {
        try
        {
            var query = _context.FileHistories.AsQueryable();
            
            if (!string.IsNullOrEmpty(searchPattern))
            {
                query = query.Where(h => h.OriginalPath.Contains(searchPattern) || 
                                       h.FilePath.Contains(searchPattern));
            }
            
            if (fromDate.HasValue)
            {
                query = query.Where(h => h.Timestamp >= fromDate.Value);
            }
            
            if (toDate.HasValue)
            {
                query = query.Where(h => h.Timestamp <= toDate.Value);
            }
            
            return await query
                .OrderByDescending(h => h.Timestamp)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search history with pattern: {SearchPattern}", searchPattern);
            return new List<FileHistory>();
        }
    }
}
