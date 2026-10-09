import {
  type InfiniteData,
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import { api } from './client'
import type { CardDetailDto, CardDto, CardSearchQuery, PagedResult } from './types'
import { useToast } from '../components/Toast'

export type CardFilters = Omit<CardSearchQuery, 'page'>

export function useCardsInfinite(filters: CardFilters) {
  return useInfiniteQuery({
    queryKey: ['cards', filters],
    queryFn: ({ pageParam }) => api.getCards({ ...filters, page: pageParam }),
    initialPageParam: 1,
    getNextPageParam: (last) =>
      last.page * last.pageSize < last.totalCount ? last.page + 1 : undefined,
    // Keep showing the previous results while a new search loads.
    placeholderData: (prev) => prev,
  })
}

export const useCard = (uuid: string) =>
  useQuery({ queryKey: ['card', uuid], queryFn: () => api.getCard(uuid) })

export const usePriceHistory = (uuid: string, days: number) =>
  useQuery({
    queryKey: ['prices', uuid, days],
    queryFn: () => api.getPriceHistory(uuid, days),
    placeholderData: (prev) => prev,
  })

export const useFavorites = () => useQuery({ queryKey: ['favorites'], queryFn: api.getFavorites })

export const useSyncStatus = (pollMs?: number) =>
  useQuery({
    queryKey: ['sync-status'],
    queryFn: api.getSyncStatus,
    refetchInterval: pollMs,
    retry: pollMs ? false : 1,
  })

/** Optimistically toggles a favourite across every cache that shows it. */
export function useToggleFavorite() {
  const qc = useQueryClient()
  const toast = useToast()

  return useMutation({
    mutationFn: ({ card, add }: { card: Pick<CardDto, 'uuid' | 'name'>; add: boolean }) =>
      add ? api.addFavorite(card.uuid) : api.removeFavorite(card.uuid),

    onMutate: ({ card, add }) => {
      qc.setQueriesData<InfiniteData<PagedResult<CardDto>>>({ queryKey: ['cards'] }, (data) =>
        data && {
          ...data,
          pages: data.pages.map((p) => ({
            ...p,
            items: p.items.map((c) => (c.uuid === card.uuid ? { ...c, isFavorite: add } : c)),
          })),
        },
      )
      qc.setQueryData<CardDetailDto>(['card', card.uuid], (d) => d && { ...d, isFavorite: add })
      qc.setQueryData<CardDto[]>(['favorites'], (d) =>
        d && (add ? d : d.filter((c) => c.uuid !== card.uuid)),
      )
    },

    onSuccess: (_r, { card, add }) =>
      toast(
        add ? `${card.name} added to favorites` : `${card.name} removed from favorites`,
        add ? 'success' : 'info',
      ),

    onError: (err: Error) => toast(`Error updating favorites: ${err.message}`, 'error'),

    onSettled: () => {
      qc.invalidateQueries({ queryKey: ['favorites'] })
      // A "favorites only" search must drop/add rows; other searches are already patched.
      qc.invalidateQueries({
        queryKey: ['cards'],
        predicate: (q) => (q.queryKey[1] as CardFilters | undefined)?.favoritesOnly === true,
      })
    },
  })
}
