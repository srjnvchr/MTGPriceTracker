# MTG Price Tracker — Architecture & Project Map

## Overview

A React single-page app and an ASP.NET Core 8 API for tracking Magic: The Gathering card prices across multiple vendors including TCGPlayer, Card Kingdom, CardMarket, and local Australian resellers like Good Games. Price history is sourced from MTGJSON daily builds and web scraping, stored in SQLite for historic trend analysis.

In production there is one process: the API serves the built React app from `wwwroot/` and answers `/api/*`. During development the React dev server and the API run separately.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React 19, TypeScript, Vite, React Router |
| Styling | Tailwind CSS v4 with CSS-variable design tokens (dark and light themes) |
| UI details | Motion (tilt, glare, reveals), Phosphor icons, Geist and Geist Mono fonts |
| Server state | TanStack Query (caching, optimistic updates), TanStack Virtual (card grid) |
| Charts | ApexCharts via `react-apexcharts` |
| Backend | ASP.NET Core 8 Web API |
| Database | SQLite via Entity Framework Core 8 |
| ORM | Entity Framework Core (Code-First) |
| Scraping | HtmlAgilityPack |
| Price Data | MTGJSON API (daily builds) |
| Background Jobs | IHostedService |
| Notifications | Discord webhook (forum-channel threads) |

---

## Solution Structure

```
MTGPriceTracker/                  (repo root)
├── deploy/deploy.sh              ← One-command server deploy with automatic rollback
└── MTGPriceTracker/
    ├── MTGPriceTracker.sln
    ├── MTGPriceTracker.Server/   ← ASP.NET Core Web API, also serves the built web app
    ├── MTGPriceTracker.Web/      ← React + Vite + TypeScript frontend
    ├── MTGPriceTracker.Shared/   ← DTOs used by the server
    └── MTGPriceTracker.Client/   ← Legacy Blazor WebAssembly client (kept, no longer built or served)
```

---

## Backend Architecture (Server)

### Design Principles (SOLID)
- **S** — Each service/repository has a single focused responsibility
- **O** — Services are extended via interfaces without modifying existing code
- **L** — Implementations are substitutable through their interfaces
- **I** — Interfaces are focused (ICardRepository vs IPriceRepository vs IFavoriteRepository)
- **D** — All dependencies injected via constructor; controllers depend on abstractions

### Layer Diagram
```
Controllers
    └── Services (ICardService, IPriceService, IFavoritesService,
    │              IMtgJsonService, IGoodGamesScraperService,
    │              IDiscordNotificationService)
    └── Repositories (ICardRepository, ICardSetRepository,
    │                 IPriceRepository, IFavoriteRepository)
            └── AppDbContext (EF Core → SQLite)
```

### Key Server Files

