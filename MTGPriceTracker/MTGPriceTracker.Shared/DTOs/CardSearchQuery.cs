namespace MTGPriceTracker.Shared.DTOs;

public class CardSearchQuery
{
    public string? Query { get; set; }
    public string? SetCode { get; set; }
    public string? Rarity { get; set; }
    public string? Type { get; set; }
    public bool? FavoritesOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 24;
    public string SortBy { get; set; } = "name";
    public bool SortDescending { get; set; } = false;
}
