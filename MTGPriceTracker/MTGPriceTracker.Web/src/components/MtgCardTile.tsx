import { memo, type KeyboardEvent, type MouseEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { Heart } from '@phosphor-icons/react'
import type { CardDto } from '../api/types'
import { currencySymbol, rarityClass, tilePrices } from '../lib/format'
import { cx } from './ui'
import TiltCard from './TiltCard'

/** Height of the caption under the artwork. The grid needs this to size rows. */
export const TILE_CAPTION_HEIGHT = 58

interface Props {
  card: CardDto
  onToggleFavorite: (card: CardDto) => void
}

function MtgCardTile({ card, onToggleFavorite }: Props) {
  const navigate = useNavigate()
  const open = () => navigate(`/cards/${card.uuid}`)
  const prices = tilePrices(card)

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault()
      open()
    }
  }
  const onFav = (e: MouseEvent) => {
    e.stopPropagation()
    onToggleFavorite(card)
  }

  return (
    <div className="group flex h-full flex-col">
      <div
        role="link"
        tabIndex={0}
        aria-label={`${card.name}, ${card.setCode}`}
        onClick={open}
        onKeyDown={onKeyDown}
        className="cursor-pointer rounded-[5%/3.5%] outline-offset-4 transition-transform duration-300 ease-out-expo active:scale-[0.985]"
      >
        <TiltCard card={card} max={8} size="normal">
          <button
            aria-label={card.isFavorite ? `Remove ${card.name} from favorites` : `Add ${card.name} to favorites`}
            aria-pressed={card.isFavorite}
            onClick={onFav}
            className={cx(
              'absolute right-2 top-2 z-10 grid size-8 place-items-center rounded-full bg-black/55 text-white',
              'backdrop-blur-md transition duration-200 hover:bg-black/75 active:scale-90',
              card.isFavorite
                ? 'opacity-100'
                : 'opacity-0 focus-visible:opacity-100 group-focus-within:opacity-100 group-hover:opacity-100',
            )}
          >
            <Heart size={17} weight={card.isFavorite ? 'fill' : 'regular'} className={card.isFavorite ? 'text-accent' : ''} />
          </button>
        </TiltCard>
      </div>

      <div className="mt-3 min-w-0 px-0.5" style={{ height: TILE_CAPTION_HEIGHT - 12 }}>
        <p className="truncate text-[13px] font-medium leading-tight">{card.name}</p>
        {prices.length > 0 ? (
          <p className="num mt-1.5 flex gap-3 text-xs text-muted">
            {prices.map((p) => (
              <span key={p.vendor} className="whitespace-nowrap">
                <span className="text-muted/70">{p.label}</span>{' '}
                <span className="text-fg">
                  {currencySymbol(p.currency)}
                  {p.value.toFixed(2)}
                </span>
              </span>
            ))}
          </p>
        ) : (
          <p className="mt-1.5 text-xs text-muted/70">
            <span className={rarityClass(card.rarity)}>{card.rarity}</span>, no prices yet
          </p>
        )}
      </div>
    </div>
  )
}

export default memo(MtgCardTile)
