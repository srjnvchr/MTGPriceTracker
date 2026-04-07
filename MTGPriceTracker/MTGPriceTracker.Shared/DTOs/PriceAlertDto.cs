namespace MTGPriceTracker.Shared.DTOs;

public class PriceAlertDto
{
    public string CardUuid { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public string SetCode { get; set; } = string.Empty;
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public decimal ChangePercent { get; set; }
    public string Currency { get; set; } = "USD";
    public string Vendor { get; set; } = string.Empty;
    public DateTime OldDate { get; set; }
    public DateTime NewDate { get; set; }
}

public class PriceAlertResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int AlertCount { get; set; }
    public List<PriceAlertDto> Alerts { get; set; } = new();
}
