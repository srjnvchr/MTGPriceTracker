import type {
  CardDetailDto,
  CardDto,
  CardSearchQuery,
  GoodGamesInspectResponse,
  PagedResult,
  PriceAlertResultDto,
  SyncStatusDto,
  SyncTriggerResponse,
  VendorPriceHistoryDto,
} from './types'

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init)
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  return (await res.json()) as T
}

async function send(url: string, method: 'POST' | 'DELETE'): Promise<void> {
  const res = await fetch(url, { method })
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
}

function cardsUrl(q: CardSearchQuery): string {
  const p = new URLSearchParams()
  if (q.query?.trim()) p.set('query', q.query.trim())
  if (q.setCode) p.set('setCode', q.setCode)
  if (q.rarity) p.set('rarity', q.rarity)
  if (q.type) p.set('type', q.type)
  if (q.favoritesOnly) p.set('favoritesOnly', 'true')
  p.set('page', String(q.page))
  p.set('pageSize', String(q.pageSize))
  p.set('sortBy', q.sortBy)
  p.set('sortDescending', String(q.sortDescending))
  return `/api/cards?${p}`
}

export const api = {
  // Cards
  getCards: (q: CardSearchQuery) => request<PagedResult<CardDto>>(cardsUrl(q)),
  getCard: (uuid: string) => request<CardDetailDto>(`/api/cards/${uuid}`),

  // Prices
  getPriceHistory: (uuid: string, days: number) =>
    request<VendorPriceHistoryDto[]>(`/api/prices/${uuid}/history?days=${days}`),

  // Favorites
  getFavorites: () => request<CardDto[]>('/api/favorites'),
  addFavorite: (uuid: string) => send(`/api/favorites/${uuid}`, 'POST'),
  removeFavorite: (uuid: string) => send(`/api/favorites/${uuid}`, 'DELETE'),

  // Sync
  getSyncStatus: () => request<SyncStatusDto>('/api/sync/status'),
  triggerSync: () => request<SyncTriggerResponse>('/api/sync/trigger', { method: 'POST' }),
  triggerCatalogImport: () =>
    request<SyncTriggerResponse>('/api/sync/import-catalog', { method: 'POST' }),
  triggerGoodGamesSync: () =>
    request<SyncTriggerResponse>('/api/sync/trigger-goodgames', { method: 'POST' }),
  triggerHistoryImport: () =>
    request<SyncTriggerResponse>('/api/sync/import-history', { method: 'POST' }),
  clearVendorPrices: (vendor: string) =>
    request<SyncTriggerResponse>(`/api/sync/prices/${encodeURIComponent(vendor)}`, {
      method: 'DELETE',
    }),
  inspectGoodGames: (search: string) =>
    request<GoodGamesInspectResponse>(
      `/api/sync/inspect-goodgames?search=${encodeURIComponent(search)}`,
    ),

  // Notifications
  triggerDiscordPriceAlerts: () =>
    request<PriceAlertResultDto>('/api/notifications/discord/price-alerts', { method: 'POST' }),
}
