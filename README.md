# MTG Price Tracker

Track Magic: The Gathering card prices across international vendors and local Australian resellers, with daily price history and Discord alerts for buying opportunities.

Built with Blazor WebAssembly (hosted) on ASP.NET Core 8, backed by SQLite.
Read more about the project architecture in ARCHITECTURE.md file.

## Features

- **Card catalog** — every MTG printing imported from [MTGJSON](https://mtgjson.com), browsable with search, set/rarity/type filters and sorting
- **Multi-vendor prices** — TCGPlayer (USD), Card Kingdom (USD), CardMarket (EUR) from MTGJSON, plus [Good Games](https://tcg.goodgames.com.au) (AUD) via its Shopify JSON API
- **Price history charts** — per-vendor time series on each card page (Blazor-ApexCharts), with a 90-day backfill
- **Foil, etched and condition tracking** — Good Games prices stored per condition (NM/LP/MP/HP/DMG) and finish
- **Favorites** — bookmark cards and view them together
- **Nightly sync** — background job runs at 1:00 AM AEST; also triggerable from the UI
- **Discord alerts** — finds cards whose TCGPlayer/Card Kingdom price jumped while Good Games hasn't repriced yet, and posts them to a daily Discord forum thread, colour-coded by size of the spike
- **Admin tools** — Data Sync page and a Good Games inspector for debugging product matching

## Tech stack

| Layer | Technology |
|---|---|
| Frontend | Blazor WebAssembly, MudBlazor 7, Blazor-ApexCharts |
| Backend | ASP.NET Core 8 Web API, Swagger |
| Data | SQLite via EF Core 8 (WAL mode) |
| Background work | `IHostedService` |
| Data sources | MTGJSON (`AllPrintings`, `AllPricesToday`, `AllPrices`), Good Games Shopify API |

## Getting started

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
git clone git@github.com:srjnvchr/MTGPriceTracker.git
cd MTGPriceTracker/MTGPriceTracker
dotnet run --project MTGPriceTracker.Server
```

The server hosts the Blazor client. Open the URL printed in the console (default `https://localhost:7191`). Swagger is available at `/swagger` in Development.

The SQLite database (`mtg-prices.db`) is created automatically on first run and is git-ignored.

### First-time data setup

The database starts empty. Open **Data Sync** in the sidebar (`/sync`) and run, in order:

1. **Import card catalog** — downloads `AllPrintings.json` (~50 MB compressed, several minutes)
2. **Import price history** *(optional)* — one-time 90-day backfill from `AllPrices.json` (~150 MB, 10–20 minutes)
3. **Run full sync** — imports today's MTGJSON prices and scrapes Good Games

After that, the nightly job keeps prices current.

### Configuration

`MTGPriceTracker.Server/appsettings.json`:

| Key | Description |
|---|---|
| `Database:Path` | SQLite file path (default `mtg-prices.db`) |
| `Discord:WebhookUrl` | Webhook of a Discord **forum** channel for price alerts |
| `Discord:YellowThresholdPct` | Spike % at which alerts turn yellow (default 25) |
| `Discord:RedThresholdPct` | Spike % at which alerts turn red (default 50) |

Keep your webhook URL out of source control — use [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (`dotnet user-secrets set "Discord:WebhookUrl" "<url>"`) or the `Discord__WebhookUrl` environment variable.

## API overview

| Endpoint | Description |
|---|---|
| `GET /api/cards` | Search/browse cards (pagination, filters, sorting) |
| `GET /api/cards/{uuid}` | Card details |
| `GET /api/prices/{uuid}/history?days=90` | Price history per vendor |
| `GET /api/prices/{uuid}/latest` | Latest price per vendor |
| `GET/POST/DELETE /api/favorites` | Manage favorites |
| `GET /api/sync/status` | Sync progress and DB stats |
| `POST /api/sync/trigger` | Full sync (MTGJSON + Good Games) |
| `POST /api/sync/trigger-goodgames` | Good Games scrape only |
| `POST /api/sync/import-catalog` · `import-history` | Catalog import / history backfill |
| `POST /api/notifications/discord/price-alerts` | Run alert check and post to Discord |

Full route list is in Swagger.

## Project structure

```
MTGPriceTracker/
├── MTGPriceTracker.Server/   ASP.NET Core API, EF Core, sync jobs (hosts the client)
├── MTGPriceTracker.Client/   Blazor WebAssembly UI
└── MTGPriceTracker.Shared/   DTOs shared by client and server
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for the layer design, database schema, import strategy and decisions log.

## Roadmap

- Currency conversion with live exchange rates
- More Australian resellers
- User accounts / multi-user favorites
- Email/push alerts

## Credits

- Card and price data from [MTGJSON](https://mtgjson.com)
- Card imagery from [Scryfall](https://scryfall.com)
- `holo_effect.css` — foil shine/glare effect reference

Magic: The Gathering is a trademark of Wizards of the Coast. This project is unofficial and not affiliated with or endorsed by Wizards of the Coast.
