using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Services.Interfaces;

public interface IDiscordNotificationService
{
    /// <summary>
    /// Finds cards whose TCGPlayer retail price jumped ≥10% in the last 7 days
    /// and whose current price is ≥ $3, then posts a Discord embed for each.
    /// </summary>
    Task<PriceAlertResultDto> SendPriceAlertsAsync(CancellationToken ct = default);
}
