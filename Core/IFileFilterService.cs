namespace BackupSynchronizer.Core;

public interface IFileFilterService
{
    bool ShouldIncludeFile(string filePath, List<string> includePatterns, List<string> excludePatterns);
    bool ShouldIncludeDirectory(string directoryPath, List<string> includePatterns, List<string> excludePatterns);
}
