using Microsoft.AspNetCore.Mvc;
using MTGPriceTracker.Server.Services.Interfaces;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CardsController : ControllerBase
{
    private readonly ICardService _cardService;

    public CardsController(ICardService cardService)
    {
        _cardService = cardService;
    }

    /// <summary>Search and browse cards with pagination, filtering, and sorting.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CardDto>>> GetCards(
        [FromQuery] string? query = null,
        [FromQuery] string? setCode = null,
        [FromQuery] string? rarity = null,
        [FromQuery] string? type = null,
        [FromQuery] bool? favoritesOnly = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24,
        [FromQuery] string sortBy = "name",
        [FromQuery] bool sortDescending = false,
        CancellationToken ct = default)
    {
        var searchQuery = new CardSearchQuery
        {
            Query = query,
            SetCode = setCode,
            Rarity = rarity,
            Type = type,
            FavoritesOnly = favoritesOnly,
            Page = Math.Max(1, page),
            PageSize = Math.Clamp(pageSize, 1, 100),
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        var result = await _cardService.SearchCardsAsync(searchQuery, ct);
        return Ok(result);
    }

    /// <summary>Get full card details including price history.</summary>
    [HttpGet("{uuid}")]
    [ProducesResponseType(typeof(CardDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CardDetailDto>> GetCard(string uuid, CancellationToken ct = default)
    {
        var card = await _cardService.GetCardDetailAsync(uuid, ct);
        if (card is null) return NotFound();
        return Ok(card);
    }

    /// <summary>Get total number of cards in the database.</summary>
    [HttpGet("count")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> GetCount(CancellationToken ct = default)
    {
        var count = await _cardService.GetTotalCardCountAsync(ct);
        return Ok(count);
    }
}
