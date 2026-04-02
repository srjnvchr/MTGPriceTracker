namespace MTGPriceTracker.Shared.DTOs;

public class CardDetailDto : CardDto
{
    public string? Text { get; set; }
    public string? FlavorText { get; set; }
    public string? Power { get; set; }
    public string? Toughness { get; set; }
    public string? Artist { get; set; }
    public string? Loyalty { get; set; }
    public string? ColorIdentity { get; set; }
    public List<VendorPriceHistoryDto> PriceHistories { get; set; } = new();
}
