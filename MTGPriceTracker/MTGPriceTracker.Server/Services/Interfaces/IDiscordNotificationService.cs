using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Services.Interfaces;

public interface IDiscordNotificationService
{
    /// <summary>
    /// Queries the DB for price alerts (fast). Returns immediately.
    /// </summary>
    Task<List<PriceAlertDto>> FindAlertsAsync(CancellationToken ct = default);

    /// <summary>
    /// Posts the given alerts to Discord. Long-running — call fire-and-forget
    /// with CancellationToken.None so it isn't tied to the HTTP request lifetime.
    /// </summary>
    Task PostAlertsAsync(List<PriceAlertDto> alerts);
}
