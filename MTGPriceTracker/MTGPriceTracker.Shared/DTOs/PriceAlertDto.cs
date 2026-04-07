namespace MTGPriceTracker.Shared.DTOs;

public class PriceAlertDto
{
    public string CardUuid { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public string SetCode { get; set; } = string.Empty;

    // Card identity
    public string? CollectorNumber { get; set; }
    public string? BorderColor { get; set; }
    public string? FrameEffects { get; set; }
    public bool HasFoil { get; set; }
    public bool HasNonFoil { get; set; }

    // The price series that triggered the alert
    public string Vendor { get; set; } = string.Empty;   // "TCGPlayer" or "Card Kingdom"
    public bool IsFoil { get; set; }                     // whether it was the foil price series
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public decimal ChangePercent { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime OldDate { get; set; }
    public DateTime NewDate { get; set; }

    // Good Games context
    public decimal? GoodGamesPrice { get; set; }         // current NM price in AUD (null = not stocked)
}

public class PriceAlertResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int AlertCount { get; set; }
    public List<PriceAlertDto> Alerts { get; set; } = new();
}
