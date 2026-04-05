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

    /// <summary>
    /// Comma-separated MTGJSON frameEffects values: "showcase", "extendedart", "etched", "fullart", etc.
    /// Also includes "fullart" when MTGJSON isFullArt=true (basics and special full-art non-basics).
    /// Used to match Good Games product listings like "(Showcase)" or "(Extended Art)".
    /// </summary>
    [MaxLength(200)]
    public string? FrameEffects { get; set; }

    /// <summary>
    /// MTGJSON borderColor field. "borderless" for borderless cards; "black" / "white" for standard.
    /// Borderless is a separate field in MTGJSON, not in frameEffects.
    /// </summary>
    [MaxLength(20)]
    public string? BorderColor { get; set; }

    /// <summary>
    /// Whether this printing is available in non-foil finish.
    /// Parsed from MTGJSON finishes[]: contains "nonfoil".
    /// Defaults to true for cards imported before this field was added.
    /// </summary>
    public bool HasNonFoil { get; set; } = true;

    /// <summary>
    /// Whether this printing is available in foil finish.
    /// Parsed from MTGJSON finishes[]: contains "foil".
    /// </summary>
    public bool HasFoil { get; set; }

    /// <summary>
    /// Whether this printing is available in etched finish.
    /// Parsed from MTGJSON finishes[]: contains "etched".
    /// </summary>
    public bool HasEtched { get; set; }

    /// <summary>
    /// MTGJSON collector number within the set (the "number" field).
    /// Examples: "79", "351", "M380", "★1".
    /// Used with SetCode for exact-match lookups against Good Games SKU values
    /// (format: "{SetCode}-{CollectorNumber}-{Language}-{Finish}-{Version}").
    /// </summary>
    [MaxLength(20)]
    public string? CollectorNumber { get; set; }

    // Navigation properties
    [ForeignKey(nameof(SetCode))]
    public CardSet Set { get; set; } = null!;

    public ICollection<PriceSnapshot> PriceSnapshots { get; set; } = new List<PriceSnapshot>();
    public ICollection<UserFavorite> Favorites { get; set; } = new List<UserFavorite>();
}
