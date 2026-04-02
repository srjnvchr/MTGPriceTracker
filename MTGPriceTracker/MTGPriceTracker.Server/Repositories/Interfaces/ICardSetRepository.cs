using MTGPriceTracker.Server.Models;

namespace MTGPriceTracker.Server.Repositories.Interfaces;

public interface ICardSetRepository
{
    Task<IEnumerable<CardSet>> GetAllAsync(CancellationToken ct = default);
    Task<CardSet?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task BulkUpsertAsync(IEnumerable<CardSet> sets, CancellationToken ct = default);
}