```
MTGPriceTracker.Server/
├── Models/                     ← EF Core domain entities
│   ├── Card.cs
│   ├── CardSet.cs
│   ├── PriceSnapshot.cs
│   └── UserFavorite.cs
├── Data/
│   ├── AppDbContext.cs         ← EF Core DbContext + seeding
│   └── SqliteTuningInterceptor.cs ← Per-connection SQLite pragmas (cache, mmap, temp store)
├── Repositories/
│   ├── Interfaces/
│   │   ├── ICardRepository.cs
│   │   ├── ICardSetRepository.cs
│   │   ├── IPriceRepository.cs
│   │   └── IFavoriteRepository.cs
│   ├── CardRepository.cs       ← Name/rarity/type search with LIKE, paging, sorting
│   ├── CardSetRepository.cs
│   ├── PriceRepository.cs      ← Latest-price lookup (+ per-card cache), history, bulk upsert
│   └── FavoriteRepository.cs
├── Services/
│   ├── Interfaces/
│   │   ├── ICardService.cs
│   │   ├── IPriceService.cs
│   │   ├── IFavoritesService.cs
│   │   ├── IMtgJsonService.cs
│   │   ├── IGoodGamesScraperService.cs
│   │   └── IDiscordNotificationService.cs
│   ├── CardService.cs          ← Search/browse (card info only), card detail with latest prices
│   ├── PriceService.cs         ← Price history queries and aggregation
│   ├── FavoritesService.cs     ← Manage user favorites (card info only)
│   ├── MtgJsonService.cs       ← Downloads + imports MTGJSON catalog, daily prices, history backfill
│   ├── GoodGamesScraperService.cs ← Scrapes tcg.goodgames.com.au (Shopify JSON)
│   └── DiscordNotificationService.cs ← Finds price-spike alerts, posts to Discord
├── BackgroundServices/
│   ├── PriceSyncBackgroundService.cs ← Daily sync job at 1:00 AM AEST
│   ├── SyncState.cs            ← Singleton sync status shared with SyncController and the price cache
│   └── DatabaseWarmupService.cs ← Runs a few representative queries at startup
├── Controllers/
│   ├── CardsController.cs      ← GET /api/cards, /api/cards/{uuid}, /api/cards/count
│   ├── PricesController.cs     ← GET /api/prices/{uuid}/history, /latest
│   ├── FavoritesController.cs  ← GET /api/favorites, GET {uuid}/status, POST/DELETE {uuid}
│   ├── SyncController.cs       ← Sync triggers, status, imports, GG inspector (see below)
│   └── NotificationsController.cs ← POST /api/notifications/discord/price-alerts
├── Program.cs                  ← DI, named HttpClients, WAL pragmas, column migrations,
│                                  static files + SPA fallback for the web app
└── appsettings.json
```

Legacy placeholder files (CardPriceController, CardPriceService, WebScraperService,
WeatherForecastController) are empty/template leftovers kept to avoid git conflicts.

### Sync API (`/api/sync`)
| Endpoint | Purpose |
|---|---|
| `GET status` | Sync state + total cards / price snapshots (the price total is approximate, see Performance) |
| `POST trigger` | Full sync (MTGJSON prices → Good Games) |
| `POST trigger-goodgames` | Good Games only, bypasses "already synced today" guard |
| `POST import-catalog` | AllPrintings import (first-time setup) |
| `POST import-history` | One-time 90-day price backfill |
| `DELETE prices/{vendor}` | Delete all snapshots for a vendor |
| `GET inspect-goodgames?search=` | Read-only dry run of GG matching for debugging |

### Serving the web app
`Program.cs` calls `UseStaticFiles()` and `MapFallbackToFile("index.html")`, so `wwwroot/` holds the built React app and deep links such as `/cards/{uuid}` fall back to `index.html` (React Router takes over). The server project has a `BuildReactApp` MSBuild target that runs `npm install` and `npm run build` in `MTGPriceTracker.Web` during `dotnet publish` and copies `dist/` into `wwwroot/`. Pass `-p:SkipWebBuild=true` to reuse an existing `dist/`.

---

## Frontend Architecture (Web)

### Pages
| Route | Page | Description |
|---|---|---|
| `/` | Home.tsx | Hero search with a fan of real cards, library stats, favorites row, vendor list |
| `/cards` | Cards.tsx | Sticky filter bar (name, rarity, sort, favorites only) over an infinite-scroll virtualized grid |
| `/cards/:uuid` | CardDetail.tsx | Tilting card art, lowest current price and per-vendor prices, history chart, card text |
| `/favorites` | Favorites.tsx | The user's favorited cards |
| `/sync` | Sync.tsx | Trigger imports/syncs, view progress, send Discord alerts, clear Good Games prices |
| `/debug/goodgames` | GoodGamesInspector.tsx | Good Games product matching inspector |

The Sync and Inspector pages are admin tools; they share the design tokens but are intentionally plain. Both, and the card detail page, are code-split so the first load stays small.

