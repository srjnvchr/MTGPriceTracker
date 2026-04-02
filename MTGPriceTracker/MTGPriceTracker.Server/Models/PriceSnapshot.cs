using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MTGPriceTracker.Server.Models;

public class PriceSnapshot
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string CardUuid { get; set; } = string.Empty;

    /// <summary>
    /// Vendor identifier. Known values: tcgplayer, cardkingdom, cardmarket, goodgames
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Vendor { get; set; } = string.Empty;

    /// <summary>Price type: retail or buylist</summary>
    [Required]
    [MaxLength(20)]
    public string PriceType { get; set; } = "retail";

    /// <summary>ISO 4217 currency code: USD, EUR, AUD</summary>
    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    [Column(TypeName = "decimal(10,4)")]
    public decimal Price { get; set; }

    /// <summary>Date the price was recorded (YYYY-MM-DD).</summary>
    [Required]
    public DateTime Date { get; set; }

    // Navigation property
    [ForeignKey(nameof(CardUuid))]
    public Card Card { get; set; } = null!;
}
