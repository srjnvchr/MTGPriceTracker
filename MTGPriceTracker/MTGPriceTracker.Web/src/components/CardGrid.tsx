import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useWindowVirtualizer } from '@tanstack/react-virtual'
import type { CardDto } from '../api/types'
import { Skeleton } from './ui'
import MtgCardTile, { TILE_CAPTION_HEIGHT } from './MtgCardTile'

const GAP = 24
const MIN_TILE_WIDTH = 164
const CARD_RATIO = 88 / 63

function useGridMetrics(ref: React.RefObject<HTMLDivElement | null>) {
  const [m, setM] = useState({ columns: 6, tileWidth: MIN_TILE_WIDTH, top: 0 })
  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    const measure = () => {
      const w = el.clientWidth
      const columns = Math.max(2, Math.floor((w + GAP) / (MIN_TILE_WIDTH + GAP)))
      const tileWidth = (w - GAP * (columns - 1)) / columns
      setM({ columns, tileWidth, top: el.getBoundingClientRect().top + window.scrollY })
    }
    measure()
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [ref])
  return m
}

interface Props {
  cards: CardDto[]
  onToggleFavorite: (card: CardDto) => void
  /** Called when the user scrolls near the end of the list. */
  onEndReached?: () => void
}

/** Window-scrolled, row-virtualised grid. Tiles stretch to fill the width. */
export default function CardGrid({ cards, onToggleFavorite, onEndReached }: Props) {
  const ref = useRef<HTMLDivElement>(null)
  const { columns, tileWidth, top } = useGridMetrics(ref)

  const rowHeight = Math.round(tileWidth * CARD_RATIO + TILE_CAPTION_HEIGHT)
  const rowCount = Math.ceil(cards.length / columns)

  const virtualizer = useWindowVirtualizer({
    count: rowCount,
    estimateSize: () => rowHeight + GAP,
    overscan: 3,
    scrollMargin: top,
  })

  useEffect(() => {
    virtualizer.measure()
  }, [rowHeight, virtualizer])

  const items = virtualizer.getVirtualItems()
  const lastIndex = items.length ? items[items.length - 1].index : -1

  useEffect(() => {
    if (onEndReached && lastIndex >= rowCount - 3) onEndReached()
  }, [lastIndex, rowCount, onEndReached])

  return (
    <div ref={ref} style={{ position: 'relative', height: virtualizer.getTotalSize() }}>
      {items.map((row) => (
        <div
          key={row.key}
          style={{
            position: 'absolute',
            top: 0,
            left: 0,
            width: '100%',
            height: rowHeight,
            display: 'grid',
            gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
            gap: GAP,
            transform: `translateY(${row.start - top}px)`,
          }}
        >
          {cards.slice(row.index * columns, (row.index + 1) * columns).map((card) => (
            <MtgCardTile key={card.uuid} card={card} onToggleFavorite={onToggleFavorite} />
          ))}
        </div>
      ))}
    </div>
  )
}

/** Placeholder shaped like the real grid while the first page loads. */
export function CardGridSkeleton({ count = 18 }: { count?: number }) {
  return (
    <div className="grid gap-6" style={{ gridTemplateColumns: `repeat(auto-fill, minmax(${MIN_TILE_WIDTH}px, 1fr))` }}>
      {Array.from({ length: count }, (_, i) => (
        <div key={i}>
          <Skeleton className="aspect-[63/88] w-full rounded-[5%/3.5%]" />
          <Skeleton className="mt-3 h-3.5 w-3/4" />
          <Skeleton className="mt-2 h-3 w-1/2" />
        </div>
      ))}
    </div>
  )
}
