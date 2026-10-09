import { useState, type ReactNode } from 'react'
import { ArrowSquareOut, MagnifyingGlass } from '@phosphor-icons/react'
import { api } from '../api/client'
import type {
  GoodGamesInspectResponse,
  GoodGamesProductInspectDto,
  GoodGamesVariantInspectDto,
} from '../api/types'
import { useToast } from '../components/Toast'
import { Alert, Badge, Button, Panel, TextInput, cx } from '../components/ui'
import { cleanText, formatNumber } from '../lib/format'

const mono = 'code text-xs'
const th = 'px-3 py-2 text-left text-xs font-medium text-muted whitespace-nowrap'
const td = 'px-3 py-2 whitespace-nowrap'

const formatPrice = (raw: string) => {
  const p = parseFloat(raw)
  return p > 0 ? `$${p.toFixed(2)}` : 'None'
}

function Tag({ children, tone = 'neutral' }: { children: ReactNode; tone?: 'neutral' | 'good' | 'warn' | 'bad' }) {
  return (
    <Badge
      className={cx(
        tone === 'good' && 'border-down/30 bg-down/10 text-down',
        tone === 'warn' && 'border-accent/35 bg-accent/10 text-accent',
        tone === 'bad' && 'border-up/35 bg-up/10 text-up',
      )}
    >
      {children}
    </Badge>
  )
}

function KV({ rows }: { rows: [string, ReactNode][] }) {
  return (
    <dl className="grid grid-cols-[9.5rem_1fr] gap-x-4 gap-y-2.5 text-sm">
      {rows.map(([k, v]) => (
        <div key={k} className="contents">
          <dt className="text-muted">{k}</dt>
          <dd className="min-w-0 break-words">{v}</dd>
        </div>
      ))}
    </dl>
  )
}

const Heading = ({ children, className }: { children: ReactNode; className?: string }) => (
  <h4 className={cx('mb-3 text-sm font-semibold tracking-tight', className)}>{children}</h4>
)

function VariantRow({ v }: { v: GoodGamesVariantInspectDto }) {
  const isNmSku = v.skuResolvedUuid !== null && v.parsedCondition === 'NM'
  return (
    <tr className={cx('border-t border-line', !v.available && 'opacity-45', isNmSku && 'bg-down/5')}>
      <td className={td}>{v.title}</td>
      <td className={td}>
        {v.parsedCondition ? (
          <Tag tone={v.parsedCondition === 'NM' ? 'good' : 'neutral'}>
            {v.parsedCondition}
            {v.isFoil ? ' foil' : ''}
          </Tag>
        ) : (
          'Unknown'
        )}
      </td>
      <td className={cx(td, 'num font-medium')}>{formatPrice(v.price)}</td>
      <td className={td}>{v.available ? 'In stock' : 'Out'}</td>
      <td className={cx(td, mono, 'text-accent')}>{v.sku || 'None'}</td>
      <td className={cx(td, mono)}>{v.skuSetCode ?? 'None'}</td>
      <td className={cx(td, mono, 'font-medium')}>{v.skuCollectorNumber ?? 'None'}</td>
      <td className={cx(td, 'text-muted')}>{v.skuLanguage ?? 'None'}</td>
      <td className={td}>{v.skuFinish ? <Tag>{v.skuFinish}</Tag> : 'None'}</td>
      <td className={cx(td, 'text-xs text-muted')}>{v.skuPriceType ?? 'None'}</td>
      <td className={cx(td, mono, v.skuResolvedUuid ? 'text-down' : 'text-muted/60')}>
        {v.skuResolvedUuid ? `${v.skuResolvedUuid.slice(0, 8)}...` : 'Not found'}
      </td>
    </tr>
  )
}

