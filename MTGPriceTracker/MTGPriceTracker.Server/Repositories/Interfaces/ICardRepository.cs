using MTGPriceTracker.Server.Models;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Repositories.Interfaces;

public interface ICardRepository
{
    Task<PagedResult<Card>> SearchAsync(CardSearchQuery query, IEnumerable<string> favoriteUuids, CancellationToken ct = default);
    Task<Card?> GetByUuidAsync(string uuid, CancellationToken ct = default);
    Task<bool> ExistsAsync(string uuid, CancellationToken ct = default);
    Task UpsertAsync(Card card, CancellationToken ct = default);
    Task BulkUpsertAsync(IEnumerable<Card> cards, CancellationToken ct = default);
    Task<int> GetTotalCountAsync(CancellationToken ct = default);
    Task<IEnumerable<string>> GetAllUuidsAsync(CancellationToken ct = default);

    /// <summary>Returns a lower-cased name → uuid map for all cards. Single query — use instead of N+1 GetByUuidAsync loops.</summary>
    Task<Dictionary<string, string>> GetNameToUuidMapAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a rich lookup: lower-cased card name → all printings, each with set name,
    /// frameEffects, and borderColor. Used by the Good Games scraper to find the exact
    /// UUID for a product listing that includes set name and art-variant info.
    /// </summary>
    Task<Dictionary<string, List<CardLookupEntry>>> GetCardLookupAsync(CancellationToken ct = default);
}

/// <summary>One row in the rich card lookup — one printing of a card.</summary>
public record CardLookupEntry(
    string Uuid,
    string SetCode,
    string SetName,
    string? FrameEffects,     // comma-separated, lowercase: "showcase,extendedart"
    string? BorderColor,      // lowercase: "borderless" | "black" | "white" | null
    bool HasNonFoil,          // from MTGJSON finishes[]
    bool HasFoil,
    bool HasEtched,
    string? CollectorNumber); // MTGJSON 'number' field — used for SKU-based exact matching
