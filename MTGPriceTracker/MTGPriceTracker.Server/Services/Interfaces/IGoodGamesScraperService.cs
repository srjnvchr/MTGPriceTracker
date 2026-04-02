namespace MTGPriceTracker.Server.Services.Interfaces;

public interface IGoodGamesScraperService
{
    /// <summary>
    /// Scrapes Good Games AU for the current price of a specific card.
    /// Returns null if the card was not found or the site is unreachable.
    /// </summary>
    Task<decimal?> GetCardPriceAsync(string cardName, CancellationToken ct = default);

    /// <summary>
    /// Scrapes prices for all cards in the database and stores them as price snapshots.
    /// Rate-limited to be polite to the server.
    /// </summary>
    Task<int> ScrapeAllPricesAsync(IProgress<string>? progress = null, CancellationToken ct = default);
}
