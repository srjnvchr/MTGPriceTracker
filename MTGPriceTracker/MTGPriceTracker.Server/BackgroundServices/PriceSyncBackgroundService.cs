using MTGPriceTracker.Server.Services.Interfaces;

namespace MTGPriceTracker.Server.BackgroundServices;

/// <summary>
/// Background service that runs price sync jobs on a daily schedule.
/// - 1:00 AM AEST: MTGJSON AllPrices import
/// - 2:00 AM AEST: Good Games scraper
/// </summary>
public class PriceSyncBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SyncState _syncState;
    private readonly ILogger<PriceSyncBackgroundService> _logger;

    public PriceSyncBackgroundService(
        IServiceProvider serviceProvider,
        SyncState syncState,
        ILogger<PriceSyncBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _syncState = syncState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PriceSyncBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = GetNextSyncTime();
            var delay = nextRun - DateTime.UtcNow;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation("Next price sync scheduled for {NextRun} UTC", nextRun);
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (!stoppingToken.IsCancellationRequested)
                await RunSyncAsync(stoppingToken);
        }
    }

    public async Task RunSyncAsync(CancellationToken ct = default)
    {
        if (_syncState.IsRunning)
        {
            _logger.LogWarning("Sync already in progress — skipping");
            return;
        }

        _syncState.IsRunning = true;
        _syncState.LastSyncAt = DateTime.UtcNow;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var mtgJsonService = scope.ServiceProvider.GetRequiredService<IMtgJsonService>();
            var goodGamesService = scope.ServiceProvider.GetRequiredService<IGoodGamesScraperService>();

            var progress = new Progress<string>(msg =>
            {
                _syncState.CurrentOperation = msg;
                _logger.LogInformation("[Sync] {Message}", msg);
            });

            // Step 1: MTGJSON prices
            _syncState.CurrentOperation = "Importing MTGJSON prices...";
            var priceCount = await mtgJsonService.ImportAllPricesAsync(progress, ct);

            // Step 2: Good Games
            _syncState.CurrentOperation = "Scraping Good Games prices...";
            var ggCount = await goodGamesService.ScrapeAllPricesAsync(progress, ct : ct);

            _syncState.LastSyncResult = $"Success — {priceCount} MTGJSON prices, {ggCount} Good Games prices";
            _syncState.CurrentOperation = string.Empty;
        }
        catch (OperationCanceledException)
        {
            _syncState.LastSyncResult = "Cancelled";
            _syncState.CurrentOperation = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Price sync failed");
            _syncState.LastSyncResult = $"Error: {ex.Message}";
            _syncState.CurrentOperation = string.Empty;
        }
        finally
        {
            _syncState.IsRunning = false;
        }
    }

    /// <summary>Returns the next scheduled sync time (1:00 AM AEST = UTC+10 = 15:00 UTC).</summary>
    private static DateTime GetNextSyncTime()
    {
        var aestOffset = TimeSpan.FromHours(10);
        var nowAest = DateTime.UtcNow.Add(aestOffset);
        var targetAest = nowAest.Date.AddHours(1); // 1:00 AM AEST

        if (nowAest >= targetAest)
            targetAest = targetAest.AddDays(1);

        return targetAest.Subtract(aestOffset); // back to UTC
    }
}