### Key Web Files
```
MTGPriceTracker.Web/src/
├── api/
│   ├── types.ts            ← Hand-written types mirroring the server DTOs (camelCase JSON)
│   ├── client.ts           ← All HTTP calls in one object
│   └── hooks.ts            ← TanStack Query hooks, including the optimistic favorite toggle
├── components/
│   ├── Layout.tsx          ← Single-line top nav, Manage menu, theme toggle, mobile sheet
│   ├── CardGrid.tsx        ← Window-virtualized grid; columns and tile width follow the container
│   ├── MtgCardTile.tsx     ← Tile: art, favorite button, name, set code and rarity
│   ├── TiltCard.tsx        ← 3D tilt and cursor glare from Motion values; foil/matte finish layers
│   ├── FannedCards.tsx     ← Hero card fan with pointer parallax
│   ├── Reveal.tsx          ← Scroll-in reveal
│   ├── Toast.tsx           ← Transient messages
│   └── ui.tsx              ← Button, inputs, segmented control, switch, alert, skeleton, panel, badge
├── lib/
│   ├── theme.tsx           ← Light/dark/system theme, persisted in localStorage
│   └── format.ts           ← Currency conversion, image URL, rarity colour, date/number formatting
├── pages/
├── index.css               ← Tailwind import, design tokens, card art / foil / skeleton styles
├── App.tsx                 ← Routes and lazy loading
└── main.tsx                ← Providers (Query, Theme, Toast, Router)
```

### Data layer
- **Server state** lives in TanStack Query. Defaults: data is fresh for 60 s, kept for 10 min, no refetch on window focus. Revisiting a page shows cached data immediately and refreshes in the background.
- **Query keys:** `['cards', filters]` (infinite), `['card', uuid]`, `['prices', uuid, days]`, `['favorites']`, `['sync-status']`, `['featured-cards']`.
- **Favorites are optimistic.** `useToggleFavorite` patches every cache that shows a card (grid pages, detail, favorites list), then refreshes the favorites list and any "favorites only" search so rows appear or disappear correctly.
- **Card list vs card detail.** List and search responses carry no prices. Prices load on the card page from `/api/prices/{uuid}/history` and the card detail response.
- **Sync status** is polled every 3 s on the Sync page only.

### Design system
- **Tokens:** colours, surfaces and lines are CSS variables, swapped by `data-theme` or `prefers-color-scheme`. One accent (ember orange, the mythic colour) is used everywhere. Rarity escalates through the neutral ramp and only mythic uses the accent. Price rises use `--up` and falls `--down`, because the app is for buyers (a falling price is good news).
- **Shape rule:** controls (buttons, inputs, chips) are pills, containers use a 16 px radius, and card art keeps the physical card corner radius.
- **Numbers** use Geist's tabular figures; Geist Mono is only for codes (SKUs, UUIDs).
- **Motion** is limited to hierarchy, feedback and state changes, and everything is disabled under `prefers-reduced-motion`. Pointer-driven effects use Motion values, never React state.
- **Copy:** the UI uses plain hyphens, never em or en dashes. Strings that come from the server go through `cleanText`. Card rules and flavor text are left untouched because they are card content.
- **Grid:** `CardGrid` virtualizes rows against the window scroll. Tile width stretches to fill the row, row height is derived from the card ratio (63 x 88) plus a fixed caption height, and the next page loads when the last rows approach the viewport.
- **Currency display** uses fixed approximate rates in `lib/format.ts` (USD 1 = AUD 1.55, EUR 1 = AUD 1.70, and so on), noted on the chart.

---

## Performance

Card pages read from a very large table, so the design keeps most requests away from it. The production database is about 42 GB, and `PriceSnapshots` holds nearly all of it.

**What each request touches**

| Request | Tables read | Notes |
|---|---|---|
| Card list, search, favorites | `Cards` (+ `CardSets`, `UserFavorites`) | No prices. A name search measured 12 to 15 ms on the real catalog |
| Card detail | `Cards`, then `PriceSnapshots` for one card | Latest prices plus 90-day history for a single card |
| Sync status | `Cards`, `PriceSnapshots` (max id only) | See "approximate count" below |

