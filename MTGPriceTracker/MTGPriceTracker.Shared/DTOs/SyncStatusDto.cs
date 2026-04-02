namespace MTGPriceTracker.Shared.DTOs;

public class SyncStatusDto
{
    public bool IsRunning { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public string? LastSyncResult { get; set; }
    public int TotalCards { get; set; }
    public int TotalPriceSnapshots { get; set; }
    public string? CurrentOperation { get; set; }
}

public class SyncTriggerResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
