using BackupSynchronizer.Core;
using BackupSynchronizer.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace BackupSynchronizer;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            // Build configuration
            var configuration = BuildConfiguration(args);
            
            // Setup dependency injection
            var services = new ServiceCollection();
            ConfigureServices(services, configuration);
            
            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            
            // Parse command line arguments
            var options = ParseCommandLineArgs(args, configuration);
            
            // Initialize database
            var historyService = serviceProvider.GetRequiredService<IFileHistoryService>();
            await historyService.InitializeDatabaseAsync();

            // Handle schedule operations
            if (options.CreateSchedule || options.DeleteSchedule || options.ListSchedules || options.ExecuteSchedule || 
                options.GenerateTaskScheduler || options.GenerateCron)
            {
                var scheduleService = serviceProvider.GetRequiredService<IScheduleService>();
                await HandleScheduleOperationsAsync(scheduleService, options);
                return 0;
            }

            // Handle archive operations
            if (!string.IsNullOrEmpty(options.ArchivePath) || options.ExtractArchive || options.ListArchive)
            {
                var archiveService = serviceProvider.GetRequiredService<IArchiveService>();
                await HandleArchiveOperationsAsync(archiveService, options);
                return 0;
            }

            // Handle restore operations
            if (!string.IsNullOrEmpty(options.RestoreFilePath) || options.ListDeleted || !string.IsNullOrEmpty(options.ListHistoryPath) || options.CleanupHistory)
            {
                var restoreService = serviceProvider.GetRequiredService<IRestoreService>();
                await HandleRestoreOperationsAsync(restoreService, options, serviceProvider);
                return 0;
            }

            // Choose service based on sync mode
            if (options.SyncMode == SyncMode.OneWay)
            {
                var backupService = serviceProvider.GetRequiredService<IBackupService>();
                logger.LogInformation("Starting backup process...");
                await backupService.RunBackupAsync(options);
                logger.LogInformation("Backup process completed successfully.");
            }
            else
            {
                var syncService = serviceProvider.GetRequiredService<ISyncService>();
                logger.LogInformation("Starting sync process...");
                var result = await syncService.RunSyncAsync(options);
                logger.LogInformation("Sync process completed successfully.");
                
                // Display sync results
                Console.WriteLine($"\nSync Results:");
                Console.WriteLine($"  Files synchronized: {result.FilesSynchronized}");
                Console.WriteLine($"  Conflicts detected: {result.ConflictsDetected}");
                Console.WriteLine($"  Files skipped (identical): {result.SkippedIdentical.Count}");
                Console.WriteLine($"  Errors: {result.Errors}");
                Console.WriteLine($"  Duration: {result.Duration}");
            }

            // Auto cleanup if enabled
            if (options.AutoCleanup && options.EnableHistoryTracking)
            {
                await historyService.CleanupExpiredHistoryAsync(options.HistoryRetentionDays);
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
    
    private static IConfiguration BuildConfiguration(string[] args)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("config.json", optional: true, reloadOnChange: true)
            .AddCommandLine(args);
            
        return builder.Build();
    }
    
    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddConfiguration(configuration.GetSection("Logging"));
        });
        
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<ISyncService, SyncService>();
        services.AddSingleton<IFileFilterService, FileFilterService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IFileComparisonService, FileComparisonService>();
        services.AddSingleton<IFileHistoryService, FileHistoryService>();
        services.AddSingleton<IRestoreService, RestoreService>();
        services.AddSingleton<IArchiveService, ArchiveService>();
        services.AddSingleton<IScheduleService, ScheduleService>();
        
        // Add Entity Framework
        services.AddDbContext<Data.FileHistoryContext>(options =>
            options.UseSqlite($"Data Source={configuration["DatabasePath"] ?? "file_history.db"}"));
    }
    
    private static SyncOptions ParseCommandLineArgs(string[] args, IConfiguration configuration)
    {
        var options = new SyncOptions();
        
        // Load from configuration first
        configuration.Bind(options);
        
        // Override with command line arguments
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--source":
                case "-s":
                    if (i + 1 < args.Length)
                        options.SourcePath = args[++i];
                    break;
                case "--target":
                case "-t":
                    if (i + 1 < args.Length)
                        options.TargetPath = args[++i];
                    break;
                case "--mode":
                case "-m":
                    if (i + 1 < args.Length)
                    {
                        var mode = args[++i].ToLower();
                        options.SyncMode = mode switch
                        {
                            "simple" => SyncMode.OneWay,
                            "sync" => SyncMode.Bidirectional,
                            _ => SyncMode.OneWay
                        };
                    }
                    break;
                case "--log-level":
                case "-l":
                    if (i + 1 < args.Length)
                        options.LogLevel = args[++i];
                    break;
                case "--config":
                case "-c":
                    if (i + 1 < args.Length)
                        options.ConfigFile = args[++i];
                    break;
                case "--delete-sync":
                    if (i + 1 < args.Length)
                        options.EnableDeletionSync = bool.Parse(args[++i]);
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--restore":
                    if (i + 1 < args.Length)
                        options.RestoreFilePath = args[++i];
                    break;
                case "--list-deleted":
                    options.ListDeleted = true;
                    break;
                case "--list-history":
                    if (i + 1 < args.Length)
                        options.ListHistoryPath = args[++i];
                    break;
                case "--history-keep-days":
                    if (i + 1 < args.Length)
                        options.HistoryRetentionDays = int.Parse(args[++i]);
                    break;
                case "--cleanup-history":
                    options.CleanupHistory = true;
                    break;
                case "--archive":
                    if (i + 1 < args.Length)
                        options.ArchivePath = args[++i];
                    break;
                case "--split-size":
                    if (i + 1 < args.Length)
                    {
                        var sizeStr = args[++i];
                        if (TryParseSize(sizeStr, out var size))
                            options.SplitSizeBytes = size;
                    }
                    break;
                case "--compression-level":
                    if (i + 1 < args.Length)
                        options.CompressionLevel = args[++i];
                    break;
                case "--extract":
                    options.ExtractArchive = true;
                    break;
                case "--list-archive":
                    options.ListArchive = true;
                    break;
                case "--schedule":
                    if (i + 1 < args.Length)
                        options.ScheduleType = args[++i];
                    break;
                case "--schedule-name":
                    if (i + 1 < args.Length)
                        options.ScheduleName = args[++i];
                    break;
                case "--create-schedule":
                    options.CreateSchedule = true;
                    break;
                case "--delete-schedule":
                    options.DeleteSchedule = true;
                    break;
                case "--list-schedules":
                    options.ListSchedules = true;
                    break;
                case "--execute-schedule":
                    options.ExecuteSchedule = true;
                    break;
                case "--generate-task-scheduler":
                    options.GenerateTaskScheduler = true;
                    break;
                case "--generate-cron":
                    options.GenerateCron = true;
                    break;
                case "--cron-expression":
                    if (i + 1 < args.Length)
                        options.CronExpression = args[++i];
                    break;
                case "--delete-source-after-archive":
                    options.DeleteSourceAfterArchive = true;
                    break;
                case "--no-timestamped-archives":
                    options.CreateTimestampedArchives = false;
                    break;
                case "--help":
                case "-h":
                    ShowHelp();
                    Environment.Exit(0);
                    break;
            }
        }
        
        return options;
    }
    
    private static bool TryParseSize(string sizeStr, out long size)
    {
        size = 0;
        
        if (string.IsNullOrEmpty(sizeStr))
            return false;
            
        sizeStr = sizeStr.Trim().ToUpper();
        
        try
        {
            if (sizeStr.EndsWith("KB"))
            {
                var value = long.Parse(sizeStr[..^2]);
                size = value * 1024;
                return true;
            }
            else if (sizeStr.EndsWith("MB"))
            {
                var value = long.Parse(sizeStr[..^2]);
                size = value * 1024 * 1024;
                return true;
            }
            else if (sizeStr.EndsWith("GB"))
            {
                var value = long.Parse(sizeStr[..^2]);
                size = value * 1024 * 1024 * 1024;
                return true;
            }
            else
            {
                size = long.Parse(sizeStr);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }
    
    private static async Task HandleRestoreOperationsAsync(IRestoreService restoreService, SyncOptions options, IServiceProvider serviceProvider)
    {
        if (options.CleanupHistory)
        {
            Console.WriteLine("Cleaning up expired history files...");
            var historyService = serviceProvider.GetRequiredService<IFileHistoryService>();
            await historyService.CleanupExpiredHistoryAsync(options.HistoryRetentionDays);
            Console.WriteLine("Cleanup completed.");
            return;
        }

        if (options.ListDeleted)
        {
            Console.WriteLine("Listing deleted files:");
            var deletedFiles = await restoreService.ListDeletedFilesAsync();
            
            if (deletedFiles.Count == 0)
            {
                Console.WriteLine("No deleted files found.");
                return;
            }

            Console.WriteLine($"Found {deletedFiles.Count} deleted files:");
            Console.WriteLine("Path | Deleted Date | Reason");
            Console.WriteLine(new string('-', 80));
            
            foreach (var file in deletedFiles)
            {
                Console.WriteLine($"{file.OriginalPath} | {file.Timestamp:yyyy-MM-dd HH:mm:ss} | {file.Reason}");
            }
            return;
        }

        if (!string.IsNullOrEmpty(options.ListHistoryPath))
        {
            Console.WriteLine($"Listing history for: {options.ListHistoryPath}");
            var history = await restoreService.ListFileHistoryAsync(options.ListHistoryPath);
            
            if (history.Count == 0)
            {
                Console.WriteLine("No history found for this file.");
                return;
            }

            Console.WriteLine($"Found {history.Count} history entries:");
            Console.WriteLine("Date | Type | Size | Hash");
            Console.WriteLine(new string('-', 80));
            
            foreach (var entry in history)
            {
                Console.WriteLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} | {entry.ChangeType} | {entry.FileSize} bytes | {entry.FileHash[..8]}...");
            }
            return;
        }

        if (!string.IsNullOrEmpty(options.RestoreFilePath))
        {
            Console.WriteLine($"Restoring file: {options.RestoreFilePath}");
            var result = await restoreService.RestoreFileAsync(new RestoreOptions
            {
                FilePath = options.RestoreFilePath
            });
            
            if (result.Success)
            {
                Console.WriteLine($"✓ {result.Message}");
                Console.WriteLine($"Restored from: {result.SourceHistoryPath}");
            }
            else
            {
                Console.WriteLine($"✗ {result.Message}");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"  Error: {error}");
                }
            }
        }
    }
    
    private static async Task HandleArchiveOperationsAsync(IArchiveService archiveService, SyncOptions options)
    {
        if (options.ExtractArchive)
        {
            if (string.IsNullOrEmpty(options.SourcePath))
            {
                Console.WriteLine("Error: Source path is required for extraction.");
                return;
            }
            
            Console.WriteLine($"Extracting archive: {options.SourcePath} -> {options.TargetPath}");
            var success = await archiveService.ExtractArchiveAsync(options.SourcePath, options.TargetPath);
            
            if (success)
            {
                Console.WriteLine("✓ Archive extracted successfully.");
            }
            else
            {
                Console.WriteLine("✗ Failed to extract archive.");
            }
            return;
        }

        if (options.ListArchive)
        {
            if (string.IsNullOrEmpty(options.SourcePath))
            {
                Console.WriteLine("Error: Archive path is required for listing.");
                return;
            }
            
            Console.WriteLine($"Listing archive contents: {options.SourcePath}");
            var contents = await archiveService.ListArchiveContentsAsync(options.SourcePath);
            
            if (contents.Count == 0)
            {
                Console.WriteLine("Archive is empty or could not be read.");
                return;
            }

            Console.WriteLine($"Found {contents.Count} entries:");
            foreach (var entry in contents.Take(50)) // Show first 50 entries
            {
                Console.WriteLine($"  {entry}");
            }
            
            if (contents.Count > 50)
            {
                Console.WriteLine($"  ... and {contents.Count - 50} more entries");
            }
            return;
        }

        if (!string.IsNullOrEmpty(options.ArchivePath))
        {
            if (string.IsNullOrEmpty(options.SourcePath))
            {
                Console.WriteLine("Error: Source path is required for archiving.");
                return;
            }

            Console.WriteLine($"Creating archive: {options.SourcePath} -> {options.ArchivePath}");
            
            var archiveOptions = new ArchiveOptions
            {
                SourcePath = options.SourcePath,
                OutputPath = options.ArchivePath,
                SplitSizeBytes = options.SplitSizeBytes,
                CompressionLevel = Enum.Parse<Models.CompressionLevel>(options.CompressionLevel),
                IncludePatterns = options.IncludePatterns,
                ExcludePatterns = options.ExcludePatterns,
                CreateLogFile = options.CreateLogFile,
                LogFilePath = options.LogFilePath
            };

            try
            {
                var result = await archiveService.CreateArchiveAsync(archiveOptions);
                
                if (result.Success)
                {
                    Console.WriteLine($"✓ Archive created successfully!");
                    Console.WriteLine($"  Files archived: {result.FilesArchived}");
                    Console.WriteLine($"  Directories archived: {result.DirectoriesArchived}");
                    Console.WriteLine($"  Original size: {FormatBytes(result.OriginalSizeBytes)}");
                    Console.WriteLine($"  Compressed size: {FormatBytes(result.CompressedSizeBytes)}");
                    Console.WriteLine($"  Compression ratio: {result.CompressionRatio:F1}%");
                    Console.WriteLine($"  Duration: {result.Duration}");
                    
                    if (result.CreatedArchives.Count > 1)
                    {
                        Console.WriteLine($"  Created {result.CreatedArchives.Count} split archives:");
                        foreach (var archive in result.CreatedArchives)
                        {
                            Console.WriteLine($"    {archive}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  Archive: {result.ArchivePath}");
                    }
                }
                else
                {
                    Console.WriteLine("✗ Archive creation failed.");
                    foreach (var error in result.Errors)
                    {
                        Console.WriteLine($"  Error: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Archive creation failed: {ex.Message}");
            }
        }
    }
    
    private static async Task HandleScheduleOperationsAsync(IScheduleService scheduleService, SyncOptions options)
    {
        if (options.ListSchedules)
        {
            Console.WriteLine("Listing all scheduled operations...");
            var schedules = await scheduleService.LoadSchedulesAsync("schedules.json");
            
            if (schedules.Count == 0)
            {
                Console.WriteLine("No schedules found.");
                return;
            }

            Console.WriteLine($"Found {schedules.Count} schedules:");
            foreach (var schedule in schedules)
            {
                Console.WriteLine($"  Name: {schedule.ScheduleName}");
                Console.WriteLine($"    Type: {schedule.ScheduleType}");
                Console.WriteLine($"    Source: {schedule.SourcePath}");
                Console.WriteLine($"    Target: {schedule.ArchivePath}");
                Console.WriteLine($"    Enabled: {schedule.Enabled}");
                Console.WriteLine($"    Next Run: {schedule.NextRun?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not scheduled"}");
                Console.WriteLine($"    Last Run: {schedule.LastRun?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"}");
                Console.WriteLine();
            }
            return;
        }

        if (options.DeleteSchedule)
        {
            if (string.IsNullOrEmpty(options.ScheduleName))
            {
                Console.WriteLine("Error: Schedule name is required for deletion.");
                return;
            }

            Console.WriteLine($"Deleting schedule: {options.ScheduleName}");
            var success = await scheduleService.DeleteScheduleAsync(options.ScheduleName);
            
            if (success)
            {
                Console.WriteLine("✓ Schedule deleted successfully.");
            }
            else
            {
                Console.WriteLine("✗ Failed to delete schedule or schedule not found.");
            }
            return;
        }

        if (options.ExecuteSchedule)
        {
            if (string.IsNullOrEmpty(options.ScheduleName))
            {
                Console.WriteLine("Error: Schedule name is required for execution.");
                return;
            }

            Console.WriteLine($"Executing schedule: {options.ScheduleName}");
            var schedule = await scheduleService.GetScheduleAsync(options.ScheduleName);
            
            if (schedule == null)
            {
                Console.WriteLine("✗ Schedule not found.");
                return;
            }

            ScheduleResult result;
            if (string.IsNullOrEmpty(options.ArchivePath))
            {
                // Execute as archive
                result = await scheduleService.ExecuteScheduledArchiveAsync(schedule);
            }
            else
            {
                // Execute as backup/sync based on mode
                if (options.SyncMode == SyncMode.OneWay)
                {
                    result = await scheduleService.ExecuteScheduledBackupAsync(schedule);
                }
                else
                {
                    result = await scheduleService.ExecuteScheduledSyncAsync(schedule);
                }
            }

            if (result.Success)
            {
                Console.WriteLine($"✓ Schedule executed successfully!");
                Console.WriteLine($"  Duration: {result.Duration}");
                Console.WriteLine($"  Files processed: {result.FilesArchived}");
                if (!string.IsNullOrEmpty(result.ArchivePath))
                {
                    Console.WriteLine($"  Archive: {result.ArchivePath}");
                }
                Console.WriteLine($"  Next run: {result.NextRun?.ToString("yyyy-MM-dd HH:mm:ss")}");
            }
            else
            {
                Console.WriteLine("✗ Schedule execution failed.");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"  Error: {error}");
                }
            }
            return;
        }

        if (options.GenerateTaskScheduler)
        {
            if (string.IsNullOrEmpty(options.ScheduleName))
            {
                Console.WriteLine("Error: Schedule name is required for Task Scheduler generation.");
                return;
            }

            var schedule = await scheduleService.GetScheduleAsync(options.ScheduleName);
            if (schedule == null)
            {
                Console.WriteLine("✗ Schedule not found.");
                return;
            }

            var executablePath = Environment.ProcessPath ?? "BackupSynchronizer.exe";
            var xml = await scheduleService.GenerateWindowsTaskSchedulerXmlAsync(schedule, executablePath);
            
            var fileName = $"task_scheduler_{schedule.ScheduleName}.xml";
            await File.WriteAllTextAsync(fileName, xml);
            
            Console.WriteLine($"✓ Windows Task Scheduler XML generated: {fileName}");
            Console.WriteLine($"  Import this file into Windows Task Scheduler to set up automatic execution.");
            return;
        }

        if (options.GenerateCron)
        {
            if (string.IsNullOrEmpty(options.ScheduleName))
            {
                Console.WriteLine("Error: Schedule name is required for cron generation.");
                return;
            }

            var schedule = await scheduleService.GetScheduleAsync(options.ScheduleName);
            if (schedule == null)
            {
                Console.WriteLine("✗ Schedule not found.");
                return;
            }

            var cronExpression = await scheduleService.GenerateCronExpressionAsync(schedule);
            
            Console.WriteLine($"✓ Cron expression generated for {schedule.ScheduleName}:");
            Console.WriteLine($"  {cronExpression}");
            Console.WriteLine($"  Add this to your crontab to set up automatic execution.");
            return;
        }

        if (options.CreateSchedule)
        {
            if (string.IsNullOrEmpty(options.ScheduleName))
            {
                Console.WriteLine("Error: Schedule name is required.");
                return;
            }

            if (string.IsNullOrEmpty(options.SourcePath))
            {
                Console.WriteLine("Error: Source path is required.");
                return;
            }

            var targetPath = !string.IsNullOrEmpty(options.ArchivePath) ? options.ArchivePath : options.TargetPath;
            if (string.IsNullOrEmpty(targetPath))
            {
                Console.WriteLine("Error: Target/Archive path is required.");
                return;
            }

            Console.WriteLine($"Creating schedule: {options.ScheduleName}");
            var schedule = await scheduleService.CreateScheduleAsync(
                options.ScheduleName, 
                options.ScheduleType, 
                options.SourcePath, 
                targetPath);

            // Set additional options
            schedule.CompressionLevel = options.CompressionLevel;
            schedule.SplitSizeBytes = options.SplitSizeBytes;
            schedule.DeleteSourceAfterArchive = options.DeleteSourceAfterArchive;
            schedule.CreateTimestampedArchives = options.CreateTimestampedArchives;
            schedule.IncludePatterns = options.IncludePatterns;
            schedule.ExcludePatterns = options.ExcludePatterns;

            if (!string.IsNullOrEmpty(options.CronExpression))
            {
                schedule.CronExpression = options.CronExpression;
            }

            // Validate schedule
            var isValid = await scheduleService.ValidateScheduleAsync(schedule);
            if (!isValid)
            {
                Console.WriteLine("✗ Schedule validation failed.");
                return;
            }

            // Save schedule
            var schedules = await scheduleService.LoadSchedulesAsync("schedules.json");
            schedules.Add(schedule);
            await scheduleService.SaveSchedulesAsync(schedules, "schedules.json");

            Console.WriteLine($"✓ Schedule created successfully!");
            Console.WriteLine($"  Name: {schedule.ScheduleName}");
            Console.WriteLine($"  Type: {schedule.ScheduleType}");
            Console.WriteLine($"  Source: {schedule.SourcePath}");
            Console.WriteLine($"  Target: {schedule.ArchivePath}");
            Console.WriteLine($"  Next Run: {schedule.NextRun?.ToString("yyyy-MM-dd HH:mm:ss")}");
            Console.WriteLine();
            Console.WriteLine("Use --generate-task-scheduler or --generate-cron to create OS scheduler integration.");
        }
    }
    
    private static string FormatBytes(long bytes)
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
    
    private static void ShowHelp()
    {
        Console.WriteLine("Backup Synchronizer - A backup and sync tool for .NET 8");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  BackupSynchronizer [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -s, --source <path>     Source directory");
        Console.WriteLine("  -t, --target <path>     Target directory");
        Console.WriteLine("  -m, --mode <mode>       Mode: simple (one-way) or sync (bidirectional)");
        Console.WriteLine("  -l, --log-level <level> Log level (info, warning, error)");
        Console.WriteLine("  -c, --config <file>     Configuration file path");
        Console.WriteLine("  --delete-sync <bool>    Enable deletion propagation (true/false)");
        Console.WriteLine("  --dry-run               Preview changes without making them");
        Console.WriteLine();
        Console.WriteLine("History & Restore Options:");
        Console.WriteLine("  --restore <filepath>    Restore a deleted file from history");
        Console.WriteLine("  --list-deleted          List all deleted files");
        Console.WriteLine("  --list-history <path>   List history for a specific file");
        Console.WriteLine("  --history-keep-days <n> Set history retention period (default: 30)");
        Console.WriteLine("  --cleanup-history       Clean up expired history files");
        Console.WriteLine();
        Console.WriteLine("Archive Options:");
        Console.WriteLine("  --archive <output>      Create ZIP archive from source directory");
        Console.WriteLine("  --split-size <size>     Split archive into parts (e.g., 100MB, 1GB)");
        Console.WriteLine("  --compression-level <level> Compression level (NoCompression, Fastest, Optimal, SmallestSize)");
        Console.WriteLine("  --extract               Extract archive to target directory");
        Console.WriteLine("  --list-archive          List contents of an archive");
        Console.WriteLine();
        Console.WriteLine("Schedule Options:");
        Console.WriteLine("  --schedule <type>       Schedule type (daily, weekly, monthly, custom)");
        Console.WriteLine("  --schedule-name <name>  Name for the schedule");
        Console.WriteLine("  --create-schedule       Create a new scheduled operation");
        Console.WriteLine("  --delete-schedule       Delete an existing schedule");
        Console.WriteLine("  --list-schedules        List all scheduled operations");
        Console.WriteLine("  --execute-schedule      Execute a scheduled operation");
        Console.WriteLine("  --generate-task-scheduler Generate Windows Task Scheduler XML");
        Console.WriteLine("  --generate-cron         Generate cron expression for Linux/macOS");
        Console.WriteLine("  --cron-expression <expr> Custom cron expression for advanced scheduling");
        Console.WriteLine("  --delete-source-after-archive Delete source after successful archive");
        Console.WriteLine("  --no-timestamped-archives Don't add timestamps to archive names");
        Console.WriteLine();
        Console.WriteLine("  -h, --help              Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  # One-way backup");
        Console.WriteLine("  BackupSynchronizer --source C:\\MyFiles --target D:\\Backup --mode simple");
        Console.WriteLine();
        Console.WriteLine("  # Bidirectional sync");
        Console.WriteLine("  BackupSynchronizer --source C:\\FolderA --target C:\\FolderB --mode sync");
        Console.WriteLine();
        Console.WriteLine("  # Dry-run preview");
        Console.WriteLine("  BackupSynchronizer --source C:\\FolderA --target C:\\FolderB --mode sync --dry-run");
        Console.WriteLine();
        Console.WriteLine("  # Restore deleted file");
        Console.WriteLine("  BackupSynchronizer --restore C:\\MyFiles\\document.txt");
        Console.WriteLine();
        Console.WriteLine("  # List deleted files");
        Console.WriteLine("  BackupSynchronizer --list-deleted");
        Console.WriteLine();
        Console.WriteLine("  # List file history");
        Console.WriteLine("  BackupSynchronizer --list-history C:\\MyFiles\\document.txt");
        Console.WriteLine();
        Console.WriteLine("  # Cleanup old history");
        Console.WriteLine("  BackupSynchronizer --cleanup-history --history-keep-days 7");
        Console.WriteLine();
        Console.WriteLine("  # Create ZIP archive");
        Console.WriteLine("  BackupSynchronizer --source C:\\MyFiles --archive C:\\Backup\\archive.zip");
        Console.WriteLine();
        Console.WriteLine("  # Create split archive");
        Console.WriteLine("  BackupSynchronizer --source C:\\LargeFolder --archive backup.zip --split-size 100MB");
        Console.WriteLine();
        Console.WriteLine("  # Extract archive");
        Console.WriteLine("  BackupSynchronizer --source archive.zip --target C:\\Extracted --extract");
        Console.WriteLine();
        Console.WriteLine("  # List archive contents");
        Console.WriteLine("  BackupSynchronizer --source archive.zip --list-archive");
        Console.WriteLine();
        Console.WriteLine("  # Create daily schedule");
        Console.WriteLine("  BackupSynchronizer --schedule daily --schedule-name \"Daily Backup\" --source C:\\MyFiles --target D:\\Backup --create-schedule");
        Console.WriteLine();
        Console.WriteLine("  # Create weekly archive schedule");
        Console.WriteLine("  BackupSynchronizer --schedule weekly --schedule-name \"Weekly Archive\" --source C:\\Data --archive C:\\Backup\\weekly.zip --create-schedule");
        Console.WriteLine();
        Console.WriteLine("  # List all schedules");
        Console.WriteLine("  BackupSynchronizer --list-schedules");
        Console.WriteLine();
        Console.WriteLine("  # Execute a schedule manually");
        Console.WriteLine("  BackupSynchronizer --schedule-name \"Daily Backup\" --execute-schedule");
        Console.WriteLine();
        Console.WriteLine("  # Generate Windows Task Scheduler XML");
        Console.WriteLine("  BackupSynchronizer --schedule-name \"Daily Backup\" --generate-task-scheduler");
        Console.WriteLine();
        Console.WriteLine("  # Generate cron expression");
        Console.WriteLine("  BackupSynchronizer --schedule-name \"Daily Backup\" --generate-cron");
        Console.WriteLine();
        Console.WriteLine("Configuration:");
        Console.WriteLine("  Create a config.json file to set default values.");
        Console.WriteLine("  Command line arguments override config file settings.");
    }
}