**Techniques in use**
- **Prices are fetched per card, on demand.** Loading latest prices for 48 cards per page used to dominate every list and search request (it took 70 to 116 s for popular cards on a spinning disk). The list no longer asks for prices at all.
- **Latest price = newest row per series.** `PriceRepository` reads only the newest row of each vendor/price type/condition with a correlated `MAX(Date)` on the unique index, instead of loading a card's whole history and picking in memory. Results are cached per card for 30 minutes, keyed on the last sync time, and the cache is bypassed while a sync is running so half-imported data is never kept.
- **Approximate price record count.** `COUNT(*)` scans the whole table. The status endpoint reports the highest row id instead, which is a single lookup. It overstates the real count by the number of rows ever deleted (for example after "Clear Good Games Prices"), because ids are never reused.
- **Search uses SQLite `LIKE`** (case-insensitive for ASCII, wildcards escaped) and compares rarity directly against the stored lowercase value so its index can be used. It is still a table scan, which is fine at about 100,000 cards. If the catalog grows or search needs ranking, an FTS5 index over card names is the next step.
- **SQLite connection tuning.** `SqliteTuningInterceptor` sets a 64 MB page cache, 256 MB of memory-mapped reads and in-memory temp storage on every connection (these pragmas are not stored in the file). WAL mode and `synchronous=NORMAL` are set at startup.
- **Startup warm-up.** `DatabaseWarmupService` runs a representative search and price lookup so the first request does not pay for EF model building, JIT and cold caches.
- **Frontend.** Code splitting keeps the initial bundle small, and TanStack Query makes revisits instant.

**Hardware matters.** The same queries ran in seconds or minutes depending on whether the pages were in the operating system's file cache. A 42 GB file cannot fit in 14 GB of RAM, so cold reads are unavoidable and storage speed decides how bad they are. Moving the database from a 5400 rpm hard disk to an SSD removed most of the pain. Keep the database file on an SSD.

**Known limits**
- Opening a card for the first time after a quiet period still reads its price history from disk.
- Price tiles are not shown on list pages. A small "latest price" table updated during sync would make that cheap if it is ever wanted.
- The price cache has no size cap; it only holds cards that were actually opened, and entries expire after 30 minutes.

---

## Deployment

One folder holds everything, on an SSD:

```
/opt/mtgpricetracker/
├── src/    git clone of the repo
├── app/    published app (the systemd service's WorkingDirectory); previous version kept in app.old
└── data/   SQLite database, outside app/ so deploys can never touch it
```

- **Service:** `mtgpricetracker.service` runs `dotnet MTGPriceTracker.Server.dll` on port 5000 with `ASPNETCORE_ENVIRONMENT=Production`. A reverse proxy (nginx) forwards to it; TLS is handled there.
- **Settings by environment variable**, because `dotnet publish` overwrites `appsettings.json`: `Database__Path` for the database location and `Discord__WebhookUrl` for alerts. User secrets only work in Development.
- **`deploy/deploy.sh`:** pulls, publishes to `app.new` while the site keeps running, stops the service, swaps `app.new` into place, starts it, and polls `/api/cards/count`. If the new version does not answer within a minute it restores the previous one automatically. `deploy.sh --rollback` switches back on demand. Publishing needs the .NET 8 SDK and Node 20.19+ or 22.12+.
- **Backups:** the live database lives only on the SSD, so back up `data/` regularly (a scheduled copy to a second disk is the simplest option).

---

## Database Schema

