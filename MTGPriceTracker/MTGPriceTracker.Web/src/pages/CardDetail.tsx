import { useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import Chart from 'react-apexcharts'
import type { ApexOptions } from 'apexcharts'
import { ArrowDownRight, ArrowUpRight, CaretRight, ChartLine, Heart, WarningCircle } from '@phosphor-icons/react'
import { useCard, usePriceHistory, useToggleFavorite } from '../api/hooks'
import type { VendorPriceHistoryDto } from '../api/types'
import TiltCard from '../components/TiltCard'
import Reveal from '../components/Reveal'
import { Alert, Button, EmptyState, Panel, Segmented, Skeleton } from '../components/ui'
import { useTheme } from '../lib/theme'
import { type Currency, RATES, cleanText, convertPrice, currencySymbol, rarityClass } from '../lib/format'

const VENDOR_COLORS: Record<string, string> = {
  tcgplayer: '#5aa0e8',
  cardkingdom: '#e0a83e',
  cardmarket: '#5cc08f',
}
const ACCENT = { dark: '#e9794a', light: '#c2410c' }

function seriesColor(h: VendorPriceHistoryDto, mode: 'dark' | 'light'): string {
  const base = h.vendor === 'goodgames' ? ACCENT[mode] : (VENDOR_COLORS[h.vendor] ?? '#9aa0b4')
  if (h.priceType?.toLowerCase().includes('foil')) return `${base}b3`
  if (h.condition === 'LP' || h.condition === 'MP' || h.condition === 'HP') return `${base}cc`
  return base
}

const RANGES = [
  { value: 7, label: '1W' },
  { value: 30, label: '1M' },
  { value: 90, label: '3M' },
]
const CURRENCIES: { value: Currency; label: string }[] = [
  { value: 'USD', label: 'USD' },
  { value: 'AUD', label: 'AUD' },
]

const pctChange = (h: VendorPriceHistoryDto): number | null => {
  const pts = h.pricePoints
  if (pts.length < 2) return null
  const prev = pts[pts.length - 2].price
  return prev === 0 ? null : ((pts[pts.length - 1].price - prev) / prev) * 100
}

function Change({ pct }: { pct: number | null }) {
  if (pct === null || Math.abs(pct) < 0.05) return null
  const up = pct > 0
  return (
    <span className={`num inline-flex items-center gap-0.5 text-xs ${up ? 'text-up' : 'text-down'}`}>
      {up ? <ArrowUpRight size={13} /> : <ArrowDownRight size={13} />}
      {Math.abs(pct).toFixed(1)}%
    </span>
  )
}

function DetailSkeleton() {
  return (
    <div className="grid grid-cols-1 gap-12 lg:grid-cols-[minmax(0,380px)_minmax(0,1fr)]">
      <Skeleton className="aspect-[63/88] w-full rounded-[5%/3.5%]" />
      <div className="space-y-6">
        <Skeleton className="h-10 w-2/3" />
        <Skeleton className="h-5 w-1/3" />
        <Skeleton className="h-40 w-full rounded-2xl" />
        <Skeleton className="h-80 w-full rounded-2xl" />
      </div>
    </div>
  )
}

export default function CardDetail() {
  const { uuid = '' } = useParams()
  const { resolved } = useTheme()
  const [days, setDays] = useState(90)
  const [currency, setCurrency] = useState<Currency>('AUD')

  const { data: card, isPending, error } = useCard(uuid)
  const { data: histories = [], isFetching: refetching, isPending: pricesPending } = usePriceHistory(uuid, days)
  const toggle = useToggleFavorite()

  // Buylist prices are hidden to reduce noise; Good Games conditions always show.
  const withData = useMemo(
    () =>
      histories.filter(
        (h) =>
          h.pricePoints.length > 0 &&
          (h.vendor === 'goodgames' || h.priceType === 'retail' || h.priceType === 'retail_foil'),
      ),
    [histories],
  )

  const current = useMemo(
    () =>
      withData
        .map((h) => {
          const latest = h.pricePoints[h.pricePoints.length - 1].price
          return {
            key: `${h.vendor}-${h.priceType}-${h.condition}`,
            name: cleanText(h.displayName),
            value: convertPrice(latest, h.currency, currency),
            pct: pctChange(h),
          }
        })
        .sort((a, b) => a.value - b.value),
    [withData, currency],
  )

  const series = useMemo(
    () =>
      withData.map((h) => ({
        name: cleanText(h.displayName),
        data: h.pricePoints
          .map((p) => ({ x: new Date(p.date).getTime(), y: convertPrice(p.price, h.currency, currency) }))
          .sort((a, b) => a.x - b.x),
      })),
    [withData, currency],
  )

  const options = useMemo<ApexOptions>(() => {
    const text = resolved === 'dark' ? '#9b9ba8' : '#62626d'
    return {
      chart: {
        type: 'line',
        background: 'transparent',
        foreColor: text,
        fontFamily: 'Geist Variable, sans-serif',
        toolbar: { show: false },
        zoom: { enabled: true, type: 'x' },
        animations: { enabled: true, speed: 500 },
      },
      colors: withData.map((h) => seriesColor(h, resolved)),
      theme: { mode: resolved },
      stroke: { curve: 'smooth', width: 2 },
      legend: { show: true, position: 'bottom', labels: { colors: text }, markers: { size: 5 } },
      xaxis: {
        type: 'datetime',
        labels: { style: { colors: text, fontSize: '11px' } },
        axisBorder: { show: false },
        axisTicks: { show: false },
      },
      yaxis: {
        labels: {
          style: { colors: text, fontSize: '11px' },
          formatter: (v: number | null) => (v == null ? '' : `${currencySymbol(currency)}${v.toFixed(2)}`),
        },
      },
      grid: {
        borderColor: resolved === 'dark' ? 'rgba(255,255,255,0.07)' : 'rgba(0,0,0,0.08)',
        strokeDashArray: 3,
        xaxis: { lines: { show: false } },
      },
      tooltip: { theme: resolved, x: { format: 'dd MMM yyyy' }, shared: true },
      markers: { size: 0 },
      noData: { text: 'No price data' },
    }
  }, [withData, resolved, currency])

  if (isPending) return <DetailSkeleton />
  if (error || !card)
    return (
      <EmptyState
        icon={<WarningCircle size={28} />}
        title="Card not found"
        action={
          <Link to="/cards">
            <Button variant="primary">Back to cards</Button>
          </Link>
        }
      >
        That card is not in the catalog.
      </EmptyState>
    )

  const best = current[0]
  const rest = current.slice(1)

  return (
    <>
      <title>{`${card.name} | MTG Price Tracker`}</title>

      <nav aria-label="Breadcrumb" className="mb-8 flex items-center gap-1.5 text-sm text-muted">
        <Link to="/cards" className="transition hover:text-fg">
          Cards
        </Link>
        <CaretRight size={13} />
        <span className="truncate text-fg">{card.name}</span>
      </nav>

      <div className="grid grid-cols-1 items-start gap-12 lg:grid-cols-[minmax(0,380px)_minmax(0,1fr)] lg:gap-16">
        {/* Card + favorite */}
        <div className="mx-auto w-full max-w-[380px] lg:sticky lg:top-24">
          <TiltCard card={card} max={12} priority size="large" />
          <Button
            className="mt-6 w-full"
            variant={card.isFavorite ? 'secondary' : 'primary'}
            size="lg"
            aria-pressed={card.isFavorite}
            icon={<Heart size={19} weight={card.isFavorite ? 'fill' : 'regular'} className={card.isFavorite ? 'text-accent' : ''} />}
            onClick={() => toggle.mutate({ card, add: !card.isFavorite })}
          >
            {card.isFavorite ? 'In your favorites' : 'Add to favorites'}
          </Button>
        </div>

        <div className="min-w-0">
          {/* Identity */}
          <h1 className="text-4xl font-semibold leading-[1.05] tracking-tighter sm:text-5xl">{card.name}</h1>
          <p className="mt-3 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted">
            <span>
              {card.setName} <span className="num">({card.setCode})</span>
            </span>
            <span className={`font-medium capitalize ${rarityClass(card.rarity)}`}>{card.rarity}</span>
            {card.manaCost && <span className="num text-fg">{card.manaCost}</span>}
          </p>
          <p className="mt-1 text-sm text-muted">{cleanText(card.type)}</p>

          {/* Current prices */}
          <section className="mt-10" aria-label="Current prices">
            <h2 className="text-lg font-semibold tracking-tight">Current prices</h2>
            {pricesPending ? (
              <Skeleton className="mt-4 h-36 w-full rounded-2xl" />
            ) : !best ? (
              <Alert className="mt-4">No prices yet. Run a price sync to populate data.</Alert>
            ) : (
              <div className="mt-4 grid gap-6 sm:grid-cols-[minmax(0,15rem)_1fr]">
                <Panel className="flex flex-col justify-between p-5">
                  <p className="text-sm text-muted">Lowest right now</p>
                  <p className="num mt-3 text-4xl font-medium tracking-tight">
                    {currencySymbol(currency)}
                    {best.value.toFixed(2)}
                  </p>
                  <p className="mt-2 text-sm leading-snug">{best.name}</p>
                  <div className="mt-1">
                    <Change pct={best.pct} />
                  </div>
                </Panel>
                {rest.length > 0 && (
                  <ul className="grid grid-cols-2 gap-3 xl:grid-cols-3">
                    {rest.map((r) => (
                      <li key={r.key} className="rounded-2xl bg-surface-2/70 p-4">
                        <p className="truncate text-xs text-muted" title={r.name}>
                          {r.name}
                        </p>
                        <p className="num mt-1.5 text-lg font-medium">
                          {currencySymbol(currency)}
                          {r.value.toFixed(2)}
                        </p>
                        <Change pct={r.pct} />
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            )}
          </section>

          {/* History */}
          <section className="mt-12" aria-label="Price history">
            <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
              <h2 className="text-lg font-semibold tracking-tight">Price history</h2>
              <div className="flex flex-wrap gap-2">
                <Segmented label="Currency" value={currency} onChange={setCurrency} options={CURRENCIES} />
                <Segmented label="Range" value={days} onChange={setDays} options={RANGES} />
              </div>
            </div>

            {pricesPending ? (
              <Skeleton className="h-[420px] w-full rounded-2xl" />
            ) : withData.length === 0 ? (
              <EmptyState icon={<ChartLine size={28} />} title="No price history yet">
                Run a price sync from Data Sync to start building history for this card.
              </EmptyState>
            ) : (
              <>
                <Panel className={`p-4 transition-opacity duration-200 ${refetching ? 'opacity-60' : ''}`}>
                  <Chart type="line" height={380} series={series} options={options} />
                </Panel>
                <p className="mt-3 text-xs leading-relaxed text-muted">
                  Exchange rates are approximate: USD 1 = AUD <span className="num">{RATES.usdToAud.toFixed(2)}</span>,
                  EUR 1 = AUD <span className="num">{RATES.eurToAud.toFixed(2)}</span>. Click a series in the legend to
                  hide it.
                </p>
              </>
            )}
          </section>

          {/* Card text */}
          {(card.text || card.flavorText || card.power || card.loyalty || card.artist) && (
            <Reveal className="mt-14">
              <section aria-label="Card text" className="max-w-prose">
                <h2 className="text-lg font-semibold tracking-tight">Card text</h2>
                {card.text && (
                  <p className="mt-4 whitespace-pre-line leading-relaxed text-fg/90">{card.text}</p>
                )}
                {card.flavorText && (
                  <p className="mt-4 italic leading-relaxed text-muted">{card.flavorText}</p>
                )}
                {(card.power || card.loyalty) && (
                  <p className="num mt-4 text-sm text-muted">
                    {card.power && card.toughness && <>Power / toughness {card.power} / {card.toughness}</>}
                    {card.loyalty && <>Loyalty {card.loyalty}</>}
                  </p>
                )}
                {card.artist && <p className="mt-4 text-sm text-muted">Illustrated by {card.artist}</p>}
              </section>
            </Reveal>
          )}
        </div>
      </div>
    </>
  )
}
