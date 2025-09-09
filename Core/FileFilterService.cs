using System.Text.RegularExpressions;

namespace BackupSynchronizer.Core;

public class FileFilterService : IFileFilterService
{
    public bool ShouldIncludeFile(string filePath, List<string> includePatterns, List<string> excludePatterns)
    {
        var fileName = Path.GetFileName(filePath);
        var relativePath = filePath;

        // If no patterns are specified, include everything
        if (!includePatterns.Any() && !excludePatterns.Any())
        {
            return true;
        }

        // Check exclude patterns first (they have higher priority)
        foreach (var excludePattern in excludePatterns)
        {
            if (MatchesPattern(relativePath, excludePattern) || MatchesPattern(fileName, excludePattern))
            {
                return false;
            }
        }

        // If include patterns are specified, check if file matches any of them
        if (includePatterns.Any())
        {
            foreach (var includePattern in includePatterns)
            {
                if (MatchesPattern(relativePath, includePattern) || MatchesPattern(fileName, includePattern))
                {
                    return true;
                }
            }
            return false; // No include pattern matched
        }

        return true; // No exclude patterns matched and no include patterns specified
    }

    public bool ShouldIncludeDirectory(string directoryPath, List<string> includePatterns, List<string> excludePatterns)
    {
        var dirName = Path.GetDirectoryName(directoryPath) ?? string.Empty;
        var relativePath = directoryPath;

        // If no patterns are specified, include everything
        if (!includePatterns.Any() && !excludePatterns.Any())
        {
            return true;
        }

        // Check exclude patterns first
        foreach (var excludePattern in excludePatterns)
        {
            if (MatchesPattern(relativePath, excludePattern) || MatchesPattern(dirName, excludePattern))
            {
                return false;
            }
        }

        // If include patterns are specified, check if directory matches any of them
        if (includePatterns.Any())
        {
            foreach (var includePattern in includePatterns)
            {
                if (MatchesPattern(relativePath, includePattern) || MatchesPattern(dirName, includePattern))
                {
                    return true;
                }
            }
            return false; // No include pattern matched
        }

        return true; // No exclude patterns matched and no include patterns specified
    }

    private static bool MatchesPattern(string path, string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(path))
        {
            return false;
        }

        // Handle negation patterns (starting with !)
        var isNegation = pattern.StartsWith("!");
        var actualPattern = isNegation ? pattern.Substring(1) : pattern;

        // Convert wildcard pattern to regex
        var regexPattern = ConvertWildcardToRegex(actualPattern);
        
        try
        {
            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
            var matches = regex.IsMatch(path);
            return isNegation ? !matches : matches;
        }
        catch (Exception)
        {
            // If regex conversion fails, fall back to simple string matching
            return isNegation ? !path.Contains(actualPattern) : path.Contains(actualPattern);
        }
    }

    private static string ConvertWildcardToRegex(string pattern)
    {
        // Escape special regex characters except * and ?
        var escaped = Regex.Escape(pattern);
        
        // Convert wildcards to regex equivalents
        escaped = escaped.Replace("\\*", ".*");
        escaped = escaped.Replace("\\?", ".");
        
        // Ensure the pattern matches the entire string
        return $"^{escaped}$";
    }
}
