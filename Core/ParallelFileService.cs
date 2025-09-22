using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public class ParallelFileService : IParallelFileService
{
    private readonly ILogger<ParallelFileService> _logger;
    private readonly IFileFilterService _fileFilterService;
    private readonly object _consoleLock = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _directorySemaphores = new();

    public ParallelFileService(ILogger<ParallelFileService> logger, IFileFilterService fileFilterService)
    {
        _logger = logger;
        _fileFilterService = fileFilterService;
    }

    public async Task<ParallelFileResult> CopyFilesParallelAsync(string sourcePath, string targetPath, ParallelFileOptions options)
    {
        var result = new ParallelFileResult
        {
            StartTime = DateTime.Now
        };

        try
        {
            _logger.LogInformation("Starting parallel file copy from {SourcePath} to {TargetPath}", sourcePath, targetPath);
            _logger.LogInformation("Max parallelism: {MaxParallelism}", options.MaxDegreeOfParallelism);

            // Step 1: Scan all files
            _logger.LogInformation("Scanning files in source directory...");
            var files = await ScanFilesAsync(sourcePath, options.IncludePatterns, options.ExcludePatterns);
            result.TotalFiles = files.Count;

            if (files.Count == 0)
            {
                _logger.LogInformation("No files found to copy");
                result.Success = true;
                return result;
            }

            _logger.LogInformation("Found {FileCount} files to copy", files.Count);

            // Step 2: Create file copy tasks
            var copyTasks = new List<FileCopyTask>();
            var totalSize = 0L;

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(sourcePath, file.FullName);
                var targetFilePath = Path.Combine(targetPath, relativePath);
                var targetDir = Path.GetDirectoryName(targetFilePath) ?? string.Empty;

                copyTasks.Add(new FileCopyTask
                {
                    SourcePath = file.FullName,
                    TargetPath = targetFilePath,
                    RelativePath = relativePath,
                    FileSize = file.Length,
                    SourceFile = file,
                    OverwriteExisting = options.OverwriteExisting,
                    PreserveTimestamps = options.PreserveTimestamps,
                    PreserveAttributes = options.PreserveAttributes
                });

                totalSize += file.Length;
            }

            _logger.LogInformation("Total size to copy: {TotalSize:F2} MB", totalSize / (1024.0 * 1024.0));

            // Step 3: Create all target directories
            var directories = copyTasks.Select(t => Path.GetDirectoryName(t.TargetPath)!).Distinct().ToList();
            await CreateDirectoriesAsync(directories);

            // Step 4: Copy files in parallel
            var progress = new ParallelProgressInfo();
            // Ensure MaxDegreeOfParallelism is valid
            var maxParallelism = Math.Max(1, options.MaxDegreeOfParallelism);
            
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = options.CancellationToken
            };

            var copiedFiles = new ConcurrentBag<string>();
            var skippedFiles = new ConcurrentBag<string>();
            var failedFiles = new ConcurrentBag<string>();
            var errors = new ConcurrentBag<string>();
            var fileErrors = new ConcurrentDictionary<string, Exception>();

            await Parallel.ForEachAsync(copyTasks, parallelOptions, async (task, ct) =>
            {
                try
                {
                    var copyResult = await CopyFileAsync(task, options);
                    
                    if (copyResult.Success)
                    {
                        copiedFiles.Add(task.RelativePath);
                        progress.IncrementFiles();
                        progress.IncrementBytes(task.FileSize);
                        
                        if (options.ShowProgress && progress.CompletedFiles % options.ProgressUpdateInterval == 0)
                        {
                            ShowProgress(progress.CompletedFiles, result.TotalFiles, progress.CompletedBytes, totalSize);
                        }
                    }
                    else
                    {
                        skippedFiles.Add(task.RelativePath);
                        if (!string.IsNullOrEmpty(copyResult.Errors.FirstOrDefault()))
                        {
                            errors.Add($"{task.RelativePath}: {copyResult.Errors.First()}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedFiles.Add(task.RelativePath);
                    fileErrors.TryAdd(task.RelativePath, ex);
                    errors.Add($"{task.RelativePath}: {ex.Message}");
                    _logger.LogError(ex, "Failed to copy file: {FilePath}", task.RelativePath);
                }
            });

            // Step 5: Collect results
            result.FilesCopied = copiedFiles.Count;
            result.FilesSkipped = skippedFiles.Count;
            result.FilesFailed = failedFiles.Count;
            result.TotalBytesCopied = progress.CompletedBytes;
            result.CopiedFiles = copiedFiles.ToList();
            result.SkippedFiles = skippedFiles.ToList();
            result.FailedFiles = failedFiles.ToList();
            result.Errors = errors.ToList();
            result.FileErrors = fileErrors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            result.Success = result.FilesFailed == 0;

            // Final progress update
            if (options.ShowProgress)
            {
                ShowProgress(result.FilesCopied, result.TotalFiles, result.TotalBytesCopied, totalSize);
                Console.WriteLine(); // New line after progress
            }

            _logger.LogInformation("Parallel copy completed. Files: {Copied}/{Total}, Bytes: {Bytes:F2} MB, Duration: {Duration}",
                result.FilesCopied, result.TotalFiles, result.TotalBytesCopied / (1024.0 * 1024.0), result.Duration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Parallel file copy failed");
            result.Success = false;
            result.Errors.Add($"Parallel copy failed: {ex.Message}");
        }
        finally
        {
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
        }

        return result;
    }

    public async Task<List<FileInfo>> ScanFilesAsync(string sourcePath, List<string> includePatterns, List<string> excludePatterns)
    {
        var files = new ConcurrentBag<FileInfo>();
        
        if (!Directory.Exists(sourcePath))
        {
            _logger.LogWarning("Source directory does not exist: {SourcePath}", sourcePath);
            return new List<FileInfo>();
        }

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        await Task.Run(() =>
        {
            Parallel.ForEach(
                Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories),
                parallelOptions,
                filePath =>
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (_fileFilterService.ShouldIncludeFile(filePath, includePatterns, excludePatterns))
                        {
                            files.Add(fileInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process file during scan: {FilePath}", filePath);
                    }
                });
        });

        return files.OrderBy(f => f.FullName).ToList();
    }

    public async Task CreateDirectoriesAsync(IEnumerable<string> directoryPaths)
    {
        var uniqueDirectories = directoryPaths
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct()
            .OrderBy(path => path.Length) // Create parent directories first
            .ToList();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        await Task.Run(() =>
        {
            Parallel.ForEach(uniqueDirectories, parallelOptions, directoryPath =>
            {
                try
                {
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                        _logger.LogDebug("Created directory: {DirectoryPath}", directoryPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create directory: {DirectoryPath}", directoryPath);
                }
            });
        });
    }

    public async Task<ParallelFileResult> CopyFileAsync(FileCopyTask task, ParallelFileOptions options)
    {
        var result = new ParallelFileResult();

        try
        {
            // Check if target file exists and handle accordingly
            if (File.Exists(task.TargetPath))
            {
                if (!task.OverwriteExisting)
                {
                    result.FilesSkipped = 1;
                    result.Success = true;
                    return result;
                }

                // Check if files are identical (same size and timestamp)
                var targetFile = new FileInfo(task.TargetPath);
                if (targetFile.Length == task.SourceFile.Length && 
                    Math.Abs((targetFile.LastWriteTimeUtc - task.SourceFile.LastWriteTimeUtc).TotalSeconds) < 1)
                {
                    result.FilesSkipped = 1;
                    result.Success = true;
                    return result;
                }
            }

            // Ensure target directory exists
            var targetDir = Path.GetDirectoryName(task.TargetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Copy the file
            await CopyFileWithProgressAsync(task.SourcePath, task.TargetPath, options.CancellationToken);

            // Preserve timestamps and attributes
            if (task.PreserveTimestamps)
            {
                File.SetCreationTimeUtc(task.TargetPath, task.SourceFile.CreationTimeUtc);
                File.SetLastWriteTimeUtc(task.TargetPath, task.SourceFile.LastWriteTimeUtc);
                File.SetLastAccessTimeUtc(task.TargetPath, task.SourceFile.LastAccessTimeUtc);
            }

            if (task.PreserveAttributes)
            {
                File.SetAttributes(task.TargetPath, task.SourceFile.Attributes);
            }

            result.FilesCopied = 1;
            result.TotalBytesCopied = task.FileSize;
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
            _logger.LogError(ex, "Failed to copy file: {SourcePath} -> {TargetPath}", task.SourcePath, task.TargetPath);
        }

        return result;
    }

    private async Task CopyFileWithProgressAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        const int bufferSize = 1024 * 1024; // 1MB buffer
        var buffer = new byte[bufferSize];

        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
        using var targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);

        int bytesRead;
        while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, bufferSize, cancellationToken)) > 0)
        {
            await targetStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
        }

        await targetStream.FlushAsync(cancellationToken);
    }

    private void ShowProgress(int completed, int total, long completedBytes, long totalBytes)
    {
        lock (_consoleLock)
        {
            var percentage = total > 0 ? (double)completed / total * 100 : 0;
            var bytesPercentage = totalBytes > 0 ? (double)completedBytes / totalBytes * 100 : 0;
            
            var completedMB = completedBytes / (1024.0 * 1024.0);
            var totalMB = totalBytes / (1024.0 * 1024.0);

            Console.Write($"\rProgress: {completed}/{total} files ({percentage:F1}%) | {completedMB:F1}/{totalMB:F1} MB ({bytesPercentage:F1}%)");
        }
    }
}
