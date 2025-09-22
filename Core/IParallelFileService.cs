using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public interface IParallelFileService
{
    Task<ParallelFileResult> CopyFilesParallelAsync(string sourcePath, string targetPath, ParallelFileOptions options);
    Task<List<FileInfo>> ScanFilesAsync(string sourcePath, List<string> includePatterns, List<string> excludePatterns);
    Task CreateDirectoriesAsync(IEnumerable<string> directoryPaths);
    Task<ParallelFileResult> CopyFileAsync(FileCopyTask task, ParallelFileOptions options);
}

public class ParallelFileOptions
{
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    public bool CreateLogFile { get; set; } = true;
    public string LogFilePath { get; set; } = "parallel_copy.log";
    public bool ShowProgress { get; set; } = true;
    public int ProgressUpdateInterval { get; set; } = 10; // Update every N files
    public bool OverwriteExisting { get; set; } = true;
    public bool PreserveTimestamps { get; set; } = true;
    public bool PreserveAttributes { get; set; } = true;
    public List<string> IncludePatterns { get; set; } = new();
    public List<string> ExcludePatterns { get; set; } = new();
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
}

public class ParallelFileResult
{
    public bool Success { get; set; }
    public int TotalFiles { get; set; }
    public int FilesCopied { get; set; }
    public int FilesSkipped { get; set; }
    public int FilesFailed { get; set; }
    public long TotalBytesCopied { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public List<string> CopiedFiles { get; set; } = new();
    public List<string> SkippedFiles { get; set; } = new();
    public List<string> FailedFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public Dictionary<string, Exception> FileErrors { get; set; } = new();
}

public class FileCopyTask
{
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public FileInfo SourceFile { get; set; } = null!;
    public bool OverwriteExisting { get; set; } = true;
    public bool PreserveTimestamps { get; set; } = true;
    public bool PreserveAttributes { get; set; } = true;
}

public class ParallelProgressInfo
{
    private int _completedFiles;
    private long _completedBytes;
    private readonly object _lock = new();

    public int CompletedFiles
    {
        get { lock (_lock) { return _completedFiles; } }
    }

    public long CompletedBytes
    {
        get { lock (_lock) { return _completedBytes; } }
    }

    public void IncrementFiles()
    {
        lock (_lock)
        {
            _completedFiles++;
        }
    }

    public void IncrementBytes(long bytes)
    {
        lock (_lock)
        {
            _completedBytes += bytes;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _completedFiles = 0;
            _completedBytes = 0;
        }
    }
}
