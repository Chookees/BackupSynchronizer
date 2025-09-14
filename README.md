# Backup Synchronizer

A comprehensive backup and synchronization tool for .NET 8 that provides both one-way backup and bidirectional file synchronization with advanced conflict resolution.

## Features

### Core Features
- **Simple Backup**: One-way synchronization (Source → Target)
- **Bidirectional Sync**: Two-way synchronization with conflict detection
- **File Filtering**: Include/exclude specific file types and folders using wildcard patterns
- **Logging**: Detailed logging with configurable log levels
- **CLI Interface**: Command-line interface with help text
- **Configuration**: JSON configuration file support with CLI override

### Advanced Features
- **Conflict Detection**: Automatically detects when both files have been modified
- **Conflict Resolution**: Creates timestamped backups of conflicting files
- **Dry-Run Mode**: Preview changes without making any modifications
- **File Comparison**: Uses both timestamp and hash comparison for accurate sync decisions
- **Deletion Propagation**: Optional deletion sync (experimental)
- **File History Tracking**: Complete version history with SQLite database
- **Restore Functionality**: Restore deleted files from history
- **Automatic Cleanup**: Configurable retention period for history files
- **ZIP Archiving**: Create compressed archives with folder structure preservation
- **Split Archives**: Create multi-part archives for large datasets
- **Archive Extraction**: Extract and list archive contents

## Installation

1. Ensure you have .NET 8 SDK installed
2. Clone or download this project
3. Build the project:
   ```bash
   dotnet build
   ```

## Usage

### Basic Usage

```bash
# Simple one-way backup
BackupSynchronizer --source "C:\MyFiles" --target "D:\Backup" --mode simple

# Bidirectional sync
BackupSynchronizer --source "C:\FolderA" --target "C:\FolderB" --mode sync

# Dry-run preview
BackupSynchronizer --source "C:\FolderA" --target "C:\FolderB" --mode sync --dry-run
```

### Command Line Options

- `-s, --source <path>`: Source directory
- `-t, --target <path>`: Target directory
- `-m, --mode <mode>`: Mode: "simple" (one-way) or "sync" (bidirectional)
- `-l, --log-level <level>`: Log level (info, warning, error)
- `-c, --config <file>`: Configuration file path
- `--delete-sync <bool>`: Enable deletion propagation (true/false)
- `--dry-run`: Preview changes without making them
- `--restore <filepath>`: Restore a deleted file from history
- `--list-deleted`: List all deleted files
- `--list-history <path>`: List history for a specific file
- `--history-keep-days <n>`: Set history retention period (default: 30)
- `--cleanup-history`: Clean up expired history files
- `--archive <output>`: Create ZIP archive from source directory
- `--split-size <size>`: Split archive into parts (e.g., 100MB, 1GB)
- `--compression-level <level>`: Compression level (NoCompression, Fastest, Optimal, SmallestSize)
- `--extract`: Extract archive to target directory
- `--list-archive`: List contents of an archive
- `-h, --help`: Show help message

### Configuration File

Create a `config.json` file in the same directory as the executable:

```json
{
  "SourcePath": "C:\\MyFiles",
  "TargetPath": "D:\\Backup",
  "Mode": "simple",
  "SyncMode": "OneWay",
  "LogLevel": "info",
  "IncludePatterns": [],
  "ExcludePatterns": [
    "*.tmp",
    "*.log",
    "node_modules",
    ".git",
    ".vs",
    "bin",
    "obj"
  ],
  "PreserveFolderStructure": true,
  "OverwriteExisting": true,
  "CreateLogFile": true,
  "LogFilePath": "backup.log",
  "EnableDeletionSync": false,
  "DryRun": false,
  "UseHashComparison": true,
  "CreateConflictBackups": true,
  "ConflictBackupDirectory": "conflicts",
  "EnableHistoryTracking": true,
  "HistoryRetentionDays": 30,
  "HistoryDirectory": ".history",
  "AutoCleanup": true,
  "DatabasePath": "file_history.db",
  "ArchivePath": "",
  "SplitSizeBytes": null,
  "CompressionLevel": "Optimal",
  "ExtractArchive": false,
  "ListArchive": false
}
```

### Filter Patterns

The tool supports wildcard patterns for including and excluding files:

- `*.*`: Include all files
- `*.txt`: Include only .txt files
- `!*.tmp`: Exclude .tmp files (negation with !)
- `!node_modules`: Exclude node_modules directory
- `*.{jpg,png,gif}`: Include specific file extensions

**Priority**: Exclude patterns are checked first, then include patterns. If no include patterns are specified, all files (except excluded) are included.

## Examples

### Example 1: One-Way Backup
```bash
BackupSynchronizer --source "C:\Documents" --target "D:\Backup\Documents" --mode simple
```

### Example 2: Bidirectional Sync
```bash
BackupSynchronizer --source "C:\FolderA" --target "C:\FolderB" --mode sync
```

### Example 3: Dry-Run Preview
```bash
BackupSynchronizer --source "C:\FolderA" --target "C:\FolderB" --mode sync --dry-run
```

