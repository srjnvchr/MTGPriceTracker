# MTG Price Tracker

Track Magic: The Gathering card prices across international vendors and local Australian resellers, with daily price history and Discord alerts for buying opportunities.

A React single-page app on top of an ASP.NET Core 8 API, backed by SQLite.
Read more about the project architecture in ARCHITECTURE.md file.

<img width="1920" height="1080" alt="MTGPriceTracker-homepage-1080p" src="https://github.com/user-attachments/assets/9cbb5f1e-9f4a-4b37-b168-d72661c930ee" />


## Features

- **Card catalog** — every MTG printing imported from [MTGJSON](https://mtgjson.com), browsable with name search, rarity filter and sorting, with infinite scroll over a virtualized grid
- **Multi-vendor prices** — TCGPlayer (USD), Card Kingdom (USD), CardMarket (EUR) from MTGJSON, plus [Good Games](https://tcg.goodgames.com.au) (AUD) via its Shopify JSON API
- **Price history charts** — per-vendor time series on each card page (ApexCharts) with a USD/AUD toggle and 1W/1M/3M ranges, backed by a 90-day backfill
- **Foil, etched and condition tracking** — Good Games prices stored per condition (NM/LP/MP/HP/DMG) and finish; foil cards get a shimmer
- **Favorites** — bookmark cards and view them together
- **Nightly sync** — background job runs at 1:00 AM AEST; also triggerable from the UI
- **Discord alerts** — finds cards whose TCGPlayer/Card Kingdom price jumped while Good Games hasn't repriced yet, and posts them to a daily Discord forum thread, colour-coded by size of the spike
- **Admin tools** — Data Sync page and a Good Games inspector for debugging product matching
- **Dark and light themes** — follows the system setting, with a manual toggle

<img width="1920" height="1080" alt="MTGPriceTracker-card-1080p" src="https://github.com/user-attachments/assets/d8fa9150-4868-4eef-821c-7747a10d1b87" />


## Tech stack

| Layer | Technology |
|---|---|
| Frontend | React 19, TypeScript, Vite, React Router |
| UI | Tailwind CSS v4 (design tokens), Phosphor icons, Motion, Geist fonts |
| Data fetching | TanStack Query (caching, optimistic updates), TanStack Virtual |
| Charts | ApexCharts (`react-apexcharts`) |
| Backend | ASP.NET Core 8 Web API, Swagger |
| Data | SQLite via EF Core 8 (WAL mode) |
| Background work | `IHostedService` |
| Data sources | MTGJSON (`AllPrintings`, `AllPricesToday`, `AllPrices`), Good Games Shopify API |

## Getting started

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Node.js](https://nodejs.org) 20.19+ or 22.12+

```bash
git clone https://github.com/srjnvchr/MTGPriceTracker.git
cd MTGPriceTracker/MTGPriceTracker

# terminal 1: API on http://localhost:5190
dotnet run --project MTGPriceTracker.Server

# terminal 2: web app on http://localhost:5173 (proxies /api to the API)
cd MTGPriceTracker.Web
npm install
npm run dev
```

Open http://localhost:5173. Swagger is available at `http://localhost:5190/swagger` in Development.

The SQLite database is created automatically on first run and is git-ignored (`mtg-prices-dev.db` in Development, `mtg-prices.db` otherwise).

In a published build the API serves the built React app itself from `wwwroot/`, so there is only one process (see [Deployment](#deployment)).

### First-time data setup

The database starts empty. Open **Data Sync** from the **Manage** menu (`/sync`) and run, in order:

1. **Import catalog** — downloads `AllPrintings.json` (~50 MB compressed, several minutes)
2. **Import history** *(optional)* — one-time 90-day backfill from `AllPrices.json` (~150 MB, 10–20 minutes)
3. **Sync prices** — imports today's MTGJSON prices and scrapes Good Games

After that, the nightly job keeps prices current.

### Configuration

`MTGPriceTracker.Server/appsettings.json`:

| Key | Description |
|---|---|
| `Database:Path` | SQLite file path (default `mtg-prices.db`) |
| `Discord:WebhookUrl` | Webhook of a Discord **forum** channel for price alerts |
| `Discord:YellowThresholdPct` | Spike % at which alerts turn yellow (default 25; mid increment in price) |
| `Discord:RedThresholdPct` | Spike % at which alerts turn red (default 50; high increment in price) |

Keep your webhook URL out of source control. In development use [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (`dotnet user-secrets set "Discord:WebhookUrl" "<url>"`). User secrets are only loaded in Development, so on a server set the `Discord__WebhookUrl` environment variable instead. Any setting can be overridden this way, with `__` standing in for `:` (for example `Database__Path`).

## API overview

| Endpoint | Description |
|---|---|
| `GET /api/cards` | Search/browse cards (pagination, filters, sorting). Returns card info only, no prices |
| `GET /api/cards/{uuid}` | Card details, including latest prices |
| `GET /api/prices/{uuid}/history?days=90` | Price history per vendor |
| `GET /api/prices/{uuid}/latest` | Latest price per vendor |
| `GET/POST/DELETE /api/favorites` | Manage favorites |
| `GET /api/sync/status` | Sync progress and DB stats (price record total is approximate) |
| `POST /api/sync/trigger` | Full sync (MTGJSON + Good Games) |
| `POST /api/sync/trigger-goodgames` | Good Games scrape only |
| `POST /api/sync/import-catalog` · `import-history` | Catalog import / history backfill |
| `POST /api/notifications/discord/price-alerts` | Run alert check and post to Discord |

Full route list is in Swagger.

## Project structure

```
MTGPriceTracker/
├── MTGPriceTracker.Server/   ASP.NET Core API, EF Core, sync jobs (also serves the built web app)
├── MTGPriceTracker.Web/      React + Vite + TypeScript frontend
├── MTGPriceTracker.Shared/   DTOs used by the server
└── MTGPriceTracker.Client/   Legacy Blazor client, kept for reference and no longer built or served
deploy/
└── deploy.sh                 One-command deploy for a Linux server
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for the layer design, database schema, import strategy, performance notes and decisions log.

## Deployment

`dotnet publish` on the Server project also builds the React app (it runs `npm install` and `npm run build`) and ships it in `wwwroot/`, so one output folder is the whole application. The machine that publishes needs the .NET 8 SDK and Node 20.19+ or 22.12+.

An example Linux setup with systemd, using one folder for everything:

```
/opt/mtgpricetracker/
├── src/    git clone of this repo
├── app/    the published app (the service's working directory)
└── data/   the SQLite database, kept outside app/ so a deploy never touches it
```

```ini
# /etc/systemd/system/mtgpricetracker.service
[Unit]
Description=MTG Price Tracker
After=network.target

[Service]
WorkingDirectory=/opt/mtgpricetracker/app
ExecStart=/usr/bin/dotnet MTGPriceTracker.Server.dll
Restart=always
RestartSec=10
User=<service user>
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Environment=Database__Path=/opt/mtgpricetracker/data/mtg-prices.db
# Environment=Discord__WebhookUrl=...

[Install]
WantedBy=multi-user.target
```

To deploy, run `deploy/deploy.sh` on the server (for example via a symlink at `/opt/mtgpricetracker/deploy.sh`). It pulls, builds the new version next to the running one, swaps it in, and waits for the site to answer. If the new version does not come up it rolls back automatically, and `deploy.sh --rollback` goes back to the previous version on demand. Adjust the paths and service name at the top of the script for your setup.

Put the database on an SSD if you can. Search and card pages read from a large file and a spinning disk makes the first request after a quiet period very slow (see the performance notes in ARCHITECTURE.md).

## Roadmap

- Currency conversion with live exchange rates
- More Australian resellers
- User accounts / multi-user favorites
- Email/push alerts
- A small "latest price" table so list tiles can show prices without touching the main price table

## Credits

- Card and price data from [MTGJSON](https://mtgjson.com)
- Card imagery from [Scryfall](https://scryfall.com)
- `holo_effect.css` — foil shine/glare effect reference

Magic: The Gathering is a trademark of Wizards of the Coast. This project is unofficial and not affiliated with or endorsed by Wizards of the Coast.
