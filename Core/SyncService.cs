using BackupSynchronizer.Models;
using Microsoft.Extensions.Logging;

namespace BackupSynchronizer.Core;

public class SyncService : ISyncService
{
    private readonly ILogger<SyncService> _logger;
    private readonly IFileFilterService _fileFilterService;
    private readonly ILoggingService _loggingService;
    private readonly IFileComparisonService _fileComparisonService;
    private readonly IBackupService _backupService;

    public SyncService(
        ILogger<SyncService> logger,
        IFileFilterService fileFilterService,
        ILoggingService loggingService,
        IFileComparisonService fileComparisonService,
        IBackupService backupService)
    {
        _logger = logger;
        _fileFilterService = fileFilterService;
        _loggingService = loggingService;
        _fileComparisonService = fileComparisonService;
        _backupService = backupService;
    }

    public async Task<SyncResult> RunSyncAsync(SyncOptions options)
    {
        _logger.LogInformation("Starting sync process with mode: {Mode}", options.SyncMode);

        if (options.DryRun)
        {
            return await DryRunSyncAsync(options);
        }

        return options.SyncMode switch
        {
            SyncMode.OneWay => await RunOneWaySyncAsync(options),
            SyncMode.Bidirectional => await BidirectionalSyncAsync(options),
            _ => throw new ArgumentException($"Unknown sync mode: {options.SyncMode}")
        };
    }

