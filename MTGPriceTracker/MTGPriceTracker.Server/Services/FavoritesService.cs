using MTGPriceTracker.Server.Repositories.Interfaces;
using MTGPriceTracker.Server.Services.Interfaces;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Services;

public class FavoritesService : IFavoritesService
{
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IPriceRepository _priceRepository;

    public FavoritesService(IFavoriteRepository favoriteRepository, IPriceRepository priceRepository)
    {
        _favoriteRepository = favoriteRepository;
        _priceRepository = priceRepository;
    }

    public async Task<List<CardDto>> GetFavoritesAsync(CancellationToken ct = default)
    {
        var favorites = await _favoriteRepository.GetAllAsync(ct);
        var favoriteList = favorites.ToList();
        var uuids = favoriteList.Select(f => f.CardUuid).ToList();
        var latestPricesMap = await _priceRepository.GetLatestPricesForCardsAsync(uuids, ct);

        return favoriteList.Select(f => new CardDto
        {
            Uuid = f.Card.Uuid,
            Name = f.Card.Name,
            SetCode = f.Card.SetCode,
            SetName = f.Card.Set?.Name ?? f.Card.SetCode,
            Rarity = f.Card.Rarity,
            Type = f.Card.Type,
            ManaCost = f.Card.ManaCost,
            ScryfallId = f.Card.ScryfallId,
            IsFavorite = true,
            LatestPrices = latestPricesMap.TryGetValue(f.CardUuid, out var prices)
                ? prices
                : new Dictionary<string, decimal>()
        }).ToList();
    }

    public async Task<bool> IsFavoriteAsync(string cardUuid, CancellationToken ct = default)
    {
        return await _favoriteRepository.IsFavoriteAsync(cardUuid, ct);
    }

    public async Task AddFavoriteAsync(string cardUuid, CancellationToken ct = default)
    {
        await _favoriteRepository.AddAsync(cardUuid, ct);
    }

    public async Task RemoveFavoriteAsync(string cardUuid, CancellationToken ct = default)
    {
        await _favoriteRepository.RemoveAsync(cardUuid, ct);
    }
}
