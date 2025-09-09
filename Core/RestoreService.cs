using Microsoft.Extensions.Logging;
using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public class RestoreService : IRestoreService
{
    private readonly ILogger<RestoreService> _logger;
    private readonly IFileHistoryService _fileHistoryService;

    public RestoreService(
        ILogger<RestoreService> logger,
        IFileHistoryService fileHistoryService)
    {
        _logger = logger;
        _fileHistoryService = fileHistoryService;
    }

    public async Task<RestoreResult> RestoreFileAsync(RestoreOptions options)
    {
        var result = new RestoreResult
        {
            RestoreTimestamp = DateTime.UtcNow
        };

        try
        {
            if (options.ListDeleted)
            {
                var deletedFiles = await ListDeletedFilesAsync();
                result.Success = true;
                result.Message = $"Found {deletedFiles.Count} deleted files";
                return result;
            }

            if (options.ListHistory)
            {
                var history = await ListFileHistoryAsync(options.FilePath);
                result.Success = true;
                result.Message = $"Found {history.Count} history entries for {options.FilePath}";
                return result;
            }

            if (string.IsNullOrEmpty(options.FilePath))
            {
                result.Success = false;
                result.Message = "File path is required for restore operation";
                return result;
            }

            var restored = await _fileHistoryService.RestoreFileAsync(options.FilePath, options.RestoreToDate);
            
            if (restored)
            {
                result.Success = true;
                result.Message = $"Successfully restored {options.FilePath}";
                result.RestoredFilePath = options.FilePath;
                
                var latestVersion = await _fileHistoryService.GetLatestVersionAsync(options.FilePath);
                if (latestVersion != null)
                {
                    result.SourceHistoryPath = latestVersion.HistoryPath;
                }
            }
            else
            {
                result.Success = false;
                result.Message = $"Failed to restore {options.FilePath} - no history found";
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Error during restore: {ex.Message}";
            result.Errors.Add(ex.Message);
            _logger.LogError(ex, "Failed to restore file: {FilePath}", options.FilePath);
            return result;
        }
    }

    public async Task<List<FileHistory>> ListDeletedFilesAsync()
    {
        try
        {
            return await _fileHistoryService.GetDeletedFilesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list deleted files");
            return new List<FileHistory>();
        }
    }

    public async Task<List<FileHistory>> ListFileHistoryAsync(string filePath)
    {
        try
        {
            return await _fileHistoryService.GetFileHistoryAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list file history: {FilePath}", filePath);
            return new List<FileHistory>();
        }
    }

    public async Task<RestoreResult> RestoreToDateAsync(string filePath, DateTime restoreDate)
    {
        var options = new RestoreOptions
        {
            FilePath = filePath,
            RestoreToDate = restoreDate
        };

        return await RestoreFileAsync(options);
    }
}