function Product({ p }: { p: GoodGamesProductInspectDto }) {
  const tone = p.matchMethod === 'SKU' ? 'good' : p.matchMethod === 'Fuzzy' ? 'warn' : 'bad'
  return (
    <Panel className="mb-6 p-5 sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="text-lg font-semibold tracking-tight">{p.title}</h3>
          <a
            href={p.productUrl}
            target="_blank"
            rel="noreferrer"
            className="mt-1 inline-flex items-center gap-1 text-xs text-muted transition hover:text-fg"
          >
            {p.handle}
            <ArrowSquareOut size={13} />
          </a>
        </div>
        <div className="flex flex-wrap gap-2">
          <Tag tone={tone}>
            {p.matchMethod === 'SKU' ? 'SKU exact match' : p.matchMethod === 'Fuzzy' ? 'Fuzzy match' : 'No match'}
          </Tag>
          {p.wouldWriteNonFoilPrice && <Tag>Writes NM</Tag>}
          {p.wouldWriteFoilPrice && <Tag>Writes foil</Tag>}
        </div>
      </div>

      <div className="mt-6 grid gap-8 lg:grid-cols-2">
        <div>
          <Heading>Raw Shopify data</Heading>
          <KV
            rows={[
              ['Shopify ID', <span className={mono}>{p.shopifyId}</span>],
              ['Product type', p.productType || 'None'],
              ['Vendor', p.vendor || 'None'],
              [
                `Tags (${p.tags.length})`,
                p.tags.length ? (
                  <span className="flex flex-wrap gap-1.5">
                    {p.tags.map((t) => (
                      <Tag key={t}>{t}</Tag>
                    ))}
                  </span>
                ) : (
                  <span className="text-muted">None</span>
                ),
              ],
            ]}
          />
        </div>

        <div>
          <Heading>Title parse and fuzzy match</Heading>
          <KV
            rows={[
              ['Card name', p.parsedCardName],
              ['Set (from title)', p.parsedSetName ?? 'None'],
              ['Art variant', p.parsedArtVariant ?? 'None'],
              ['Recognised art', p.isRecognisedArtVariant ? 'Yes (strict)' : 'No (relaxed)'],
              ['Resolved set code', p.resolvedSetCode ?? 'Could not resolve'],
              ['NM price (normal)', <span className="num">{p.nmNormalPrice != null ? `A$${p.nmNormalPrice.toFixed(2)}` : 'None'}</span>],
              ['NM price (foil)', <span className="num">{p.nmFoilPrice != null ? `A$${p.nmFoilPrice.toFixed(2)}` : 'None'}</span>],
              ['Fuzzy reason', <span className="text-xs text-muted">{cleanText(p.matchReason)}</span>],
            ]}
          />
        </div>
      </div>

      {p.skuSnapshots.length > 0 && (
        <div className="mt-8 overflow-x-auto">
          <Heading>Snapshots to write ({p.skuSnapshots.length})</Heading>
          <table className="w-full text-sm">
            <thead>
              <tr>
                {['SKU used', 'Set', '#', 'Frame', 'Border', 'Price type', 'Price (AUD)', 'UUID'].map((h) => (
                  <th key={h} className={th}>
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {p.skuSnapshots.map((s) => (
                <tr key={`${s.uuid}-${s.priceType}-${s.skuUsed}`} className="border-t border-line">
                  <td className={cx(td, mono, 'text-accent')}>{s.skuUsed}</td>
                  <td className={td}>{s.setCode}</td>
                  <td className={cx(td, mono, 'font-medium')}>{s.collectorNumber ?? 'None'}</td>
                  <td className={cx(td, 'text-muted')}>{s.frameEffects ?? 'None'}</td>
                  <td className={cx(td, 'text-muted')}>{s.borderColor ?? 'black'}</td>
                  <td className={td}>
                    <Tag>{s.priceType}</Tag>
                  </td>
                  <td className={cx(td, 'num font-medium')}>${s.price.toFixed(2)}</td>
                  <td className={cx(td, mono, 'text-muted')}>{s.uuid}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="mt-8">
        {p.matched ? (
          <>
            <Heading>MTGJSON card (representative)</Heading>
            <KV
              rows={[
                ['UUID', <span className={mono}>{p.matchedUuid}</span>],
                ['Set', `${p.matchedSetCode} ${p.matchedSetName}`],
                ['Collector number', <span className={cx(mono, 'font-medium')}>{p.matchedCollectorNumber ?? 'None'}</span>],
                ['Frame effects', p.matchedFrameEffects ?? 'None (standard)'],
                ['Border color', p.matchedBorderColor ?? 'black (default)'],
                [
                  'Finishes',
                  <span className="flex flex-wrap gap-1.5">
                    {p.matchedHasNonFoil && <Tag>Non-foil</Tag>}
                    {p.matchedHasFoil && <Tag>Foil</Tag>}
                    {p.matchedHasEtched && <Tag>Etched</Tag>}
                  </span>,
                ],
              ]}
            />
          </>
        ) : (
          <Alert tone="error">{cleanText(p.matchReason)}</Alert>
        )}
      </div>

      <div className="mt-8 overflow-x-auto">
        <Heading>All variants ({p.variants.length})</Heading>
        <table className="w-full text-sm">
          <thead>
            <tr>
              {['Title', 'Condition', 'Price', 'Stock', 'SKU', 'Set', 'Collector #', 'Lang', 'Finish', 'Price type', 'SKU UUID'].map(
                (h) => (
                  <th key={h} className={th}>
                    {h}
                  </th>
                ),
              )}
            </tr>
          </thead>
          <tbody>
            {p.variants.map((v) => (
              <VariantRow key={v.id} v={v} />
            ))}
          </tbody>
        </table>
      </div>
    </Panel>
  )
}

export default function GoodGamesInspector() {
  const toast = useToast()
  const [term, setTerm] = useState('the one ring')
  const [loading, setLoading] = useState(false)
  const [result, setResult] = useState<GoodGamesInspectResponse | null>(null)

  const run = async () => {
    if (!term.trim()) return
    setLoading(true)
    setResult(null)
    try {
      setResult(await api.inspectGoodGames(term.trim()))
    } catch (e) {
      toast(`Error: ${(e as Error).message}`, 'error')
    } finally {
      setLoading(false)
    }
  }

  return (
    <>
      <title>GG Inspector | MTG Price Tracker</title>

      <h1 className="text-3xl font-semibold tracking-tight sm:text-4xl">Good Games Inspector</h1>
      <p className="mt-2 max-w-[62ch] text-sm leading-relaxed text-muted">
        Fetches live data from tcg.goodgames.com.au and shows every Shopify field (tags, SKUs, barcodes) plus how each
        product is matched, by SKU collector number or title. Read-only: nothing is written to the database.
      </p>

      <form
        className="mt-8 flex flex-col gap-3 sm:flex-row"
        onSubmit={(e) => {
          e.preventDefault()
          void run()
        }}
      >
        <TextInput
          wrapperClass="flex-1 sm:max-w-md"
          icon={<MagnifyingGlass size={18} />}
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          placeholder="Card name, for example the one ring"
          aria-label="Card name to inspect"
        />
        <Button type="submit" variant="primary" size="lg" loading={loading} className="h-11">
          {loading ? 'Fetching' : 'Inspect'}
        </Button>
      </form>

      {loading && (
        <p className="mt-6 text-sm text-muted" role="status">
          Paginating through the Good Games collection. This can take 30 to 60 seconds.
        </p>
      )}

      {result && (
        <div className="mt-10">
          <div className="mb-8 flex flex-wrap items-center gap-2">
            <Tag>Scanned {formatNumber(result.totalScanned)}</Tag>
            <Tag>Found {result.totalMatched}</Tag>
            <Tag tone="good">SKU exact {result.skuMatched}</Tag>
            <Tag tone="warn">Fuzzy {result.fuzzyMatched}</Tag>
            <Tag tone="bad">Unmatched {result.totalMatched - result.skuMatched - result.fuzzyMatched}</Tag>
            <span className="num ml-auto text-xs text-muted">{result.fetchedAt}</span>
          </div>

          {result.products.length === 0 && (
            <Alert tone="warning">No products found for "{result.searchTerm}". Check the spelling.</Alert>
          )}
          {result.products.map((p) => (
            <Product key={p.shopifyId} p={p} />
          ))}
        </div>
      )}
    </>
  )
}
