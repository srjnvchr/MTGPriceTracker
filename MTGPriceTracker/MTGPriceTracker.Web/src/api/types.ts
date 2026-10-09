// Mirrors MTGPriceTracker.Shared/DTOs. ASP.NET serialises with camelCase.

export interface CardDto {
  uuid: string
  name: string
  setCode: string
  setName: string
  rarity: string
  type: string
  manaCost: string | null
  scryfallId: string | null
  isFavorite: boolean
  hasFoil: boolean
  hasNonFoil: boolean
  latestPrices: Record<string, number>
}

export interface CardDetailDto extends CardDto {
  text: string | null
  flavorText: string | null
  power: string | null
  toughness: string | null
  artist: string | null
  loyalty: string | null
  colorIdentity: string | null
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

export interface CardSearchQuery {
  query?: string
  setCode?: string
  rarity?: string
  type?: string
  favoritesOnly?: boolean
  page: number
  pageSize: number
  sortBy: string
  sortDescending: boolean
}

export interface PricePointDto {
  date: string
  price: number
}

export interface VendorPriceHistoryDto {
  vendor: string
  displayName: string
  priceType: string
  condition: string | null
  currency: string
  pricePoints: PricePointDto[]
}

export interface SyncStatusDto {
  isRunning: boolean
  lastSyncAt: string | null
  lastSyncResult: string | null
  totalCards: number
  totalPriceSnapshots: number
  currentOperation: string | null
}

export interface SyncTriggerResponse {
  success: boolean
  message: string
}

export interface PriceAlertDto {
  cardUuid: string
  cardName: string
  setCode: string
  collectorNumber: string | null
  borderColor: string | null
  frameEffects: string | null
  hasFoil: boolean
  hasNonFoil: boolean
  vendor: string
  isFoil: boolean
  oldPrice: number
  newPrice: number
  changePercent: number
  currency: string
  oldDate: string
  newDate: string
  goodGamesPrice: number | null
}

export interface PriceAlertResultDto {
  success: boolean
  message: string
  alertCount: number
  alerts: PriceAlertDto[]
}

export interface GoodGamesVariantInspectDto {
  id: number
  title: string
  price: string
  available: boolean
  sku: string | null
  barcode: string | null
  option1: string | null
  option2: string | null
  parsedCondition: string | null
  isFoil: boolean
  skuSetCode: string | null
  skuCollectorNumber: string | null
  skuLanguage: string | null
  skuFinish: string | null
  skuPriceType: string | null
  skuResolvedUuid: string | null
}

export interface GoodGamesSkuSnapshotDto {
  uuid: string
  cardName: string
  setCode: string
  collectorNumber: string | null
  frameEffects: string | null
  borderColor: string | null
  priceType: string
  price: number
  skuUsed: string
}

export interface GoodGamesProductInspectDto {
  shopifyId: number
  title: string
  handle: string
  productType: string
  vendor: string
  productUrl: string
  tags: string[]
  variants: GoodGamesVariantInspectDto[]
  parsedCardName: string
  parsedSetName: string | null
  parsedArtVariant: string | null
  resolvedSetCode: string | null
  isRecognisedArtVariant: boolean
  nmNormalPrice: number | null
  nmFoilPrice: number | null
  matchMethod: 'SKU' | 'Fuzzy' | 'None'
  matched: boolean
  matchReason: string
  matchedUuid: string | null
  matchedSetCode: string | null
  matchedSetName: string | null
  matchedFrameEffects: string | null
  matchedBorderColor: string | null
  matchedHasNonFoil: boolean
  matchedHasFoil: boolean
  matchedHasEtched: boolean
  matchedCollectorNumber: string | null
  wouldWriteNonFoilPrice: boolean
  wouldWriteFoilPrice: boolean
  skuSnapshots: GoodGamesSkuSnapshotDto[]
}

export interface GoodGamesInspectResponse {
  searchTerm: string
  totalScanned: number
  totalMatched: number
  skuMatched: number
  fuzzyMatched: number
  fetchedAt: string
  products: GoodGamesProductInspectDto[]
}
