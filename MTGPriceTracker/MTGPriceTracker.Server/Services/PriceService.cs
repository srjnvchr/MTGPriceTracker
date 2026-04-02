using MTGPriceTracker.Server.Repositories.Interfaces;
using MTGPriceTracker.Server.Services.Interfaces;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Services;

public class PriceService : IPriceService
{
    private static readonly Dictionary<string, string> VendorDisplayNames = new()
    {
        ["tcgplayer"] = "TCGPlayer (USD)",
        ["cardkingdom"] = "Card Kingdom (USD)",
        ["cardmarket"] = "CardMarket (EUR)",
        ["goodgames"] = "Good Games AU (AUD)"
    };

    private static readonly Dictionary<string, string> VendorCurrencies = new()
    {
        ["tcgplayer"] = "USD",
        ["cardkingdom"] = "USD",
        ["cardmarket"] = "EUR",
        ["goodgames"] = "AUD"
    };

    private readonly IPriceRepository _priceRepository;

    public PriceService(IPriceRepository priceRepository)
    {
        _priceRepository = priceRepository;
    }

    public async Task<List<VendorPriceHistoryDto>> GetPriceHistoryAsync(
        string cardUuid,
        int days = 90,
        CancellationToken ct = default)
    {
        var snapshots = await _priceRepository.GetHistoryAsync(cardUuid, null, days, ct);

        // Group by vendor + price type
        var grouped = snapshots
            .GroupBy(s => new { s.Vendor, s.PriceType })
            .OrderBy(g => g.Key.Vendor)
            .ThenBy(g => g.Key.PriceType);

        var result = new List<VendorPriceHistoryDto>();

        foreach (var group in grouped)
        {
            var vendorKey = group.Key.Vendor;
            var priceType = group.Key.PriceType;
            var currency = VendorCurrencies.GetValueOrDefault(vendorKey, "USD");
            var displayName = VendorDisplayNames.TryGetValue(vendorKey, out var dn)
                ? $"{dn} ({priceType})"
                : $"{vendorKey} ({priceType})";

            var history = new VendorPriceHistoryDto
            {
                Vendor = vendorKey,
                DisplayName = displayName,
                PriceType = priceType,
                Currency = currency,
                PricePoints = group
                    .OrderBy(s => s.Date)
                    .Select(s => new PricePointDto { Date = s.Date, Price = s.Price })
                    .ToList()
            };

            result.Add(history);
        }

        return result;
    }

    public async Task<Dictionary<string, decimal>> GetLatestPricesAsync(string cardUuid, CancellationToken ct = default)
    {
        return await _priceRepository.GetLatestPricesAsync(cardUuid, ct);
    }
}
