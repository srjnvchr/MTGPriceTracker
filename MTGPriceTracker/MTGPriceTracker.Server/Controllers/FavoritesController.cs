using Microsoft.AspNetCore.Mvc;
using MTGPriceTracker.Server.Services.Interfaces;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FavoritesController : ControllerBase
{
    private readonly IFavoritesService _favoritesService;

    public FavoritesController(IFavoritesService favoritesService)
    {
        _favoritesService = favoritesService;
    }

    /// <summary>Get all favorited cards.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<CardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CardDto>>> GetFavorites(CancellationToken ct = default)
    {
        var favorites = await _favoritesService.GetFavoritesAsync(ct);
        return Ok(favorites);
    }

    /// <summary>Check if a specific card is favorited.</summary>
    [HttpGet("{uuid}/status")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<ActionResult<bool>> GetFavoriteStatus(string uuid, CancellationToken ct = default)
    {
        var isFavorite = await _favoritesService.IsFavoriteAsync(uuid, ct);
        return Ok(isFavorite);
    }

    /// <summary>Add a card to favorites.</summary>
    [HttpPost("{uuid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddFavorite(string uuid, CancellationToken ct = default)
    {
        await _favoritesService.AddFavoriteAsync(uuid, ct);
        return NoContent();
    }

    /// <summary>Remove a card from favorites.</summary>
    [HttpDelete("{uuid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveFavorite(string uuid, CancellationToken ct = default)
    {
        await _favoritesService.RemoveFavoriteAsync(uuid, ct);
        return NoContent();
    }
}
