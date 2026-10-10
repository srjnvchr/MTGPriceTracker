using MTGPriceTracker.Server.Repositories.Interfaces;
using MTGPriceTracker.Server.Services.Interfaces;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Services;

public class CardService : ICardService
{
    private readonly ICardRepository _cardRepository;
    private readonly IPriceRepository _priceRepository;
    private readonly IFavoriteRepository _favoriteRepository;

    public CardService(
        ICardRepository cardRepository,
        IPriceRepository priceRepository,
        IFavoriteRepository favoriteRepository)
    {
        _cardRepository = cardRepository;
        _priceRepository = priceRepository;
        _favoriteRepository = favoriteRepository;
    }

    public async Task<PagedResult<CardDto>> SearchCardsAsync(CardSearchQuery query, CancellationToken ct = default)
    {
        var favoriteUuids = await _favoriteRepository.GetAllUuidsAsync(ct);
        var pagedCards = await _cardRepository.SearchAsync(query, favoriteUuids, ct);

        // Prices are deliberately not loaded for list results: they live in the huge PriceSnapshots
        // table, and the list only needs card info. Prices are fetched when a card is opened.
        var favoriteSet = favoriteUuids.ToHashSet();

        var dtos = pagedCards.Items.Select(card => new CardDto
        {
            Uuid = card.Uuid,
            Name = card.Name,
            SetCode = card.SetCode,
            SetName = card.Set?.Name ?? card.SetCode,
            Rarity = card.Rarity,
            Type = card.Type,
            ManaCost = card.ManaCost,
            ScryfallId = card.ScryfallId,
            IsFavorite = favoriteSet.Contains(card.Uuid),
            HasFoil = card.HasFoil,
            HasNonFoil = card.HasNonFoil
        }).ToList();

        return new PagedResult<CardDto>
        {
            Items = dtos,
            TotalCount = pagedCards.TotalCount,
            Page = pagedCards.Page,
            PageSize = pagedCards.PageSize
        };
    }

    public async Task<CardDetailDto?> GetCardDetailAsync(string uuid, CancellationToken ct = default)
    {
        var card = await _cardRepository.GetByUuidAsync(uuid, ct);
        if (card is null) return null;

        var favoriteUuids = await _favoriteRepository.GetAllUuidsAsync(ct);
        var favoriteSet = favoriteUuids.ToHashSet();
        var latestPrices = await _priceRepository.GetLatestPricesAsync(uuid, ct);

        return new CardDetailDto
        {
            Uuid = card.Uuid,
            Name = card.Name,
            SetCode = card.SetCode,
            SetName = card.Set?.Name ?? card.SetCode,
            Rarity = card.Rarity,
            Type = card.Type,
            ManaCost = card.ManaCost,
            ScryfallId = card.ScryfallId,
            Text = card.Text,
            FlavorText = card.FlavorText,
            Power = card.Power,
            Toughness = card.Toughness,
            Loyalty = card.Loyalty,
            Artist = card.Artist,
            ColorIdentity = card.ColorIdentity,
            IsFavorite = favoriteSet.Contains(card.Uuid),
            LatestPrices = latestPrices
        };
    }

    public async Task<int> GetTotalCardCountAsync(CancellationToken ct = default)
    {
        return await _cardRepository.GetTotalCountAsync(ct);
    }
}
