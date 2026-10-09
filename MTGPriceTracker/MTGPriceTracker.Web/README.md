# MTG Price Tracker web client

React 19 + Vite + TypeScript frontend for the MTG Price Tracker API.

- Styling: Tailwind CSS v4 with CSS-variable tokens (dark and light themes)
- Motion: `motion` (card tilt, foil glare, reveals). All motion respects `prefers-reduced-motion`
- Icons: Phosphor. Type: Geist and Geist Mono (self-hosted via Fontsource)
- Data: TanStack Query (cached, optimistic favorites) and TanStack Virtual (card grid)
- Charts: ApexCharts

## Develop

```bash
# terminal 1: API on http://localhost:5190
dotnet run --project ../MTGPriceTracker.Server

# terminal 2: this app on http://localhost:5173 (proxies /api to the server)
npm install
npm run dev
```

`npm run build` type-checks and writes the production bundle to `dist/`.