### Cards
| Column | Type | Notes |
|---|---|---|
| Uuid | TEXT PK | MTGJSON UUID |
| Name | TEXT | Card name |
| SetCode | TEXT FK | → CardSets.Code |
| Rarity | TEXT | common/uncommon/rare/mythic (stored lowercase) |
| Type | TEXT | Creature, Instant, etc. |
| ManaCost | TEXT | e.g. {2}{U}{U} |
| Text | TEXT | Oracle text |
| ScryfallId | TEXT | For image URLs |
| Artist | TEXT | |
| FlavorText, Power, Toughness, Loyalty, ColorIdentity | TEXT | Nullable card attributes |
| FrameEffects | TEXT | Comma-separated (showcase, extendedart, etched, fullart...); used for GG matching |
| BorderColor | TEXT | "borderless" / "black" / "white" |
| HasNonFoil / HasFoil / HasEtched | INTEGER | Available finishes (from MTGJSON `finishes`) |
| CollectorNumber | TEXT | MTGJSON `number`; enables exact SKU matching against Good Games |

Columns added after first release are applied at startup by `ALTER TABLE` in `Program.cs` (EnsureCreated does not alter existing tables).

### CardSets
| Column | Type | Notes |
|---|---|---|
| Code | TEXT PK | e.g. "MH3" |
| Name | TEXT | Full set name |
| ReleaseDate | TEXT | ISO date |

### PriceSnapshots
| Column | Type | Notes |
|---|---|---|
| Id | INTEGER PK | Auto-increment, never reused (used for the approximate row count) |
| CardUuid | TEXT FK | → Cards.Uuid |
| Vendor | TEXT | tcgplayer/cardkingdom/cardmarket/goodgames |
| PriceType | TEXT | retail/buylist/retail_foil (and other foil variants) |
| Condition | TEXT | NM/LP/MP/HP/DMG/NM_FOIL/LP_FOIL/… Null for MTGJSON market prices |
| Currency | TEXT | USD/EUR/AUD |
| Price | REAL | Price value |
| Date | TEXT | ISO date (YYYY-MM-DD) |

Unique index: `(CardUuid, Vendor, PriceType, Condition, Date)` — SQLite treats each NULL as distinct, so rows with a null condition (MTGJSON prices) are not protected from duplicates if the same date is imported twice. Readers tolerate this: the latest-price query returns one row per series and any duplicate at the newest date is equivalent. The same index serves the `MAX(Date)` lookups.

### UserFavorites
| Column | Type | Notes |
|---|---|---|
| Id | INTEGER PK | |
| CardUuid | TEXT FK | → Cards.Uuid |
| AddedAt | TEXT | ISO timestamp |

---

## Vendor & Pricing

### MTGJSON Vendors (auto-imported daily)
| Vendor | Currency | Price Types |
|---|---|---|
| tcgplayer | USD | retail, buylist |
| cardkingdom | USD | retail, buylist |
| cardmarket | EUR | retail |

### Local Vendors (scraped)
| Vendor | Currency | Method | Conditions |
|---|---|---|---|
| goodgames | AUD | Shopify JSON API (tcg.goodgames.com.au) | NM, LP, MP, HP, DMG + foil variants |

### Currency Conversion
- Stored in native currency
- AUD/USD/EUR conversion applied at display time, using fixed approximate rates in the web app
- Live exchange rates are a future item

---

## MTGJSON Import Strategy

The imports download these files from `https://mtgjson.com/api/v5/`:

1. **AllPrintings.json.gz** (~50MB compressed) — Card catalog
   - Streamed and parsed with `System.Text.Json`
   - Upserts cards and sets in batches of 500
   - Run once initially (`POST /api/sync/import-catalog`), then only for new sets

2. **AllPricesToday.json** — Nightly price data
   - Imported by the daily sync; one new date's snapshot per run
   - Full historic retention from the first import date onward

3. **AllPrices.json** (~150MB) — One-time 90-day history backfill
   - `POST /api/sync/import-history`; downloaded to a temp file to avoid in-memory buffer overflow
   - Skips automatically if more than one distinct date already exists

---

## Good Games Scraper

