using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Services.Interfaces;

public interface IFavoritesService
{
    Task<List<CardDto>> GetFavoritesAsync(CancellationToken ct = default);
    Task<bool> IsFavoriteAsync(string cardUuid, CancellationToken ct = default);
    Task AddFavoriteAsync(string cardUuid, CancellationToken ct = default);
    Task RemoveFavoriteAsync(string cardUuid, CancellationToken ct = default);
}
