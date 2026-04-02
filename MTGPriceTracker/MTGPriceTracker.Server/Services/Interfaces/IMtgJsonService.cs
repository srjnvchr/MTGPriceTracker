namespace MTGPriceTracker.Server.Services.Interfaces;

public interface IMtgJsonService
{
    /// <summary>Downloads and imports card catalog from AllPrintings.json.</summary>
    Task<(int setsImported, int cardsImported)> ImportAllPrintingsAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>Downloads and imports latest price data from AllPrices.json.</summary>
    Task<int> ImportAllPricesAsync(IProgress<string>? progress = null, CancellationToken ct = default);
}
