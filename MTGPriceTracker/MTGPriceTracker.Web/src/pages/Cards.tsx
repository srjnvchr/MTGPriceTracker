import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { ArrowDown, ArrowUp, MagnifyingGlass, SpinnerGap, Stack, X } from '@phosphor-icons/react'
import { type CardFilters, useCardsInfinite, useToggleFavorite } from '../api/hooks'
import type { CardDto } from '../api/types'
import CardGrid, { CardGridSkeleton } from '../components/CardGrid'
import { Alert, EmptyState, IconButton, Segmented, Select, Switch, TextInput } from '../components/ui'
import { formatNumber } from '../lib/format'

const PAGE_SIZE = 48

const RARITIES = [
  { value: '', label: 'Any' },
  { value: 'common', label: 'Common' },
  { value: 'uncommon', label: 'Uncommon' },
  { value: 'rare', label: 'Rare' },
  { value: 'mythic', label: 'Mythic' },
]

function useDebounced<T>(value: T, ms: number): T {
  const [v, setV] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms)
    return () => clearTimeout(t)
  }, [value, ms])
  return v
}

export default function Cards() {
  const [params] = useSearchParams()
  const [search, setSearch] = useState(params.get('query') ?? '')
  const [rarity, setRarity] = useState('')
  const [sortBy, setSortBy] = useState('name')
  const [descending, setDescending] = useState(false)
  const [favoritesOnly, setFavoritesOnly] = useState(false)

  // Follow the URL when arriving from the Home search box.
  const urlQuery = params.get('query')
  useEffect(() => {
    if (urlQuery !== null) setSearch(urlQuery)
  }, [urlQuery])

  const debouncedSearch = useDebounced(search, 300)

  const filters = useMemo<CardFilters>(
    () => ({
      query: debouncedSearch || undefined,
      rarity: rarity || undefined,
      favoritesOnly: favoritesOnly || undefined,
      pageSize: PAGE_SIZE,
      sortBy,
      sortDescending: descending,
    }),
    [debouncedSearch, rarity, favoritesOnly, sortBy, descending],
  )

  const { data, isPending, isFetching, isFetchingNextPage, hasNextPage, fetchNextPage, error } =
    useCardsInfinite(filters)
  const toggle = useToggleFavorite()

  const cards = useMemo(() => data?.pages.flatMap((p) => p.items) ?? [], [data])
  const totalCount = data?.pages[0]?.totalCount ?? 0

  const onToggle = useCallback((card: CardDto) => toggle.mutate({ card, add: !card.isFavorite }), [toggle])
  const loadMore = useCallback(() => {
    if (hasNextPage && !isFetchingNextPage) void fetchNextPage()
  }, [hasNextPage, isFetchingNextPage, fetchNextPage])

  const searching = isFetching && !isFetchingNextPage && !isPending

  return (
    <>
      <title>Cards | MTG Price Tracker</title>

      <div className="mb-6 flex flex-wrap items-end justify-between gap-x-6 gap-y-2">
        <h1 className="text-3xl font-semibold tracking-tight sm:text-4xl">All Cards</h1>
        <p className="num flex items-center gap-2 pb-1 text-sm text-muted" aria-live="polite">
          {searching && <SpinnerGap size={14} className="animate-spin motion-reduce:animate-none" />}
          {totalCount > 0 && `${formatNumber(cards.length)} of ${formatNumber(totalCount)} cards`}
        </p>
      </div>

      <div className="sticky top-16 z-20 -mx-4 mb-8 border-b border-line bg-bg/80 px-4 py-3 backdrop-blur-xl sm:-mx-6 sm:px-6">
        <div className="flex flex-wrap items-center gap-x-4 gap-y-3">
          <div className="relative min-w-[220px] flex-1 sm:max-w-sm">
            <TextInput
              icon={<MagnifyingGlass size={18} />}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search by name"
              aria-label="Search by name"
              className="pr-10"
            />
            {search && (
              <IconButton
                label="Clear search"
                onClick={() => setSearch('')}
                className="absolute right-1 top-1/2 size-8 -translate-y-1/2"
              >
                <X size={16} />
              </IconButton>
            )}
          </div>

          <div className="max-w-full overflow-x-auto">
            <Segmented label="Rarity" value={rarity} onChange={setRarity} options={RARITIES} />
          </div>

          <div className="flex items-center gap-1">
            <Select aria-label="Sort by" value={sortBy} onChange={(e) => setSortBy(e.target.value)} className="h-10 w-32">
              <option value="name">Name</option>
              <option value="set">Set</option>
              <option value="rarity">Rarity</option>
            </Select>
            <IconButton
              label={descending ? 'Sorted descending, switch to ascending' : 'Sorted ascending, switch to descending'}
              onClick={() => setDescending((d) => !d)}
              className="border border-line"
            >
              {descending ? <ArrowDown size={17} /> : <ArrowUp size={17} />}
            </IconButton>
          </div>

          <Switch checked={favoritesOnly} onChange={setFavoritesOnly} label="Favorites only" />
        </div>
      </div>

      {error ? (
        <Alert tone="error">Could not load cards: {(error as Error).message}</Alert>
      ) : isPending ? (
        <CardGridSkeleton />
      ) : cards.length === 0 ? (
        <EmptyState icon={<Stack size={28} />} title="No cards found">
          {debouncedSearch
            ? 'Nothing matches that search. Try fewer words or clear the filters.'
            : 'The catalog is empty. Import it from Data Sync to get started.'}
        </EmptyState>
      ) : (
        <>
          <CardGrid cards={cards} onToggleFavorite={onToggle} onEndReached={loadMore} />
          {isFetchingNextPage && (
            <div className="mt-6 flex justify-center text-muted">
              <SpinnerGap size={22} className="animate-spin motion-reduce:animate-none" />
            </div>
          )}
        </>
      )}
    </>
  )
}
