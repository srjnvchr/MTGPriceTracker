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
}
