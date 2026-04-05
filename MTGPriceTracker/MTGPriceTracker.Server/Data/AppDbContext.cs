using Microsoft.EntityFrameworkCore;
using MTGPriceTracker.Server.Models;

namespace MTGPriceTracker.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Card> Cards => Set<Card>();
    public DbSet<CardSet> CardSets => Set<CardSet>();
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
    public DbSet<UserFavorite> UserFavorites => Set<UserFavorite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Card indexes
        modelBuilder.Entity<Card>(entity =>
        {
            entity.HasIndex(c => c.Name);
            entity.HasIndex(c => c.SetCode);
            entity.HasIndex(c => c.Rarity);
            entity.HasIndex(c => c.Type);
            entity.HasIndex(c => new { c.Name, c.SetCode });

            entity.HasOne(c => c.Set)
                  .WithMany(s => s.Cards)
                  .HasForeignKey(c => c.SetCode)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // PriceSnapshot indexes — critical for query performance
        modelBuilder.Entity<PriceSnapshot>(entity =>
        {
            entity.HasIndex(p => p.CardUuid);
            entity.HasIndex(p => p.Date);
            // Condition is nullable — SQLite treats each NULL as distinct, so null rows won't
            // collide with each other, and non-null rows are uniquely identified by all five columns.
            entity.HasIndex(p => new { p.CardUuid, p.Vendor, p.PriceType, p.Condition, p.Date }).IsUnique();

            entity.HasOne(p => p.Card)
                  .WithMany(c => c.PriceSnapshots)
                  .HasForeignKey(p => p.CardUuid)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // UserFavorites
        modelBuilder.Entity<UserFavorite>(entity =>
        {
            entity.HasIndex(f => f.CardUuid).IsUnique();

            entity.HasOne(f => f.Card)
                  .WithMany(c => c.Favorites)
                  .HasForeignKey(f => f.CardUuid)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