### Example 4: Sync with Deletion Propagation
```bash
BackupSynchronizer --source "C:\FolderA" --target "C:\FolderB" --mode sync --delete-sync true
```

### Example 5: Restore Deleted File
```bash
BackupSynchronizer --restore "C:\MyFiles\document.txt"
```

### Example 6: List Deleted Files
```bash
BackupSynchronizer --list-deleted
```

### Example 7: List File History
```bash
BackupSynchronizer --list-history "C:\MyFiles\document.txt"
```

### Example 8: Cleanup Old History
```bash
BackupSynchronizer --cleanup-history --history-keep-days 7
```

### Example 9: Create ZIP Archive
```bash
BackupSynchronizer --source C:\MyFiles --archive C:\Backup\archive.zip
```

### Example 10: Create Split Archive
```bash
BackupSynchronizer --source C:\LargeFolder --archive backup.zip --split-size 100MB
```

### Example 11: Extract Archive
```bash
BackupSynchronizer --source archive.zip --target C:\Extracted --extract
```

### Example 12: List Archive Contents
```bash
BackupSynchronizer --source archive.zip --list-archive
```

### Example 13: Using Configuration File
```bash
# Create config.json with your settings, then run:
BackupSynchronizer
```

## Sync Modes

### One-Way Backup (Simple Mode)
- Copies files from source to target
- Maintains folder structure
- Supports file filtering
- Overwrites existing files by default

### Bidirectional Sync
- Synchronizes files in both directions
- Compares files using timestamps and hashes
- Detects conflicts when both files have been modified
- Creates timestamped backups of conflicting files
- Skips identical files to improve performance

### Conflict Resolution
When conflicts are detected:
1. Both versions are backed up with timestamps
2. Conflict is logged with details
3. User can manually resolve conflicts later
4. Sync continues with other files

## Logging

The tool provides comprehensive logging:

1. **Console Output**: Real-time feedback showing operations
2. **Log File**: Detailed log file (default: `backup.log`) with timestamps
3. **Conflict Logs**: Special logging for conflict detection and resolution

Log levels:
- `info`: All operations and information
- `warning`: Warnings and errors only
- `error`: Errors only

## Error Handling

The tool handles various error scenarios:
- Missing source/target directories
- Permission issues
- File access conflicts
- Invalid configuration
- Network issues (for network paths)

Errors are logged and the sync process continues with other files.

## File History & Versioning

### History Tracking
The tool automatically tracks all file changes in a SQLite database (`file_history.db`):

- **File Modifications**: When files are overwritten during backup/sync
- **File Deletions**: When files are removed (if deletion sync is enabled)
- **Metadata Storage**: File path, hash, change type, timestamp, and original location
- **History Structure**: Files are moved to `.history/<relative_path>/<timestamp>_filename.ext`

### Restore Operations
- **Restore Files**: `--restore <filepath>` restores the latest version of a file
- **List Deleted**: `--list-deleted` shows all deleted files with timestamps
- **List History**: `--list-history <path>` shows complete history for a specific file
- **Restore to Date**: Restore files to a specific point in time

### Automatic Cleanup
- **Retention Period**: Configurable via `--history-keep-days` (default: 30 days)
- **Auto Cleanup**: Automatically removes expired history during sync operations
- **Manual Cleanup**: `--cleanup-history` manually removes old history files
- **Database Maintenance**: Removes database entries for expired files

### History Database Schema
The SQLite database tracks:
- File path and original path
- SHA256 file hash for integrity verification
- Change type (Modified, Deleted, Created, etc.)
- Timestamp of the change
- File size and reason for the change
- Physical location of the history file

## Archiving Features

### ZIP Archive Creation
The tool can create compressed ZIP archives with comprehensive features:

- **Folder Structure Preservation**: Maintains complete directory hierarchy
- **File Filtering**: Uses same include/exclude patterns as backup/sync operations
- **Compression Levels**: NoCompression, Fastest, Optimal, SmallestSize
- **Progress Tracking**: Real-time progress with file counts and compression ratios

### Split Archives
For large datasets, archives can be split into multiple parts:

- **Size-Based Splitting**: Specify maximum size per part (KB, MB, GB)
- **Automatic Naming**: Parts named as `archive.part001.zip`, `archive.part002.zip`, etc.
- **Transparent Handling**: Each part is a complete, valid ZIP archive

### Archive Operations
- **Create**: `--archive <output>` creates ZIP from source directory
- **Extract**: `--extract` extracts archive to target directory
- **List**: `--list-archive` shows all files and folders in archive
- **Compression**: `--compression-level` controls compression algorithm

### Archive Features
- **Metadata Preservation**: File timestamps and attributes maintained
- **Error Handling**: Graceful handling of file access issues
- **Logging**: Detailed logging of archive operations
- **Size Reporting**: Original vs compressed size with compression ratios

## Building from Source

```bash
# Clone the repository
git clone <repository-url>
cd BackupSynchronizer

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run -- --help
```

## License

This project is open source and available under the MIT License.
