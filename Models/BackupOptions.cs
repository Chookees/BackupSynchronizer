namespace BackupSynchronizer.Models;

public class BackupOptions
{
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string Mode { get; set; } = "simple";
    public string LogLevel { get; set; } = "info";
    public string ConfigFile { get; set; } = "config.json";
    public List<string> IncludePatterns { get; set; } = new();
    public List<string> ExcludePatterns { get; set; } = new();
    public bool PreserveFolderStructure { get; set; } = true;
    public bool OverwriteExisting { get; set; } = true;
    public bool CreateLogFile { get; set; } = true;
    public string LogFilePath { get; set; } = "backup.log";
    
    // Parallel file operations
    public int MaxThreads { get; set; } = Environment.ProcessorCount;
    public bool EnableParallelCopy { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    public int ProgressUpdateInterval { get; set; } = 10;
}
