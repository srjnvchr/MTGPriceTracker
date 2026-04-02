using Microsoft.AspNetCore.Mvc;
using MTGPriceTracker.Server.BackgroundServices;
using MTGPriceTracker.Server.Repositories.Interfaces;
using MTGPriceTracker.Server.Services.Interfaces;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly PriceSyncBackgroundService _syncService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICardRepository _cardRepository;
    private readonly IPriceRepository _priceRepository;
    private readonly SyncState _syncState;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        PriceSyncBackgroundService syncService,
        IServiceScopeFactory scopeFactory,
        ICardRepository cardRepository,
        IPriceRepository priceRepository,
        SyncState syncState,
        ILogger<SyncController> logger)
    {
        _syncService = syncService;
        _scopeFactory = scopeFactory;
        _cardRepository = cardRepository;
        _priceRepository = priceRepository;
        _syncState = syncState;
        _logger = logger;
    }

    /// <summary>Get current sync status and database statistics.</summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(SyncStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SyncStatusDto>> GetStatus(CancellationToken ct = default)
    {
        var status = new SyncStatusDto
        {
            IsRunning = _syncState.IsRunning,
            LastSyncAt = _syncState.LastSyncAt,
            LastSyncResult = _syncState.LastSyncResult,
            CurrentOperation = _syncState.CurrentOperation,
            TotalCards = await _cardRepository.GetTotalCountAsync(ct),
            TotalPriceSnapshots = await _priceRepository.GetTotalCountAsync(ct)
        };
        return Ok(status);
    }

    /// <summary>
    /// Trigger a full price sync manually (MTGJSON + Good Games).
    /// Fire and forget — poll /api/sync/status for progress.
    /// </summary>
    [HttpPost("trigger")]
    [ProducesResponseType(typeof(SyncTriggerResponse), StatusCodes.Status200OK)]
    public IActionResult TriggerSync()
    {
        if (_syncState.IsRunning)
            return Ok(new SyncTriggerResponse { Success = false, Message = "Sync is already in progress." });

        _ = Task.Run(() => _syncService.RunSyncAsync());

        return Ok(new SyncTriggerResponse { Success = true, Message = "Sync triggered. Poll /api/sync/status for progress." });
    }

    /// <summary>
    /// Trigger initial card catalog import from AllPrintings.json.
    /// Run this once on first setup (takes ~3–10 minutes).
    /// Fire and forget — poll /api/sync/status for progress.
    /// </summary>
    [HttpPost("import-catalog")]
    [ProducesResponseType(typeof(SyncTriggerResponse), StatusCodes.Status200OK)]
    public IActionResult TriggerCatalogImport()
    {
        if (_syncState.IsRunning)
            return Ok(new SyncTriggerResponse { Success = false, Message = "Sync is already in progress." });

        // Capture singleton-safe scopeFactory — the request scope will be disposed before this task completes
        var scopeFactory = _scopeFactory;
        var syncState = _syncState;
        var logger = _logger;

        _ = Task.Run(async () =>
        {
            syncState.IsRunning = true;
            syncState.LastSyncAt = DateTime.UtcNow;
            try
            {
                // IServiceScopeFactory is a singleton — safe to use after the request scope is disposed
                using var scope = scopeFactory.CreateScope();
                var mtgJsonService = scope.ServiceProvider.GetRequiredService<IMtgJsonService>();

                var progress = new Progress<string>(msg =>
                {
                    syncState.CurrentOperation = msg;
                    logger.LogInformation("[CatalogImport] {Message}", msg);
                });

                await mtgJsonService.ImportAllPrintingsAsync(progress);
                syncState.LastSyncResult = "Catalog import complete";
            }
            catch (Exception ex)
            {
                syncState.LastSyncResult = $"Catalog import failed: {ex.Message}";
                logger.LogError(ex, "Catalog import failed");
            }
            finally
            {
                syncState.IsRunning = false;
                syncState.CurrentOperation = string.Empty;
            }
        });

        return Ok(new SyncTriggerResponse { Success = true, Message = "Catalog import triggered. This will take several minutes — poll /api/sync/status for progress." });
    }
}