    public async Task<SyncResult> BidirectionalSyncAsync(SyncOptions options)
    {
        var result = new SyncResult
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

            _logger.LogInformation("Starting bidirectional sync between {SourcePath} and {TargetPath}", 
                options.SourcePath, options.TargetPath);

            // Create conflict backup directory if needed
            if (options.CreateConflictBackups)
            {
                var conflictDir = Path.Combine(options.SourcePath, options.ConflictBackupDirectory);
                if (!Directory.Exists(conflictDir))
                {
                    Directory.CreateDirectory(conflictDir);
                }
            }

            // Sync files from source to target
            await SyncDirectoryRecursiveAsync(options.SourcePath, options.TargetPath, options, result, SyncDirection.SourceToTarget);
            
            // Sync files from target to source
            await SyncDirectoryRecursiveAsync(options.TargetPath, options.SourcePath, options, result, SyncDirection.TargetToSource);

            // Handle deletions if enabled
            if (options.EnableDeletionSync)
            {
                await HandleDeletionsAsync(options, result);
            }

            result.Success = true;
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;

            _logger.LogInformation("Bidirectional sync completed. Files synchronized: {FilesSynced}, Conflicts: {Conflicts}, Duration: {Duration}",
                result.FilesSynchronized, result.ConflictsDetected, result.Duration);

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            result.ErrorMessages.Add(ex.Message);

            _logger.LogError(ex, "Bidirectional sync failed: {Message}", ex.Message);
            throw;
        }
    }

    public async Task<SyncResult> DryRunSyncAsync(SyncOptions options)
    {
        var result = new SyncResult
        {
            StartTime = DateTime.Now
        };

        try
        {
            _logger.LogInformation("Starting dry-run sync simulation between {SourcePath} and {TargetPath}", 
                options.SourcePath, options.TargetPath);

            Console.WriteLine("=== DRY RUN MODE - No files will be modified ===");

            // Simulate sync operations
            await SimulateSyncDirectoryRecursiveAsync(options.SourcePath, options.TargetPath, options, result, SyncDirection.SourceToTarget);
            await SimulateSyncDirectoryRecursiveAsync(options.TargetPath, options.SourcePath, options, result, SyncDirection.TargetToSource);

            if (options.EnableDeletionSync)
            {
                await SimulateDeletionsAsync(options, result);
            }

            result.Success = true;
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;

            Console.WriteLine($"=== DRY RUN COMPLETE ===");
            Console.WriteLine($"Would synchronize: {result.FilesSynchronized} files");
            Console.WriteLine($"Would detect conflicts: {result.ConflictsDetected} files");
            Console.WriteLine($"Would delete: {result.FilesDeleted} files");

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            result.ErrorMessages.Add(ex.Message);

            _logger.LogError(ex, "Dry-run sync failed: {Message}", ex.Message);
            throw;
        }
    }

    private async Task<SyncResult> RunOneWaySyncAsync(SyncOptions options)
    {
        // Convert to backup options and use existing backup service
        var backupOptions = new BackupOptions
        {
            SourcePath = options.SourcePath,
            TargetPath = options.TargetPath,
            Mode = "simple",
            LogLevel = options.LogLevel,
            IncludePatterns = options.IncludePatterns,
            ExcludePatterns = options.ExcludePatterns,
            PreserveFolderStructure = options.PreserveFolderStructure,
            OverwriteExisting = options.OverwriteExisting,
            CreateLogFile = options.CreateLogFile,
            LogFilePath = options.LogFilePath
        };

        // Use existing backup service for one-way sync
        var backupResult = await _backupService.SimpleBackupAsync(backupOptions);

        // Convert to sync result
        return new SyncResult
        {
            Success = backupResult.Success,
            FilesCopied = backupResult.FilesCopied,
            FilesSkipped = backupResult.FilesSkipped,
            Errors = backupResult.Errors,
            CopiedFiles = backupResult.CopiedFiles,
            SkippedFiles = backupResult.SkippedFiles,
            ErrorMessages = backupResult.ErrorMessages,
            Duration = backupResult.Duration,
            StartTime = backupResult.StartTime,
            EndTime = backupResult.EndTime,
            FilesSynchronized = backupResult.FilesCopied
        };
    }

    private async Task SyncDirectoryRecursiveAsync(string sourceDir, string targetDir, SyncOptions options, SyncResult result, SyncDirection direction)
    {
        if (!Directory.Exists(sourceDir))
            return;

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
                    continue;
                }

                // Compare files
                var comparison = await _fileComparisonService.CompareFilesAsync(file, targetFilePath);

                if (comparison.AreIdentical)
                {
                    result.SkippedIdentical.Add(file);
                    continue;
                }

                if (comparison.HasConflict)
                {
                    await HandleConflictAsync(file, targetFilePath, options, result);
                    continue;
                }

                // Determine which file is newer and sync accordingly
                bool shouldCopySourceToTarget = direction == SyncDirection.SourceToTarget && 
                    (comparison.SourceIsNewer || !File.Exists(targetFilePath));
                bool shouldCopyTargetToSource = direction == SyncDirection.TargetToSource && 
                    (comparison.TargetIsNewer || !File.Exists(file));

                if (shouldCopySourceToTarget)
                {
                    await CopyFileAsync(file, targetFilePath, options, result);
                }
                else if (shouldCopyTargetToSource)
                {
                    await CopyFileAsync(targetFilePath, file, options, result);
                }
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add($"Failed to sync {file}: {ex.Message}");
                _logger.LogError(ex, "Failed to sync file: {FilePath}", file);
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
                    continue;
                }

                // Create target subdirectory if it doesn't exist
                if (!Directory.Exists(targetSubdir))
                {
                    Directory.CreateDirectory(targetSubdir);
                }

                // Recursively sync subdirectory
                await SyncDirectoryRecursiveAsync(subdir, targetSubdir, options, result, direction);
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add($"Failed to process directory {subdir}: {ex.Message}");
                _logger.LogError(ex, "Failed to process directory: {DirectoryPath}", subdir);
            }
        }
    }

    private async Task SimulateSyncDirectoryRecursiveAsync(string sourceDir, string targetDir, SyncOptions options, SyncResult result, SyncDirection direction)
    {
        if (!Directory.Exists(sourceDir))
            return;

        var files = Directory.GetFiles(sourceDir);
        
        foreach (var file in files)
        {
            try
            {
                var fileName = Path.GetFileName(file);
                var targetFilePath = Path.Combine(targetDir, fileName);

                if (!_fileFilterService.ShouldIncludeFile(file, options.IncludePatterns, options.ExcludePatterns))
                {
                    result.SkippedFiles.Add(file);
                    result.FilesSkipped++;
                    continue;
                }

                var comparison = await _fileComparisonService.CompareFilesAsync(file, targetFilePath);

                if (comparison.AreIdentical)
                {
                    result.SkippedIdentical.Add(file);
                    continue;
                }

                if (comparison.HasConflict)
                {
                    result.ConflictsDetected++;
                    result.Conflicts.Add(new ConflictInfo
                    {
                        FilePath = file,
                        SourcePath = file,
                        TargetPath = targetFilePath,
                        SourceModified = comparison.SourceModified,
                        TargetModified = comparison.TargetModified,
                        SourceHash = comparison.SourceHash,
                        TargetHash = comparison.TargetHash
                    });
                    
                    Console.WriteLine($"Would detect conflict: {file}");
                    continue;
                }

                bool shouldCopySourceToTarget = direction == SyncDirection.SourceToTarget && 
                    (comparison.SourceIsNewer || !File.Exists(targetFilePath));
                bool shouldCopyTargetToSource = direction == SyncDirection.TargetToSource && 
                    (comparison.TargetIsNewer || !File.Exists(file));

                if (shouldCopySourceToTarget)
                {
                    result.FilesSynchronized++;
                    Console.WriteLine($"Would copy: {file} -> {targetFilePath}");
                }
                else if (shouldCopyTargetToSource)
                {
                    result.FilesSynchronized++;
                    Console.WriteLine($"Would copy: {targetFilePath} -> {file}");
                }
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add($"Failed to simulate sync {file}: {ex.Message}");
                _logger.LogError(ex, "Failed to simulate sync file: {FilePath}", file);
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

                if (!_fileFilterService.ShouldIncludeDirectory(subdir, options.IncludePatterns, options.ExcludePatterns))
                {
                    continue;
                }

                await SimulateSyncDirectoryRecursiveAsync(subdir, targetSubdir, options, result, direction);
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add($"Failed to simulate process directory {subdir}: {ex.Message}");
                _logger.LogError(ex, "Failed to simulate process directory: {DirectoryPath}", subdir);
            }
        }
    }

    private async Task CopyFileAsync(string sourcePath, string targetPath, SyncOptions options, SyncResult result)
    {
        try
        {
            // Ensure target directory exists
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            File.Copy(sourcePath, targetPath, options.OverwriteExisting);
            
            result.CopiedFiles.Add(sourcePath);
            result.FilesSynchronized++;
            
            _loggingService.LogFileOperation("Synchronizing", sourcePath, targetPath);
            Console.WriteLine($"Synchronizing file {sourcePath} to {targetPath}");
        }
        catch (Exception ex)
        {
            result.Errors++;
            result.ErrorMessages.Add($"Failed to copy {sourcePath}: {ex.Message}");
            _logger.LogError(ex, "Failed to copy file: {FilePath}", sourcePath);
        }
    }

    private async Task HandleConflictAsync(string sourcePath, string targetPath, SyncOptions options, SyncResult result)
    {
        result.ConflictsDetected++;
        
        var conflict = new ConflictInfo
        {
            FilePath = sourcePath,
            SourcePath = sourcePath,
            TargetPath = targetPath,
            ConflictDetectedAt = DateTime.Now
        };

        try
        {
            var comparison = await _fileComparisonService.CompareFilesAsync(sourcePath, targetPath);
            conflict.SourceModified = comparison.SourceModified;
            conflict.TargetModified = comparison.TargetModified;
            conflict.SourceHash = comparison.SourceHash;
            conflict.TargetHash = comparison.TargetHash;

            if (options.CreateConflictBackups)
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = Path.GetFileName(sourcePath);
                var conflictBackupPath = Path.Combine(options.SourcePath, options.ConflictBackupDirectory, $"{timestamp}_{fileName}");
                
                // Backup the target file (the one that would be overwritten)
                if (File.Exists(targetPath))
                {
                    File.Copy(targetPath, conflictBackupPath, true);
                    conflict.ConflictBackupPath = conflictBackupPath;
                }
            }

            result.Conflicts.Add(conflict);
            
            _loggingService.LogError($"Conflict detected: {sourcePath}");
            Console.WriteLine($"Conflict detected: {sourcePath} - both files have been modified");
        }
        catch (Exception ex)
        {
            result.Errors++;
            result.ErrorMessages.Add($"Failed to handle conflict for {sourcePath}: {ex.Message}");
            _logger.LogError(ex, "Failed to handle conflict: {FilePath}", sourcePath);
        }
    }

    private Task HandleDeletionsAsync(SyncOptions options, SyncResult result)
    {
        // This is a simplified implementation
        // In a real scenario, you'd want to track what files existed during the last sync
        _logger.LogInformation("Deletion sync is enabled but not fully implemented in this version");
        return Task.CompletedTask;
    }

    private Task SimulateDeletionsAsync(SyncOptions options, SyncResult result)
    {
        Console.WriteLine("Would check for deletions (feature not fully implemented)");
        return Task.CompletedTask;
    }
}

public enum SyncDirection
{
    SourceToTarget,
    TargetToSource
}
