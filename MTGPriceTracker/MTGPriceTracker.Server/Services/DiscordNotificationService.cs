using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MTGPriceTracker.Server.Data;
using MTGPriceTracker.Server.Services.Interfaces;
using MTGPriceTracker.Shared.DTOs;

namespace MTGPriceTracker.Server.Services;

public class DiscordNotificationService : IDiscordNotificationService
{
    public const string HttpClientName = "Discord";

    private const double MinPriceChangePercent = 10.0;
    private const decimal MinCurrentPrice = 3.0m;
    private const int LookbackDays = 7;
    private const int EmbedsPerMessage = 10;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<DiscordNotificationService> _logger;

    public DiscordNotificationService(
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<DiscordNotificationService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    // ── Query ────────────────────────────────────────────────────────────────

    public async Task<List<PriceAlertDto>> FindAlertsAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-LookbackDays);

        // Pull all tcgplayer retail (non-foil) prices in the window.
        // Condition is NULL for MTGJSON vendors — that's the non-foil retail price.
        var snapshots = await _db.PriceSnapshots
            .AsNoTracking()
            .Where(p => p.Vendor == "tcgplayer"
                     && p.PriceType == "retail"
                     && p.Condition == null
                     && p.Date >= cutoff)
            .ToListAsync(ct);

        if (snapshots.Count == 0)
            return new List<PriceAlertDto>();

        // Group by card, compare oldest vs newest price in the window.
        var candidates = snapshots
            .GroupBy(p => p.CardUuid)
            .Select(g =>
            {
                var sorted = g.OrderBy(p => p.Date).ToList();
                var oldest = sorted.First();
                var latest = sorted.Last();

                // Require at least two data points to compute a meaningful change.
                if (sorted.Count < 2 || oldest.Price <= 0m)
                    return null;

                var changePct = (latest.Price - oldest.Price) / oldest.Price * 100m;
                return new
                {
                    CardUuid = g.Key,
                    OldPrice = oldest.Price,
                    NewPrice = latest.Price,
                    ChangePercent = changePct,
                    OldDate = oldest.Date,
                    NewDate = latest.Date
                };
            })
            .Where(x => x != null
                     && x.NewPrice >= MinCurrentPrice
                     && x.ChangePercent >= (decimal)MinPriceChangePercent)
            .OrderByDescending(x => x!.ChangePercent)
            .ToList();

        if (candidates.Count == 0)
            return new List<PriceAlertDto>();

        // Fetch card names + set codes for all matching UUIDs in one query.
        var uuids = candidates.Select(c => c!.CardUuid).ToList();
        var cardInfo = await _db.Cards
            .AsNoTracking()
            .Where(c => uuids.Contains(c.Uuid))
            .Select(c => new { c.Uuid, c.Name, c.SetCode })
            .ToDictionaryAsync(c => c.Uuid, ct);

        return candidates
            .Where(c => cardInfo.ContainsKey(c!.CardUuid))
            .Select(c => new PriceAlertDto
            {
                CardUuid      = c!.CardUuid,
                CardName      = cardInfo[c.CardUuid].Name,
                SetCode       = cardInfo[c.CardUuid].SetCode,
                OldPrice      = c.OldPrice,
                NewPrice      = c.NewPrice,
                ChangePercent = Math.Round(c.ChangePercent, 1),
                Currency      = "USD",
                Vendor        = "TCGPlayer",
                OldDate       = c.OldDate,
                NewDate       = c.NewDate
            })
            .ToList();
    }

    // ── Discord webhook ──────────────────────────────────────────────────────

    /// <summary>
    /// Long-running — always called with CancellationToken.None so it is never
    /// cancelled by the HTTP request lifetime.
    /// </summary>
    public async Task PostAlertsAsync(List<PriceAlertDto> alerts)
    {
        var webhookUrl = _config["Discord:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _logger.LogWarning("Discord webhook URL is not configured — skipping post.");
            return;
        }

        if (alerts.Count == 0)
            return;

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);

            // Header summary message.
            await SendMessageAsync(client, webhookUrl, new
            {
                username = "MTG Price Tracker",
                content  = $"**Price Alert** — {alerts.Count} card{(alerts.Count == 1 ? "" : "s")} jumped **10%+** in the last 7 days (TCGPlayer retail, NM, ≥$3)"
            });

            // Send card embeds in batches of EmbedsPerMessage.
            for (int i = 0; i < alerts.Count; i += EmbedsPerMessage)
            {
                var batch = alerts.Skip(i).Take(EmbedsPerMessage).ToList();

                var embeds = batch.Select(alert => new
                {
                    title  = $"{alert.CardName}  [{alert.SetCode}]",
                    color  = EmbedColor(alert.ChangePercent),
                    fields = new[]
                    {
                        new { name = "7 Days Ago",    value = $"${alert.OldPrice:F2}",       inline = true },
                        new { name = "Current Price", value = $"${alert.NewPrice:F2}",       inline = true },
                        new { name = "Change",        value = $"+{alert.ChangePercent:F1}%", inline = true }
                    },
                    footer = new { text = $"TCGPlayer · NM Retail · {alert.OldDate:dd MMM} → {alert.NewDate:dd MMM}" }
                }).ToList();

                await SendMessageAsync(client, webhookUrl, new { username = "MTG Price Tracker", embeds });

                // Polite delay between batches — Discord allows 30 requests/second per webhook.
                if (i + EmbedsPerMessage < alerts.Count)
                    await Task.Delay(1000);
            }

            _logger.LogInformation("Discord price alerts sent: {Count} cards.", alerts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post Discord price alerts.");
        }
    }

    private static async Task SendMessageAsync(HttpClient client, string webhookUrl, object payload)
    {
        var json     = JsonSerializer.Serialize(payload);
        var content  = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(webhookUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Discord webhook returned {(int)response.StatusCode}: {body}");
        }
    }

    /// <summary>Returns a Discord embed colour based on the magnitude of the price jump.</summary>
    private static int EmbedColor(decimal changePct) => changePct switch
    {
        >= 50m => 0xFF4500,  // red-orange  — extreme spike
        >= 25m => 0xFF8C00,  // dark orange — large spike
        _      => 0xFFD700   // gold        — moderate jump (10–25%)
    };
}
