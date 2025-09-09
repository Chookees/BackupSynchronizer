using System.Security.Cryptography;
using BackupSynchronizer.Models;
using Microsoft.Extensions.Logging;

namespace BackupSynchronizer.Core;

public class FileComparisonService : IFileComparisonService
{
    private readonly ILogger<FileComparisonService> _logger;

    public FileComparisonService(ILogger<FileComparisonService> logger)
    {
        _logger = logger;
    }

    public async Task<string> CalculateFileHashAsync(string filePath)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await Task.Run(() => sha256.ComputeHash(stream));
            return Convert.ToHexString(hash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate hash for file: {FilePath}", filePath);
            return string.Empty;
        }
    }

    public async Task<FileComparisonResult> CompareFilesAsync(string sourcePath, string targetPath)
    {
        var result = new FileComparisonResult();

        try
        {
            // Check if both files exist
            if (!File.Exists(sourcePath) || !File.Exists(targetPath))
            {
                result.AreIdentical = false;
                result.SourceIsNewer = File.Exists(sourcePath) && !File.Exists(targetPath);
                result.TargetIsNewer = File.Exists(targetPath) && !File.Exists(sourcePath);
                return result;
            }

            // Get file modification times
            var sourceInfo = new FileInfo(sourcePath);
            var targetInfo = new FileInfo(targetPath);
            
            result.SourceModified = sourceInfo.LastWriteTimeUtc;
            result.TargetModified = targetInfo.LastWriteTimeUtc;

            // Compare modification times first (faster)
            if (result.SourceModified == result.TargetModified)
            {
                // Same modification time, check file size
                if (sourceInfo.Length == targetInfo.Length)
                {
                    // Same size and time, check hash for certainty
                    result.SourceHash = await CalculateFileHashAsync(sourcePath);
                    result.TargetHash = await CalculateFileHashAsync(targetPath);
                    result.AreIdentical = result.SourceHash == result.TargetHash;
                }
                else
                {
                    result.AreIdentical = false;
                }
            }
            else
            {
                result.AreIdentical = false;
                result.SourceIsNewer = result.SourceModified > result.TargetModified;
                result.TargetIsNewer = result.TargetModified > result.SourceModified;
            }

            // If files are different, calculate hashes to detect conflicts
            if (!result.AreIdentical)
            {
                if (string.IsNullOrEmpty(result.SourceHash))
                    result.SourceHash = await CalculateFileHashAsync(sourcePath);
                if (string.IsNullOrEmpty(result.TargetHash))
                    result.TargetHash = await CalculateFileHashAsync(targetPath);

                // Check for conflict: both files have been modified since last sync
                // This is a simple heuristic - in a real scenario, you might want to track sync history
                var timeDifference = Math.Abs((result.SourceModified - result.TargetModified).TotalMinutes);
                result.HasConflict = timeDifference < 1 && result.SourceHash != result.TargetHash;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compare files: {SourcePath} vs {TargetPath}", sourcePath, targetPath);
            result.AreIdentical = false;
            return result;
        }
    }

    public bool IsFileNewer(string sourcePath, string targetPath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return false;
            if (!File.Exists(targetPath))
                return true;

            var sourceInfo = new FileInfo(sourcePath);
            var targetInfo = new FileInfo(targetPath);
            
            return sourceInfo.LastWriteTimeUtc > targetInfo.LastWriteTimeUtc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compare file timestamps: {SourcePath} vs {TargetPath}", sourcePath, targetPath);
            return false;
        }
    }

    public async Task<bool> AreFilesIdenticalAsync(string sourcePath, string targetPath)
    {
        var comparison = await CompareFilesAsync(sourcePath, targetPath);
        return comparison.AreIdentical;
    }
}
