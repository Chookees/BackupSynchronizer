using BackupSynchronizer.Models;
using Microsoft.Extensions.Logging;

namespace BackupSynchronizer.Core;

public class BackupService : IBackupService
{
    private readonly ILogger<BackupService> _logger;
    private readonly IFileFilterService _fileFilterService;
    private readonly ILoggingService _loggingService;
    private readonly IFileHistoryService _fileHistoryService;

    public BackupService(
        ILogger<BackupService> logger,
        IFileFilterService fileFilterService,
        ILoggingService loggingService,
        IFileHistoryService fileHistoryService)
    {
        _logger = logger;
        _fileFilterService = fileFilterService;
        _loggingService = loggingService;
        _fileHistoryService = fileHistoryService;
    }

    public async Task<BackupResult> RunBackupAsync(BackupOptions options)
    {
        _logger.LogInformation("Starting backup process with mode: {Mode}", options.Mode);

        return options.Mode.ToLower() switch
        {
            "simple" => await SimpleBackupAsync(options),
            _ => throw new ArgumentException($"Unknown backup mode: {options.Mode}")
        };
    }

    public async Task<BackupResult> SimpleBackupAsync(BackupOptions options)
    {
        var result = new BackupResult
        {
            StartTime = DateTime.Now
        };

        try
        {
            // Validate paths
            if (!Directory.Exists(options.SourcePath))
            {
                throw new DirectoryNotFoundException($"Source directory does not exist: {options.SourcePath}");
            }

            // Create target directory if it doesn't exist
            if (!Directory.Exists(options.TargetPath))
            {
                Directory.CreateDirectory(options.TargetPath);
                _logger.LogInformation("Created target directory: {TargetPath}", options.TargetPath);
            }

            // Initialize logging
            if (options.CreateLogFile)
            {
                _loggingService.InitializeLogFile(options.LogFilePath);
            }

            _logger.LogInformation("Starting simple backup from {SourcePath} to {TargetPath}", 
                options.SourcePath, options.TargetPath);

            // Copy files recursively
            await CopyDirectoryRecursiveAsync(options.SourcePath, options.TargetPath, options, result);

            result.Success = true;
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;

            _logger.LogInformation("Backup completed successfully. Files copied: {FilesCopied}, Errors: {Errors}, Duration: {Duration}",
                result.FilesCopied, result.Errors, result.Duration);

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            result.ErrorMessages.Add(ex.Message);

            _logger.LogError(ex, "Backup failed: {Message}", ex.Message);
            throw;
        }
    }

    private async Task CopyDirectoryRecursiveAsync(string sourceDir, string targetDir, BackupOptions options, BackupResult result)
    {
        // Get all files in the current directory
        var files = Directory.GetFiles(sourceDir);
        
        foreach (var file in files)
        {
            try
            {
                var fileName = Path.GetFileName(file);
                var targetFilePath = Path.Combine(targetDir, fileName);

                // Check if file should be included
                if (!_fileFilterService.ShouldIncludeFile(file, options.IncludePatterns, options.ExcludePatterns))
                {
                    result.SkippedFiles.Add(file);
                    result.FilesSkipped++;
                    _logger.LogDebug("Skipped file (filtered): {FilePath}", file);
                    continue;
                }

                // Check if target file exists and handle overwrite
                if (File.Exists(targetFilePath) && !options.OverwriteExisting)
                {
                    result.SkippedFiles.Add(file);
                    result.FilesSkipped++;
                    _logger.LogDebug("Skipped file (exists): {FilePath}", file);
                    continue;
                }

                // Track history if target file exists and we're overwriting
                if (File.Exists(targetFilePath) && options.OverwriteExisting)
                {
                    await _fileHistoryService.MoveToHistoryAsync(targetFilePath, targetFilePath, ChangeType.Modified, "Backup overwrite");
                }

                // Copy the file
                File.Copy(file, targetFilePath, options.OverwriteExisting);
                
                result.CopiedFiles.Add(file);
                result.FilesCopied++;
                
                // Track the new file in history
                await _fileHistoryService.TrackFileChangeAsync(targetFilePath, targetFilePath, ChangeType.Created, "Backup copy");
                
                // Log the operation
                _loggingService.LogFileOperation("Copying", file, targetFilePath);
                Console.WriteLine($"Copying file {file} to {targetFilePath}");
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add($"Failed to copy {file}: {ex.Message}");
                _logger.LogError(ex, "Failed to copy file: {FilePath}", file);
            }
        }

        // Process subdirectories
        var subdirectories = Directory.GetDirectories(sourceDir);
        foreach (var subdir in subdirectories)
        {
            try
            {
                var dirName = Path.GetFileName(subdir);
                var targetSubdir = Path.Combine(targetDir, dirName);

                // Check if directory should be included
                if (!_fileFilterService.ShouldIncludeDirectory(subdir, options.IncludePatterns, options.ExcludePatterns))
                {
                    _logger.LogDebug("Skipped directory (filtered): {DirectoryPath}", subdir);
                    continue;
                }

                // Create target subdirectory if it doesn't exist
                if (!Directory.Exists(targetSubdir))
                {
                    Directory.CreateDirectory(targetSubdir);
                }

                // Recursively copy subdirectory
                await CopyDirectoryRecursiveAsync(subdir, targetSubdir, options, result);
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add($"Failed to process directory {subdir}: {ex.Message}");
                _logger.LogError(ex, "Failed to process directory: {DirectoryPath}", subdir);
            }
        }
    }
}
