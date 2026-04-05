namespace MTGPriceTracker.Server.Services.Interfaces;

public interface IMtgJsonService
{
    /// <summary>Downloads and imports card catalog from AllPrintings.json.</summary>
    Task<(int setsImported, int cardsImported)> ImportAllPrintingsAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>Downloads and imports latest price data from AllPricesToday.json (daily incremental).</summary>
    Task<int> ImportAllPricesAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// One-time backfill: downloads AllPrices.json (90-day history) and imports all date entries.
    /// Skips if history already exists (more than 1 distinct date in the price table).
    /// Uses a temp file on disk to avoid the in-memory buffer overflow from the large JSON.
    /// </summary>
    Task<int> ImportAllPricesHistoryAsync(IProgress<string>? progress = null, CancellationToken ct = default);
}
