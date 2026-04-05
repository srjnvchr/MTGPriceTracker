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

        // Group by vendor + priceType + condition
        var grouped = snapshots
            .GroupBy(s => new { s.Vendor, s.PriceType, s.Condition })
            .OrderBy(g => g.Key.Vendor)
            .ThenBy(g => g.Key.PriceType)
            .ThenBy(g => g.Key.Condition ?? "");

        var result = new List<VendorPriceHistoryDto>();

        foreach (var group in grouped)
        {
            var vendorKey = group.Key.Vendor;
            var priceType = group.Key.PriceType;
            var condition = group.Key.Condition;
            var currency = VendorCurrencies.GetValueOrDefault(vendorKey, "USD");

            // Build a human-readable display name.
            // PriceType encodes both type and finish: "retail", "retail_foil", "buylist", "buylist_foil"
            // Condition is set for Good Games (NM, LP, NM_FOIL, etc.), null for MTGJSON.
            var baseName = VendorDisplayNames.TryGetValue(vendorKey, out var dn) ? dn : vendorKey;
            var displayName = condition != null
                ? $"{baseName} — {condition}"       // Good Games: "Good Games AU (AUD) — NM"
                : $"{baseName} — {FormatPriceType(priceType)}"; // MTGJSON: "TCGPlayer (USD) — Retail Foil"

            var history = new VendorPriceHistoryDto
            {
                Vendor = vendorKey,
                DisplayName = displayName,
                PriceType = priceType,
                Condition = condition,
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

    /// <summary>
    /// Converts a raw priceType key into a readable label.
    /// "retail"       → "Retail"
    /// "retail_foil"  → "Retail Foil"
    /// "buylist"      → "Buylist"
    /// "buylist_foil" → "Buylist Foil"
    /// </summary>
    private static string FormatPriceType(string priceType) =>
        priceType switch
        {
            "retail"       => "Retail",
            "retail_foil"  => "Retail Foil",
            "buylist"      => "Buylist",
            "buylist_foil" => "Buylist Foil",
            _              => System.Globalization.CultureInfo.InvariantCulture
                                 .TextInfo.ToTitleCase(priceType.Replace('_', ' '))
        };
}
