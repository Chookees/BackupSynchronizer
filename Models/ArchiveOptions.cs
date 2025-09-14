namespace BackupSynchronizer.Models;

public class ArchiveOptions
{
    public string SourcePath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;
    public long? SplitSizeBytes { get; set; } = null;
    public bool IncludeEmptyDirectories { get; set; } = true;
    public List<string> IncludePatterns { get; set; } = new();
    public List<string> ExcludePatterns { get; set; } = new();
    public bool PreserveDirectoryStructure { get; set; } = true;
    public string ArchiveFormat { get; set; } = "zip"; // zip, tar.gz, etc.
    public bool CreateLogFile { get; set; } = true;
    public string LogFilePath { get; set; } = "archive.log";
}

public enum CompressionLevel
{
    NoCompression,
    Fastest,
    Optimal,
    SmallestSize
}

public class ArchiveResult
{
    public bool Success { get; set; }
    public string ArchivePath { get; set; } = string.Empty;
    public List<string> CreatedArchives { get; set; } = new();
    public long OriginalSizeBytes { get; set; }
    public long CompressedSizeBytes { get; set; }
    public double CompressionRatio { get; set; }
    public int FilesArchived { get; set; }
    public int DirectoriesArchived { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> SkippedFiles { get; set; } = new();
}
