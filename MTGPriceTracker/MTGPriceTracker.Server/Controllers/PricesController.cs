using Microsoft.AspNetCore.Mvc;
using MTGPriceTracker.Server.Services.Interfaces;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PricesController : ControllerBase
{
    private readonly IPriceService _priceService;

    public PricesController(IPriceService priceService)
    {
        _priceService = priceService;
    }

    /// <summary>Get price history for a card across all vendors.</summary>
    [HttpGet("{uuid}/history")]
    [ProducesResponseType(typeof(List<VendorPriceHistoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<VendorPriceHistoryDto>>> GetPriceHistory(
        string uuid,
        [FromQuery] int days = 90,
        CancellationToken ct = default)
    {
        var history = await _priceService.GetPriceHistoryAsync(uuid, Math.Clamp(days, 1, 365), ct);
        return Ok(history);
    }

    /// <summary>Get latest price per vendor for a card.</summary>
    [HttpGet("{uuid}/latest")]
    [ProducesResponseType(typeof(Dictionary<string, decimal>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Dictionary<string, decimal>>> GetLatestPrices(
        string uuid,
        CancellationToken ct = default)
    {
        var prices = await _priceService.GetLatestPricesAsync(uuid, ct);
        return Ok(prices);
    }
}
