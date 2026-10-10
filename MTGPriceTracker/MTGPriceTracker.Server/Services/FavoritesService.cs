using MTGPriceTracker.Server.Repositories.Interfaces;
using MTGPriceTracker.Server.Services.Interfaces;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Services;

public class FavoritesService : IFavoritesService
{
    private readonly IFavoriteRepository _favoriteRepository;

    public FavoritesService(IFavoriteRepository favoriteRepository)
    {
        _favoriteRepository = favoriteRepository;
    }

    public async Task<List<CardDto>> GetFavoritesAsync(CancellationToken ct = default)
    {
        // Like the card list, favorites return card info only. Prices load on the card page.
        var favorites = await _favoriteRepository.GetAllAsync(ct);
        var favoriteList = favorites.ToList();

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
            HasFoil = f.Card.HasFoil,
            HasNonFoil = f.Card.HasNonFoil
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
