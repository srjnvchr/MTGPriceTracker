import { useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import {
  BellRinging,
  CloudArrowDown,
  ClockCounterClockwise,
  Storefront,
  Trash,
  ArrowsClockwise,
} from '@phosphor-icons/react'
import { api } from '../api/client'
import { useSyncStatus } from '../api/hooks'
import type { PriceAlertResultDto, SyncTriggerResponse } from '../api/types'
import { useToast } from '../components/Toast'
import { Alert, Button, Skeleton, Spinner } from '../components/ui'
import { cleanText, formatDateTime, formatNumber } from '../lib/format'

const alertTone = (pct: number) => (pct >= 50 ? 'text-accent' : pct >= 25 ? 'text-fg' : 'text-muted')

function Stat({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <div className="num text-2xl font-medium tracking-tight sm:text-3xl">{children}</div>
      <p className="mt-1.5 text-sm text-muted">{label}</p>
    </div>
  )
}

function Group({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="mt-12">
      <h2 className="text-lg font-semibold tracking-tight">{title}</h2>
      <div className="mt-2 divide-y divide-line">{children}</div>
    </section>
  )
}

function ActionRow({
  icon,
  title,
  description,
  action,
  children,
}: {
  icon: ReactNode
  title: string
  description: ReactNode
  action: ReactNode
  children?: ReactNode
}) {
  return (
    <div className="py-6">
      <div className="grid items-center gap-4 sm:grid-cols-[1fr_auto] sm:gap-10">
        <div className="flex gap-4">
          <span className="mt-0.5 grid size-9 shrink-0 place-items-center rounded-xl bg-surface-2 text-muted">{icon}</span>
          <div className="min-w-0">
            <h3 className="font-medium">{title}</h3>
            <p className="mt-1 max-w-[62ch] text-sm leading-relaxed text-muted">{description}</p>
          </div>
        </div>
        <div className="sm:w-56">{action}</div>
      </div>
      {children}
    </div>
  )
}

export default function Sync() {
  const toast = useToast()
  const qc = useQueryClient()
  const { data: s, isError } = useSyncStatus(3000)
  const [clearing, setClearing] = useState(false)
  const [alertsRunning, setAlertsRunning] = useState(false)
  const [alertResult, setAlertResult] = useState<PriceAlertResultDto | null>(null)

  const refresh = () => qc.invalidateQueries({ queryKey: ['sync-status'] })

  const trigger = async (fn: () => Promise<SyncTriggerResponse>, failMsg: string) => {
    try {
      const r = await fn()
      toast(r.message || failMsg, r.success ? 'success' : 'warning')
    } catch {
      toast(failMsg, 'warning')
    }
    void refresh()
  }

  const clearGoodGames = async () => {
    setClearing(true)
    try {
      const r = await api.clearVendorPrices('goodgames')
      toast(r.message, r.success ? 'success' : 'error')
      void refresh()
    } catch {
      toast('Failed to clear prices', 'error')
    } finally {
      setClearing(false)
    }
  }

  const sendAlerts = async () => {
    setAlertsRunning(true)
    setAlertResult(null)
    try {
      const r = await api.triggerDiscordPriceAlerts()
      setAlertResult(r)
      toast(r.message, r.success ? 'success' : 'warning')
    } catch {
      toast('Error contacting the server.', 'error')
    } finally {
      setAlertsRunning(false)
    }
  }

  const busy = s?.isRunning ?? false
  const empty = !s || s.totalCards === 0

  return (
    <>
      <title>Data Sync | MTG Price Tracker</title>

      <h1 className="text-3xl font-semibold tracking-tight sm:text-4xl">Data Sync</h1>
      <p className="mt-2 max-w-[60ch] text-sm leading-relaxed text-muted">
        Import the card catalog, refresh prices and send alerts. Prices also sync on their own every day at 1:00 AM AEST.
      </p>

      {isError && (
        <Alert tone="warning" className="mt-6">
          Cannot reach the server. Is the backend running? Retrying every 3 seconds.
        </Alert>
      )}

      <div className="mt-10 grid gap-8 border-y border-line py-8 sm:grid-cols-4">
        <Stat label="Cards in database">{s ? formatNumber(s.totalCards) : <Skeleton className="h-8 w-24" />}</Stat>
        <Stat label="Price records">{s ? formatNumber(s.totalPriceSnapshots) : <Skeleton className="h-8 w-28" />}</Stat>
        <Stat label="Status">
          {s ? (
            <span className={busy ? 'text-accent' : ''}>{busy ? 'Running' : 'Idle'}</span>
          ) : (
            <Skeleton className="h-8 w-16" />
          )}
        </Stat>
        <Stat label="Last sync">
          {s ? (
            <span className="text-lg sm:text-xl">{s.lastSyncAt ? formatDateTime(s.lastSyncAt) : 'Never'}</span>
          ) : (
            <Skeleton className="h-8 w-36" />
          )}
        </Stat>
      </div>

      {busy && s?.currentOperation && (
        <Alert tone="info" className="mt-6">
          <span className="flex items-center gap-3">
            <Spinner className="size-4" />
            {cleanText(s.currentOperation)}
          </span>
        </Alert>
      )}
      {s?.lastSyncResult && (
        <Alert tone={s.lastSyncResult.startsWith('Error') ? 'error' : 'success'} className="mt-4">
          {cleanText(s.lastSyncResult)}
        </Alert>
      )}

      <Group title="Catalog">
        <ActionRow
          icon={<CloudArrowDown size={20} />}
          title="Import card catalog"
          description="Downloads every printing from MTGJSON (AllPrintings). One-time setup that takes 3 to 10 minutes. Run it again when new sets release."
          action={
            <Button
              className="w-full"
              variant="primary"
              disabled={busy}
              onClick={() => trigger(api.triggerCatalogImport, 'Failed to trigger import')}
            >
              Import catalog
            </Button>
          }
        />
      </Group>

      <Group title="Prices">
        <ActionRow
          icon={<ArrowsClockwise size={20} />}
          title="Sync prices now"
          description="Imports the latest MTGJSON prices (TCGPlayer, Card Kingdom, CardMarket), then scrapes Good Games AU."
          action={
            <Button
              className="w-full"
              variant="primary"
              disabled={busy || empty}
              onClick={() => trigger(api.triggerSync, 'Failed to trigger sync')}
            >
              Sync prices
            </Button>
          }
        />
        <ActionRow
          icon={<Storefront size={20} />}
          title="Sync Good Games only"
          description="Scrapes Near Mint to damaged prices from Good Games AU and stores them against the matched card. Runs immediately, skipping the once-per-day guard."
          action={
            <Button
              className="w-full"
              disabled={busy || empty}
              onClick={() => trigger(api.triggerGoodGamesSync, 'Failed to trigger Good Games sync')}
            >
              Sync Good Games
            </Button>
          }
        />
        <ActionRow
          icon={<ClockCounterClockwise size={20} />}
          title="Import 90-day history"
          description="Backfills 90 days of retail history from AllPrices (about 150 MB compressed, 10 to 20 minutes, around 1 GB of temporary disk). Skips itself if history already exists."
          action={
            <Button
              className="w-full"
              disabled={busy || empty}
              onClick={() => trigger(api.triggerHistoryImport, 'Failed to trigger history import')}
            >
              Import history
            </Button>
          }
        />
      </Group>

      <Group title="Alerts">
        <ActionRow
          icon={<BellRinging size={20} />}
          title="Discord buying opportunities"
          description="Finds cards where TCGPlayer or Card Kingdom rose 10% or more in the last 7 days (and cost at least $3) while Good Games has not repriced. Posts them to your Discord channel."
          action={
            <Button className="w-full" variant="primary" loading={alertsRunning} disabled={empty} onClick={sendAlerts}>
              {alertsRunning ? 'Checking prices' : 'Send alerts'}
            </Button>
          }
        >
          {alertResult && (
            <div className="mt-5 sm:ml-[3.25rem]">
              <Alert tone={alertResult.success ? 'success' : 'error'}>{cleanText(alertResult.message)}</Alert>
              {alertResult.success && alertResult.alertCount > 0 && (
                <div className="mt-4 overflow-x-auto">
                  <table className="w-full min-w-[520px] text-left text-sm">
                    <thead className="text-muted">
                      <tr>
                        <th className="py-2 pr-4 font-medium">Card</th>
                        <th className="py-2 pr-4 font-medium">Finish</th>
                        <th className="py-2 pr-4 text-right font-medium">Jump</th>
                        <th className="py-2 text-right font-medium">Good Games</th>
                      </tr>
                    </thead>
                    <tbody>
                      {alertResult.alerts.slice(0, 10).map((a) => (
                        <tr key={`${a.cardUuid}-${a.vendor}-${a.isFoil}`} className="border-t border-line">
                          <td className="py-2.5 pr-4">
                            {a.cardName}
                            <span className="num ml-2 text-xs text-muted">
                              {a.setCode}
                              {a.collectorNumber ? ` #${a.collectorNumber}` : ''}
                            </span>
                          </td>
                          <td className="py-2.5 pr-4 text-muted">
                            {a.isFoil ? 'Foil' : 'Non-foil'}
                            {a.borderColor && a.borderColor !== 'black' ? `, ${a.borderColor}` : ''}
                          </td>
                          <td className={`num py-2.5 pr-4 text-right font-medium ${alertTone(a.changePercent)}`}>
                            +{a.changePercent.toFixed(1)}%
                            <div className="text-xs font-normal text-muted">{a.vendor}</div>
                          </td>
                          <td className="num py-2.5 text-right">
                            {a.goodGamesPrice != null ? `A$${a.goodGamesPrice.toFixed(2)}` : 'Not stocked'}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {alertResult.alertCount > 10 && (
                    <p className="mt-2 text-xs text-muted">{alertResult.alertCount - 10} more were sent to Discord.</p>
                  )}
                </div>
              )}
            </div>
          )}
        </ActionRow>
      </Group>

      <Group title="Danger zone">
        <ActionRow
          icon={<Trash size={20} />}
          title="Clear Good Games prices"
          description="Deletes every stored Good Games price row so you can re-scrape from scratch. This cannot be undone. MTGJSON vendor prices are not touched."
          action={
            <Button className="w-full" variant="danger" loading={clearing} onClick={clearGoodGames}>
              {clearing ? 'Clearing' : 'Clear prices'}
            </Button>
          }
        />
      </Group>
    </>
  )
}
