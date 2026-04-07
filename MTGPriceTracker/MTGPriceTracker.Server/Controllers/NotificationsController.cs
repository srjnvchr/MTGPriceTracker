using Microsoft.AspNetCore.Mvc;
using MTGPriceTracker.Server.Services.Interfaces;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly IDiscordNotificationService _discord;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        IDiscordNotificationService discord,
        ILogger<NotificationsController> logger)
    {
        _discord = discord;
        _logger = logger;
    }

    /// <summary>
    /// Scans the last 7 days of TCGPlayer retail prices, finds cards that jumped
    /// ≥10% and are currently ≥$3, and posts embeds to the configured Discord webhook.
    /// </summary>
    [HttpPost("discord/price-alerts")]
    [ProducesResponseType(typeof(PriceAlertResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PriceAlertResultDto>> TriggerPriceAlerts(CancellationToken ct)
    {
        _logger.LogInformation("Discord price-alert check triggered manually.");
        var result = await _discord.SendPriceAlertsAsync(ct);
        return Ok(result);
    }
}
