using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Services.Interfaces;

public interface IGoodGamesScraperService
{
    /// <summary>
    /// Returns all available condition→price pairs for a card name by fetching the
    /// Shopify product JSON directly. Keys are condition codes e.g. "NM", "LP", "NM_FOIL".
    /// Returns an empty dictionary if the card was not found.
    /// </summary>
    Task<Dictionary<string, decimal>> GetCardPricesAsync(string cardName, CancellationToken ct = default);

    /// <summary>
    /// Paginates through the Good Games MTG collection via the Shopify products.json endpoint,
    /// capturing all variants (NM/LP/MP/HP/DMG, foil and non-foil) for every product.
    /// Stores results as PriceSnapshots keyed by condition.
    /// </summary>
    /// <summary>
    /// When <paramref name="force"/> is true the "already synced today" guard is bypassed,
    /// allowing a manual re-run on the same day. The scheduled nightly run always passes false.
    /// </summary>
    Task<int> ScrapeAllPricesAsync(IProgress<string>? progress = null, bool force = false, CancellationToken ct = default);

    /// <summary>
    /// Fetches live Good Games products matching <paramref name="searchTerm"/> and returns
    /// a detailed inspection report showing every raw Shopify field, the parsed card
    /// name / set / art variant, the resolved MTGJSON match, and what prices would be written.
    /// Useful for debugging matching issues without modifying the database.
    /// </summary>
    Task<GoodGamesInspectResponse> InspectProductsAsync(string searchTerm, CancellationToken ct = default);
}
