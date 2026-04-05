namespace MTGPriceTracker.Shared.DTOs;

public class VendorPriceHistoryDto
{
    public string Vendor { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PriceType { get; set; } = string.Empty; // retail or buylist
    /// <summary>Card condition (NM, LP, MP, HP, DMG, NM_FOIL etc). Null for MTGJSON market prices.</summary>
    public string? Condition { get; set; }
    public string Currency { get; set; } = "USD";
    public List<PricePointDto> PricePoints { get; set; } = new();
    public decimal? LatestPrice => PricePoints.Count > 0 ? PricePoints[^1].Price : null;
    public decimal? PreviousPrice => PricePoints.Count > 1 ? PricePoints[^2].Price : null;
    public decimal? PriceChange => LatestPrice.HasValue && PreviousPrice.HasValue
        ? LatestPrice.Value - PreviousPrice.Value
        : null;
    public double? PriceChangePercent => LatestPrice.HasValue && PreviousPrice.HasValue && PreviousPrice.Value != 0
        ? (double)((LatestPrice.Value - PreviousPrice.Value) / PreviousPrice.Value * 100)
        : null;
}

public class PricePointDto
{
    public DateTime Date { get; set; }
    public decimal Price { get; set; }
}
