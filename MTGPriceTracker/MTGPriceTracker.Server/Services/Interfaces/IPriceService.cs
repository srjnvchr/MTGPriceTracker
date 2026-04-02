using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Services.Interfaces;

public interface IPriceService
{
    Task<List<VendorPriceHistoryDto>> GetPriceHistoryAsync(string cardUuid, int days = 90, CancellationToken ct = default);
    Task<Dictionary<string, decimal>> GetLatestPricesAsync(string cardUuid, CancellationToken ct = default);
}
