namespace MTGPriceTracker.Shared.DTOs;

public class CardDto
{
    public string Uuid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SetCode { get; set; } = string.Empty;
    public string SetName { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ManaCost { get; set; }
    public string? ScryfallId { get; set; }
    public bool IsFavorite { get; set; }

    // Latest price snapshot per vendor (for card list display)
    public Dictionary<string, decimal> LatestPrices { get; set; } = new();

    /// <summary>
    /// Constructs the Scryfall card image URI from the ScryfallId.
    /// Returns null if ScryfallId is not set.
    /// </summary>
    public string? ImageUri => ScryfallId != null
        ? $"https://cards.scryfall.io/normal/front/{ScryfallId[0]}/{ScryfallId[1]}/{ScryfallId}.jpg"
        : null;
}
