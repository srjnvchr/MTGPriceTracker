using System.ComponentModel.DataAnnotations;

namespace MTGPriceTracker.Server.Models;

public class CardSet
{
    [Key]
    [MaxLength(10)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? ReleaseDate { get; set; }

    [MaxLength(50)]
    public string? SetType { get; set; }

    public ICollection<Card> Cards { get; set; } = new List<Card>();
}