- Target: `https://tcg.goodgames.com.au`
- Strategy: Paginate the Shopify `/collections/magic-the-gathering/products.json` endpoint (250 products/page) — no HTML parsing
- Each product returns all variants (NM, LP, MP, HP, DMG, foil) with prices as clean JSON
- Matching (primary): variant SKU `{SetCode}-{CollectorNumber}-{Lang}-{Finish}-{Ver}` resolved against `Cards.SetCode` + `CollectorNumber`, respecting `HasNonFoil`/`HasFoil`
- Matching (fallback): product title parsed to card name (text before `[set name]`) and matched by name (case-insensitive, first UUID wins)
- Rate limiting: 500 ms between pages
- Scheduled: Runs after the MTGJSON import in the nightly sync job; `force` flag bypasses the once-per-day guard for manual runs
- Stored as `goodgames` vendor with AUD currency and per-condition `Condition` column
- Collection handle is `magic-the-gathering`; if the scraper returns 0 results, check the URL path on tcg.goodgames.com.au and update `CollectionHandle` in `GoodGamesScraperService.cs`
- Debug with the `/debug/goodgames` page or `GET /api/sync/inspect-goodgames`

---

## Background Sync Schedule

A single job (`PriceSyncBackgroundService`) runs at **1:00 AM AEST** and executes sequentially:

1. Import MTGJSON prices
2. Scrape Good Games prices

Progress and last result are exposed via `SyncState` / `GET /api/sync/status`. Discord alerts are **not** part of the nightly job yet; they are triggered manually from the Sync page.

---

## Discord Price Alerts

- `DiscordNotificationService.FindAlertsAsync` looks for cards whose TCGPlayer or Card Kingdom retail price (foil and non-foil) rose >= 10% over the last 7 days and is >= $3, **and** whose Good Games NM price has not itself risen >= 10% (a buying opportunity before local repricing). Cards with no Good Games price are skipped; one alert (best jump) per card.
- `PostAlertsAsync` posts to a Discord **forum** webhook: one thread per UTC day (cached in memory), alerts sent as embed batches of 10, grouped green/moderate (< 25%), yellow/significant (>= 25%), red/high-value (>= 50%). Thresholds come from `Discord:YellowThresholdPct` / `RedThresholdPct`.
- The controller queries within the request, then posts in a background task so client timeouts can't cancel the Discord calls.
- Webhook URL is read from `Discord:WebhookUrl`; keep it in user secrets (development) or the `Discord__WebhookUrl` environment variable (server), never in `appsettings.json`.

---

## Future Roadmap

- [x] Discord price alerts (manual trigger) — implemented
- [x] React frontend with dark/light themes — implemented
- [x] Card condition tracking (NM, LP, MP, HP, DMG) — implemented for Good Games
- [x] Foil price tracking — implemented (NM_FOIL, LP_FOIL, …)
- [ ] Run Discord alerts automatically after the nightly sync
- [ ] Rule-based notification system (e.g. "alert when TCGPlayer > Good Games AUD")
- [ ] Latest-price table (one row per card/vendor/condition, updated during sync) so list tiles can show prices cheaply
- [ ] FTS5 index for card name search if the catalog grows
- [ ] Scheduled database backup
- [ ] Currency conversion with live rates
- [ ] User accounts / multi-user favorites
- [ ] More Australian resellers (Auggies, Magic Madhouse AU)
- [ ] Price alerts via email/push
- [ ] Remove the legacy Blazor client project once it is no longer needed for reference

---

## Key Decisions Log

