using MTGPriceTracker.Server.Models;

namespace MTGPriceTracker.Server.Repositories.Interfaces;

public interface IFavoriteRepository
{
    Task<IEnumerable<UserFavorite>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<string>> GetAllUuidsAsync(CancellationToken ct = default);
    Task<bool> IsFavoriteAsync(string cardUuid, CancellationToken ct = default);
    Task AddAsync(string cardUuid, CancellationToken ct = default);
    Task RemoveAsync(string cardUuid, CancellationToken ct = default);
}
