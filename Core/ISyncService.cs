using BackupSynchronizer.Models;

namespace BackupSynchronizer.Core;

public interface ISyncService
{
    Task<SyncResult> RunSyncAsync(SyncOptions options);
    Task<SyncResult> BidirectionalSyncAsync(SyncOptions options);
    Task<SyncResult> DryRunSyncAsync(SyncOptions options);
}
