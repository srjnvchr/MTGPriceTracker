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
    /// Finds cards that jumped ≥10% in the last 7 days and are ≥$3, returns them
    /// immediately, then posts embeds to Discord in the background.
    /// </summary>
    [HttpPost("discord/price-alerts")]
    [ProducesResponseType(typeof(PriceAlertResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PriceAlertResultDto>> TriggerPriceAlerts(CancellationToken ct)
    {
        _logger.LogInformation("Discord price-alert check triggered manually.");

        // DB query is fast — do it within the request lifetime.
        var alerts = await _discord.FindAlertsAsync(ct);

        if (alerts.Count == 0)
        {
            return Ok(new PriceAlertResultDto
            {
                Success    = true,
                Message    = "No cards met the alert criteria (≥10% jump, ≥$3 current price).",
                AlertCount = 0,
                Alerts     = alerts
            });
        }

        // Fire Discord posting as a background task — completely decoupled from the
        // HTTP request so a client timeout can never cancel or crash it.
        _ = Task.Run(() => _discord.PostAlertsAsync(alerts));

        return Ok(new PriceAlertResultDto
        {
            Success    = true,
            Message    = $"Found {alerts.Count} alert{(alerts.Count == 1 ? "" : "s")} — sending to Discord in the background.",
            AlertCount = alerts.Count,
            Alerts     = alerts
        });
    }
}
