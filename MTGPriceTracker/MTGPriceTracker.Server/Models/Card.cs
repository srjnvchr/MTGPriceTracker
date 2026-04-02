using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MTGPriceTracker.Server.Models;

public class Card
{
    [Key]
    public string Uuid { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string SetCode { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Rarity { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Type { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ManaCost { get; set; }

    public string? Text { get; set; }

    public string? FlavorText { get; set; }

    [MaxLength(20)]
    public string? Power { get; set; }

    [MaxLength(20)]
    public string? Toughness { get; set; }

    [MaxLength(20)]
    public string? Loyalty { get; set; }

    [MaxLength(200)]
    public string? Artist { get; set; }

    /// <summary>Scryfall UUID — used to construct image URLs.</summary>
    [MaxLength(36)]
    public string? ScryfallId { get; set; }

    [MaxLength(100)]
    public string? ColorIdentity { get; set; }

    // Navigation properties
    [ForeignKey(nameof(SetCode))]
    public CardSet Set { get; set; } = null!;

    public ICollection<PriceSnapshot> PriceSnapshots { get; set; } = new List<PriceSnapshot>();
    public ICollection<UserFavorite> Favorites { get; set; } = new List<UserFavorite>();
}
