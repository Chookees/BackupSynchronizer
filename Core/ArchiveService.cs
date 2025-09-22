using System.IO.Compression;
using Microsoft.Extensions.Logging;
using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public class ArchiveService : IArchiveService
{
    private readonly ILogger<ArchiveService> _logger;
    private readonly IFileFilterService _fileFilterService;
    private readonly ILoggingService _loggingService;

    public ArchiveService(
        ILogger<ArchiveService> logger,
        IFileFilterService fileFilterService,
        ILoggingService loggingService)
    {
        _logger = logger;
        _fileFilterService = fileFilterService;
        _loggingService = loggingService;
    }

    public async Task<ArchiveResult> CreateArchiveAsync(ArchiveOptions options)
    {
        _logger.LogInformation("Starting archive creation for: {SourcePath}", options.SourcePath);

        return options.ArchiveFormat.ToLower() switch
        {
            "zip" => await CreateZipArchiveAsync(options),
            _ => throw new ArgumentException($"Unsupported archive format: {options.ArchiveFormat}")
        };
    }

    public async Task<ArchiveResult> CreateZipArchiveAsync(ArchiveOptions options)
    {
        var result = new ArchiveResult
        {
            StartTime = DateTime.Now
        };

        try
        {
            // Validate source path
            if (!Directory.Exists(options.SourcePath))
            {
                throw new DirectoryNotFoundException($"Source directory does not exist: {options.SourcePath}");
            }

            // Determine output path
            if (string.IsNullOrEmpty(options.OutputPath))
            {
                var sourceName = Path.GetFileName(options.SourcePath);
                if (string.IsNullOrEmpty(sourceName))
                {
                    sourceName = "archive";
                }
                options.OutputPath = $"{sourceName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            }

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(options.OutputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Initialize logging
            if (options.CreateLogFile)
            {
                _loggingService.InitializeLogFile(options.LogFilePath);
            }

            _logger.LogInformation("Creating ZIP archive: {OutputPath} from {SourcePath}", 
                options.OutputPath, options.SourcePath);

            // Calculate original size
            result.OriginalSizeBytes = CalculateDirectorySize(options.SourcePath, options);

            // Create archive
            if (options.SplitSizeBytes.HasValue && options.SplitSizeBytes.Value > 0)
            {
                await CreateSplitZipArchiveAsync(options, result);
            }
            else
            {
                await CreateSingleZipArchiveAsync(options, result);
            }

            result.Success = true;
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;

            // Calculate compression ratio
            if (result.OriginalSizeBytes > 0)
            {
                result.CompressionRatio = (double)(result.OriginalSizeBytes - result.CompressedSizeBytes) / result.OriginalSizeBytes * 100;
            }

            _logger.LogInformation("Archive creation completed successfully. Files: {FilesArchived}, Size: {OriginalSize} -> {CompressedSize} ({CompressionRatio:F1}% compression)",
                result.FilesArchived, FormatBytes(result.OriginalSizeBytes), FormatBytes(result.CompressedSizeBytes), result.CompressionRatio);

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            result.Errors.Add(ex.Message);

            _logger.LogError(ex, "Archive creation failed: {Message}", ex.Message);
            throw;
        }
    }

    private async Task CreateSingleZipArchiveAsync(ArchiveOptions options, ArchiveResult result)
    {
        using var archive = new ZipArchive(File.Create(options.OutputPath), ZipArchiveMode.Create);
        
        await ArchiveDirectoryAsync(options.SourcePath, options.SourcePath, archive, options, result);
        
        result.ArchivePath = options.OutputPath;
        result.CreatedArchives.Add(options.OutputPath);
        
        // Calculate compressed size
        var fileInfo = new FileInfo(options.OutputPath);
        result.CompressedSizeBytes = fileInfo.Length;
    }

    private async Task CreateSplitZipArchiveAsync(ArchiveOptions options, ArchiveResult result)
    {
        var baseName = Path.GetFileNameWithoutExtension(options.OutputPath);
        var extension = Path.GetExtension(options.OutputPath);
        var outputDir = Path.GetDirectoryName(options.OutputPath) ?? ".";
        
        var splitIndex = 1;
        var currentSize = 0L;
        ZipArchive? currentArchive = null;
        string? currentArchivePath = null;

        try
        {
            var files = GetAllFiles(options.SourcePath, options).ToList();
            
            foreach (var file in files)
            {
                // Check if we need to create a new split
                if (currentArchive == null || currentSize >= options.SplitSizeBytes!.Value)
                {
                    // Close current archive if it exists
                    if (currentArchive != null)
                    {
                        currentArchive.Dispose();
                        if (!string.IsNullOrEmpty(currentArchivePath))
                        {
                            result.CreatedArchives.Add(currentArchivePath);
                        var archiveFileInfo = new FileInfo(currentArchivePath);
                        result.CompressedSizeBytes += archiveFileInfo.Length;
                        }
                    }

                    // Create new split archive
                    currentArchivePath = Path.Combine(outputDir, $"{baseName}.part{splitIndex:D3}{extension}");
                    currentArchive = new ZipArchive(File.Create(currentArchivePath), ZipArchiveMode.Create);
                    currentSize = 0;
                    splitIndex++;
                    
                    _logger.LogInformation("Creating split archive part {PartIndex}: {ArchivePath}", 
                        splitIndex - 1, currentArchivePath);
                }

                // Add file to current archive
                await AddFileToArchiveAsync(file, options.SourcePath, currentArchive, options, result);
                
                // Estimate current archive size (rough calculation)
                var fileInfo = new FileInfo(file);
                currentSize += fileInfo.Length;
            }

            // Close final archive
            if (currentArchive != null)
            {
                currentArchive.Dispose();
                if (!string.IsNullOrEmpty(currentArchivePath))
                {
                    result.CreatedArchives.Add(currentArchivePath);
                        var archiveFileInfo = new FileInfo(currentArchivePath);
                        result.CompressedSizeBytes += archiveFileInfo.Length;
                }
            }

            result.ArchivePath = result.CreatedArchives.FirstOrDefault() ?? string.Empty;
        }
        finally
        {
            currentArchive?.Dispose();
        }
    }

    private async Task ArchiveDirectoryAsync(string sourceDir, string rootPath, ZipArchive archive, ArchiveOptions options, ArchiveResult result)
    {
        // Add files in current directory
        var files = Directory.GetFiles(sourceDir);
        
        foreach (var file in files)
        {
            try
            {
                // Check if file should be included
                if (!_fileFilterService.ShouldIncludeFile(file, options.IncludePatterns, options.ExcludePatterns))
                {
                    result.SkippedFiles.Add(file);
                    continue;
                }

                await AddFileToArchiveAsync(file, rootPath, archive, options, result);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to archive {file}: {ex.Message}");
                _logger.LogError(ex, "Failed to archive file: {FilePath}", file);
            }
        }

        // Process subdirectories
        var subdirectories = Directory.GetDirectories(sourceDir);
        foreach (var subdir in subdirectories)
        {
            try
            {
                // Check if directory should be included
                if (!_fileFilterService.ShouldIncludeDirectory(subdir, options.IncludePatterns, options.ExcludePatterns))
                {
                    continue;
                }

                // Add empty directory if requested
                if (options.IncludeEmptyDirectories)
                {
                    var relativePath = Path.GetRelativePath(rootPath, subdir) + "/";
                    var entry = archive.CreateEntry(relativePath);
                    entry.LastWriteTime = Directory.GetLastWriteTime(subdir);
                    result.DirectoriesArchived++;
                }

                // Recursively archive subdirectory
                await ArchiveDirectoryAsync(subdir, rootPath, archive, options, result);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to archive directory {subdir}: {ex.Message}");
                _logger.LogError(ex, "Failed to archive directory: {DirectoryPath}", subdir);
            }
        }
    }

    private async Task AddFileToArchiveAsync(string filePath, string rootPath, ZipArchive archive, ArchiveOptions options, ArchiveResult result)
    {
        var relativePath = Path.GetRelativePath(rootPath, filePath);
        var entry = archive.CreateEntry(relativePath, GetCompressionLevel(options.CompressionLevel));
        
        entry.LastWriteTime = File.GetLastWriteTime(filePath);

        using var entryStream = entry.Open();
        using var fileStream = File.OpenRead(filePath);
        
        await fileStream.CopyToAsync(entryStream);
        
        result.FilesArchived++;
        
        _loggingService.LogFileOperation("Archiving", filePath, relativePath);
        Console.WriteLine($"Archiving: {relativePath}");
    }

    private long CalculateDirectorySize(string directory, ArchiveOptions options)
    {
        try
        {
            return GetAllFiles(directory, options).Sum(file => new FileInfo(file).Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate directory size: {Directory}", directory);
            return 0;
        }
    }

    private IEnumerable<string> GetAllFiles(string directory, ArchiveOptions options)
    {
        var files = new List<string>();
        
        try
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                if (_fileFilterService.ShouldIncludeFile(file, options.IncludePatterns, options.ExcludePatterns))
                {
                    files.Add(file);
                }
            }

            foreach (var subdir in Directory.GetDirectories(directory))
            {
                if (_fileFilterService.ShouldIncludeDirectory(subdir, options.IncludePatterns, options.ExcludePatterns))
                {
                    files.AddRange(GetAllFiles(subdir, options));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate files in directory: {Directory}", directory);
        }

        return files;
    }

    private System.IO.Compression.CompressionLevel GetCompressionLevel(Models.CompressionLevel level)
    {
        return level switch
        {
            Models.CompressionLevel.NoCompression => System.IO.Compression.CompressionLevel.NoCompression,
            Models.CompressionLevel.Fastest => System.IO.Compression.CompressionLevel.Fastest,
            Models.CompressionLevel.Optimal => System.IO.Compression.CompressionLevel.Optimal,
            Models.CompressionLevel.SmallestSize => System.IO.Compression.CompressionLevel.SmallestSize,
            _ => System.IO.Compression.CompressionLevel.Optimal
        };
    }

    private string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }

    public async Task<bool> ExtractArchiveAsync(string archivePath, string extractPath)
    {
        try
        {
            if (!File.Exists(archivePath))
            {
                _logger.LogError("Archive file not found: {ArchivePath}", archivePath);
                return false;
            }

            if (!Directory.Exists(extractPath))
            {
                Directory.CreateDirectory(extractPath);
            }

            using var archive = ZipFile.OpenRead(archivePath);
            
            foreach (var entry in archive.Entries)
            {
                var destinationPath = Path.Combine(extractPath, entry.FullName);
                
                // Ensure destination directory exists
                var destinationDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                // Skip directories
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                // Extract file
                entry.ExtractToFile(destinationPath, true);
                _logger.LogDebug("Extracted: {EntryName} -> {DestinationPath}", entry.FullName, destinationPath);
            }

            _logger.LogInformation("Successfully extracted archive: {ArchivePath} -> {ExtractPath}", archivePath, extractPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract archive: {ArchivePath}", archivePath);
            return false;
        }
    }

    public async Task<List<string>> ListArchiveContentsAsync(string archivePath)
    {
        var contents = new List<string>();
        
        try
        {
            if (!File.Exists(archivePath))
            {
                _logger.LogError("Archive file not found: {ArchivePath}", archivePath);
                return contents;
            }

            using var archive = ZipFile.OpenRead(archivePath);
            
            foreach (var entry in archive.Entries)
            {
                contents.Add(entry.FullName);
            }

            _logger.LogInformation("Listed {Count} entries in archive: {ArchivePath}", contents.Count, archivePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list archive contents: {ArchivePath}", archivePath);
        }

        return contents;
    }
}
