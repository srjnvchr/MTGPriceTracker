namespace MTGPriceTracker.Server.BackgroundServices;

/// <summary>
/// Singleton that holds the current sync state, shared between background service and controllers.
/// </summary>
public class SyncState
{
    public bool IsRunning { get; set; }
    public string CurrentOperation { get; set; } = string.Empty;
    public DateTime? LastSyncAt { get; set; }
    public string? LastSyncResult { get; set; }
}
