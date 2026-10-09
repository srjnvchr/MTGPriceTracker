import type { CardDto } from '../api/types'

// Approximate exchange rates, same constants as the Blazor client.
const USD_TO_AUD = 1.55
const AUD_TO_USD = 0.645
const EUR_TO_USD = 1.08
export const RATES = { usdToAud: USD_TO_AUD, eurToAud: 1.7 }

export type Currency = 'USD' | 'AUD' | 'EUR'

export function convertPrice(price: number, from: string, to: Currency): number {
  if (from === to) return price
  const inUsd = from === 'EUR' ? price * EUR_TO_USD : from === 'AUD' ? price * AUD_TO_USD : price
  return to === 'AUD' ? inUsd * USD_TO_AUD : to === 'EUR' ? inUsd / EUR_TO_USD : inUsd
}

export const currencySymbol = (c: string) => (c === 'EUR' ? '€' : c === 'AUD' ? 'A$' : '$')

export const scryfallImage = (card: Pick<CardDto, 'scryfallId'>): string | null => {
  const id = card.scryfallId
  return id ? `https://cards.scryfall.io/normal/front/${id[0]}/${id[1]}/${id}.jpg` : null
}

// Rarity escalates through the neutral ramp; only mythic uses the accent.
export const rarityClass = (rarity: string) => {
  switch (rarity.toLowerCase()) {
    case 'mythic':
      return 'text-accent'
    case 'rare':
      return 'text-fg'
    case 'uncommon':
      return 'text-muted'
    default:
      return 'text-muted/70'
  }
}

export const VENDOR_SHORT: Record<string, string> = {
  tcgplayer: 'TCG',
  cardkingdom: 'CK',
  cardmarket: 'CM',
  goodgames: 'GG',
}
const VENDOR_CURRENCY: Record<string, string> = { cardmarket: 'EUR', goodgames: 'AUD' }

/** Picks the headline retail price for a vendor from a card's LatestPrices map. */
export function vendorPrice(card: Pick<CardDto, 'latestPrices' | 'hasNonFoil'>, vendor: string) {
  const keys = Object.keys(card.latestPrices).filter((k) => k.startsWith(`${vendor}_`) && k.includes('retail'))
  if (keys.length === 0) return null
  const wantFoil = !card.hasNonFoil
  const pool = keys.filter((k) => k.includes('foil') === wantFoil)
  const list = pool.length ? pool : keys
  const key = list.find((k) => k.endsWith('_NM')) ?? list[0]
  const cur = VENDOR_CURRENCY[vendor] ?? 'USD'
  return { vendor, label: VENDOR_SHORT[vendor] ?? vendor.slice(0, 3).toUpperCase(), value: card.latestPrices[key], currency: cur }
}

/** Up to two prices for the tile caption, preferring TCGPlayer and Good Games. */
export function tilePrices(card: Pick<CardDto, 'latestPrices' | 'hasNonFoil'>) {
  const order = ['tcgplayer', 'goodgames', 'cardkingdom', 'cardmarket']
  return order
    .map((v) => vendorPrice(card, v))
    .filter((p): p is NonNullable<typeof p> => p !== null)
    .slice(0, 2)
}

export const formatDateTime = (iso: string) =>
  new Date(iso).toLocaleString('en-AU', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })

export const formatNumber = (n: number) => n.toLocaleString('en-AU')

/** Server strings sometimes contain em/en dashes; the UI uses plain hyphens. */
export const cleanText = (s: string) => s.replace(/\s*[—–]\s*/g, ' - ')
