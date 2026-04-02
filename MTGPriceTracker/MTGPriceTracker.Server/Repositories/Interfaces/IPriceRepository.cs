using MTGPriceTracker.Server.Models;

namespace MTGPriceTracker.Server.Repositories.Interfaces;

public interface IPriceRepository
{
    Task<IEnumerable<PriceSnapshot>> GetHistoryAsync(string cardUuid, string? vendor = null, int days = 90, CancellationToken ct = default);
    Task<Dictionary<string, decimal>> GetLatestPricesAsync(string cardUuid, CancellationToken ct = default);
    Task<Dictionary<string, Dictionary<string, decimal>>> GetLatestPricesForCardsAsync(IEnumerable<string> cardUuids, CancellationToken ct = default);
    Task BulkUpsertAsync(IEnumerable<PriceSnapshot> snapshots, CancellationToken ct = default);
    Task<int> GetTotalCountAsync(CancellationToken ct = default);
    Task<DateTime?> GetLastSyncDateAsync(string vendor, CancellationToken ct = default);
}
