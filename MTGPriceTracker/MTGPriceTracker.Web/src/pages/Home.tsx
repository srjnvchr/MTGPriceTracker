import { useCallback, useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { motion, useReducedMotion } from 'motion/react'
import { ArrowRight, Cards, MagnifyingGlass } from '@phosphor-icons/react'
import { api } from '../api/client'
import { useFavorites, useSyncStatus, useToggleFavorite } from '../api/hooks'
import type { CardDto } from '../api/types'
import FannedCards from '../components/FannedCards'
import MtgCardTile from '../components/MtgCardTile'
import Reveal from '../components/Reveal'
import { Alert, Button, Skeleton, TextInput } from '../components/ui'
import { formatDateTime, formatNumber } from '../lib/format'

const VENDORS = [
  { name: 'TCGPlayer', detail: 'USD, retail and buylist. Imported daily from MTGJSON.' },
  { name: 'Card Kingdom', detail: 'USD, retail and buylist. Imported daily from MTGJSON.' },
  { name: 'CardMarket', detail: 'EUR, retail. Imported daily from MTGJSON.' },
  { name: 'Good Games AU', detail: 'AUD, by condition (NM to damaged) with foil variants. Scraped nightly.' },
]

/** Real cards from the catalog for the hero: mythics first, anything with art as a fallback. */
function useFeaturedCards() {
  return useQuery({
    queryKey: ['featured-cards'],
    staleTime: 10 * 60_000,
    queryFn: async () => {
      const base = { page: 1, pageSize: 24, sortBy: 'name', sortDescending: false }
      const mythic = await api.getCards({ ...base, rarity: 'mythic' })
      const pool = mythic.items.length >= 5 ? mythic.items : (await api.getCards(base)).items
      return pool.filter((c) => c.scryfallId).slice(0, 5)
    },
  })
}

export default function Home() {
  const [search, setSearch] = useState('')
  const navigate = useNavigate()
  const reduce = useReducedMotion()
  const { data: stats } = useSyncStatus()
  const { data: featured, isPending: featuredPending } = useFeaturedCards()
  const { data: favorites } = useFavorites()
  const toggle = useToggleFavorite()
  const onToggle = useCallback((c: CardDto) => toggle.mutate({ card: c, add: !c.isFavorite }), [toggle])

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const q = search.trim()
    navigate(q ? `/cards?query=${encodeURIComponent(q)}` : '/cards')
  }

  const rise = (delay: number) =>
    reduce
      ? {}
      : {
          initial: { opacity: 0, y: 20 },
          animate: { opacity: 1, y: 0 },
          transition: { duration: 0.7, delay, ease: [0.16, 1, 0.3, 1] as const },
        }

  return (
    <>
      <title>Home | MTG Price Tracker</title>

      {/* Hero */}
      <section className="grid items-center gap-12 pt-4 lg:min-h-[min(40rem,calc(100dvh-10rem))] lg:grid-cols-[1.05fr_0.95fr] lg:gap-8">
        <div className="max-w-xl">
          <motion.h1
            {...rise(0)}
            className="text-4xl font-semibold leading-[1.05] tracking-tighter sm:text-5xl lg:text-6xl"
          >
            Track MTG card prices across every vendor.
          </motion.h1>
          <motion.p {...rise(0.08)} className="mt-5 max-w-[46ch] text-base leading-relaxed text-muted sm:text-lg">
            TCGPlayer, Card Kingdom, CardMarket and Good Games AU, with daily price history.
          </motion.p>

          <motion.form {...rise(0.16)} onSubmit={submit} className="mt-8 flex flex-col gap-3 sm:flex-row" role="search">
            <TextInput
              wrapperClass="flex-1"
              icon={<MagnifyingGlass size={18} />}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Try Black Lotus or Lightning Bolt"
              aria-label="Search cards by name"
              className="h-12"
            />
            <Button type="submit" variant="primary" size="lg">
              Search
            </Button>
          </motion.form>

          <motion.div {...rise(0.22)} className="mt-4">
            <Link
              to="/cards"
              className="group inline-flex items-center gap-1.5 text-sm font-medium text-muted transition hover:text-fg"
            >
              Browse all cards
              <ArrowRight size={16} className="transition-transform duration-200 group-hover:translate-x-0.5" />
            </Link>
          </motion.div>
        </div>

        <div className="min-w-0">
          {featuredPending ? (
            <Skeleton className="mx-auto aspect-[5/4.4] w-full max-w-[560px] rounded-3xl" />
          ) : featured && featured.length > 0 ? (
            <FannedCards cards={featured} />
          ) : (
            <div className="mx-auto flex aspect-[5/4.4] w-full max-w-[560px] flex-col items-center justify-center gap-3 rounded-3xl border border-dashed border-line p-8 text-center">
              <Cards size={36} className="text-muted" />
              <p className="max-w-[28ch] text-sm leading-relaxed text-muted">
                The catalog is empty. Import it from Data Sync and your cards will appear here.
              </p>
            </div>
          )}
        </div>
      </section>

      {stats?.totalCards === 0 && (
        <Alert tone="info" className="mt-10 max-w-2xl">
          First time setup: open{' '}
          <Link to="/sync" className="font-medium text-accent underline underline-offset-4">
            Data Sync
          </Link>{' '}
          and run Import Card Catalog (about 3 to 10 minutes). Then run a price sync to fill in prices.
        </Alert>
      )}

      {/* Library stats */}
      <Reveal className="mt-20 grid gap-10 border-t border-line pt-10 sm:grid-cols-3">
        {[
          { label: 'Cards tracked', value: stats ? formatNumber(stats.totalCards) : null },
          { label: 'Price records', value: stats ? formatNumber(stats.totalPriceSnapshots) : null },
          { label: 'Last sync', value: stats ? (stats.lastSyncAt ? formatDateTime(stats.lastSyncAt) : 'Never') : null },
        ].map((s) => (
          <div key={s.label}>
            {s.value === null ? (
              <Skeleton className="h-10 w-40" />
            ) : (
              <p className="num text-3xl font-medium tracking-tight sm:text-4xl">{s.value}</p>
            )}
            <p className="mt-2 text-sm text-muted">{s.label}</p>
          </div>
        ))}
      </Reveal>

      {/* Favorites row, only when there is something to show */}
      {favorites && favorites.length > 0 && (
        <Reveal className="mt-24">
          <div className="mb-6 flex items-end justify-between gap-4">
            <h2 className="text-2xl font-semibold tracking-tight">Your favorites</h2>
            <Link to="/favorites" className="text-sm font-medium text-muted transition hover:text-fg">
              View all {favorites.length}
            </Link>
          </div>
          <div className="snap-row -mx-4 flex gap-6 overflow-x-auto px-4 pb-4 sm:-mx-6 sm:px-6">
            {favorites.slice(0, 12).map((c) => (
              <div key={c.uuid} className="w-[176px] shrink-0">
                <MtgCardTile card={c} onToggleFavorite={onToggle} />
              </div>
            ))}
          </div>
        </Reveal>
      )}

      {/* Sources */}
      <Reveal className="mt-24 grid gap-10 lg:grid-cols-[0.8fr_1.2fr] lg:gap-16">
        <h2 className="max-w-[16ch] text-3xl font-semibold leading-tight tracking-tight sm:text-4xl">
          Four vendors, three currencies.
        </h2>
        <ul className="divide-y divide-line">
          {VENDORS.map((v) => (
            <li key={v.name} className="grid gap-1 py-5 first:pt-0 sm:grid-cols-[11rem_1fr] sm:gap-6">
              <span className="font-medium">{v.name}</span>
              <span className="text-sm leading-relaxed text-muted">{v.detail}</span>
            </li>
          ))}
        </ul>
      </Reveal>
    </>
  )
}
