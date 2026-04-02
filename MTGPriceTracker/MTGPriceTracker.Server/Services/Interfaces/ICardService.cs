using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Services.Interfaces;

public interface ICardService
{
    Task<PagedResult<CardDto>> SearchCardsAsync(CardSearchQuery query, CancellationToken ct = default);
    Task<CardDetailDto?> GetCardDetailAsync(string uuid, CancellationToken ct = default);
    Task<int> GetTotalCardCountAsync(CancellationToken ct = default);
}