| Date | Decision | Reason |
|---|---|---|
| 2026-04-02 | SQLite over PostgreSQL | Simpler local setup; can migrate to Postgres later with same EF Core code |
| 2026-04-02 | Blazor WASM hosted over Blazor Server | Better scalability; API-first lets us add mobile later (superseded by the React port, 2026-10-09) |
| 2026-04-02 | Repository pattern | Testability and clean separation of data access from business logic |
| 2026-04-02 | MTGJSON as primary data source | Official MTG data with daily price updates across all major vendors |
| 2026-04-02 | Blazor-ApexCharts for price charts | Rich interactive time-series charts vs MudBlazor's basic MudChart (the React app keeps ApexCharts) |
| 2026-04-02 | Store prices in native currency | Avoids stale conversion data; convert at display time |
| 2026-04-03 | Condition column on PriceSnapshot | Good Games sells NM/LP/MP/HP/DMG separately; MTGJSON is condition-agnostic (null) |
| 2026-04-03 | Shopify JSON API over HTML scraping for Good Games | Structured JSON from /collections/{handle}/products.json is more reliable, captures all variants in one pass, and doesn't break when the theme changes |
| 2026-04-03 | IServiceScopeFactory in SyncController | IServiceProvider injected into controllers is request-scoped and disposed on response; IServiceScopeFactory is singleton and safe for fire-and-forget background tasks |
| 2026-04-05 | Split catalog import, daily price import and history backfill into separate triggers | Decouples heavy one-time downloads from the nightly job; sync status shared through a `SyncState` singleton |
| 2026-04-05 | SKU-based exact matching for Good Games (name match as fallback) | SKUs encode set + collector number + finish, avoiding wrong-printing matches; needed `CollectorNumber`, `FrameEffects`, `BorderColor`, `Has*` finish columns |
| 2026-04-05 | Startup `ALTER TABLE` column migrations + SQLite WAL | EnsureCreated doesn't alter existing tables; WAL + `synchronous=NORMAL` speeds bulk imports |
| 2026-04-05 | Show full Scryfall card image as the card tile | Better browsing than cropped art |
| 2026-04-07 | Discord alerts via webhook; DB query in request, posting in background `Task.Run` | Posting many embeds exceeded client timeouts and crashed the server |
| 2026-04-07 | Alert only on international price jumps where Good Games hasn't repriced | Highlights actual buying opportunities rather than every spike |
| 2026-04-22 | One Discord forum thread per day, embeds grouped by severity | Keeps the channel tidy; high-value alerts sit at the bottom of the thread |
| 2026-05-01 | Fix card grid layout so all cards display | Cards page grid was dropping items |
| 2026-09-29 | Discord webhook moved out of `appsettings.json` into user secrets / environment variables | The URL had been committed; secrets and local config files are now git-ignored |
| 2026-10-09 | React 19 + Vite + TypeScript replaces Blazor WebAssembly | Much smaller first load (no .NET runtime or 9 MB component library), larger ecosystem; the API is unchanged |
| 2026-10-09 | Tailwind v4 design tokens, Motion and Phosphor icons instead of a component library | A custom collectibles look (card tilt, foil, ember accent), dark and light themes from CSS variables, one icon family |
| 2026-10-09 | TanStack Query for server state, TanStack Virtual for the card grid | Cached revisits, optimistic favorites, smooth infinite scroll over thousands of cards |
| 2026-10-09 | Case-insensitive `LIKE` search, SQLite connection tuning, startup warm-up job | Avoid per-row `lower()` scans, give SQLite a larger cache, and keep the first request after a restart from paying for cold caches |
| 2026-10-10 | The server serves the React build, built by `dotnet publish` | One deployable artifact and one process; Node is only needed on the machine that publishes |
| 2026-10-10 | Latest price = newest row per series, cached per card | The old group-and-take-first query read each card's whole history and took minutes on the 42 GB table |
| 2026-10-11 | Card list, search and favorites return no prices | Prices live in the huge table and the list only needs card info; this removed the 70 to 116 s searches |
| 2026-10-11 | Price record total = highest row id instead of `COUNT(*)` | `COUNT(*)` scans the whole table; the id is an instant lookup, at the cost of overstating after deletions |
| 2026-10-11 | Database moved from a 5400 rpm hard disk to the SSD, outside the app folder | Cold reads were the main source of slowness; a separate data folder means a publish can never overwrite the database |
| 2026-10-11 | Single `/opt/mtgpricetracker` layout and `deploy.sh` with automatic rollback | Replaced four scattered folders and a manual, error-prone sequence; a failed build or unhealthy start leaves the previous version running |
