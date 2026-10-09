import { useCallback } from 'react'
import { Link } from 'react-router-dom'
import { Heart } from '@phosphor-icons/react'
import { useFavorites, useToggleFavorite } from '../api/hooks'
import type { CardDto } from '../api/types'
import CardGrid, { CardGridSkeleton } from '../components/CardGrid'
import { Alert, Button, EmptyState } from '../components/ui'

export default function Favorites() {
  const { data: favorites, isPending, error } = useFavorites()
  const toggle = useToggleFavorite()
  const onToggle = useCallback((card: CardDto) => toggle.mutate({ card, add: !card.isFavorite }), [toggle])

  return (
    <>
      <title>Favorites | MTG Price Tracker</title>

      <div className="mb-8 flex flex-wrap items-end justify-between gap-2">
        <h1 className="text-3xl font-semibold tracking-tight sm:text-4xl">Favorites</h1>
        {favorites && favorites.length > 0 && (
          <p className="num pb-1 text-sm text-muted">
            {favorites.length} {favorites.length === 1 ? 'card' : 'cards'}
          </p>
        )}
      </div>

      {error ? (
        <Alert tone="error">Could not load favorites: {(error as Error).message}</Alert>
      ) : isPending ? (
        <CardGridSkeleton count={12} />
      ) : favorites.length === 0 ? (
        <EmptyState
          icon={<Heart size={28} />}
          title="No favorites yet"
          action={
            <Link to="/cards">
              <Button variant="primary">Browse cards</Button>
            </Link>
          }
        >
          Tap the heart on any card to keep it here.
        </EmptyState>
      ) : (
        <CardGrid cards={favorites} onToggleFavorite={onToggle} />
      )}
    </>
  )
}
