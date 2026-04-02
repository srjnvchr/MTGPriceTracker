using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MTGPriceTracker.Server.Models;

public class UserFavorite
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string CardUuid { get; set; } = string.Empty;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    [ForeignKey(nameof(CardUuid))]
    public Card Card { get; set; } = null!;
}
