import type { CheckoutRequest, PendingSale } from '~/utils/pos'
import { isRetryableStatus, loadQueue, saveQueue } from '~/utils/pos'
import type { MarketSale } from '~/utils/markets'
import { parseApiError } from '~/utils/apiError'

export type SubmitOutcome
  = | { kind: 'done', sale: MarketSale }
    | { kind: 'queued', pending: PendingSale }
    | { kind: 'rejected', status: number, fieldErrors: Record<string, string[]>, title: string }

const requestTimeoutMs = 20_000

function storage() {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage
  }
  catch {
    return null
  }
}

export function usePosQueue() {
  const auth = useAuthStore()
  const queue = useState<PendingSale[]>('pos-queue', () => [])
  const flushing = ref(false)

  function persist() {
    saveQueue(storage(), queue.value)
  }

  function hydrate() {
    queue.value = loadQueue(storage())
  }

  function send(marketId: string, request: CheckoutRequest) {
    return auth.authorizedFetch<MarketSale>(`/api/markets/${marketId}/sales`, { method: 'POST', body: request, timeout: requestTimeoutMs })
  }

  function enqueue(marketId: string, request: CheckoutRequest, total: number): PendingSale {
    const pending: PendingSale = { key: request.idempotencyKey, marketId, request, total, queuedAt: new Date().toISOString(), state: 'pending', message: null }
    queue.value = [...queue.value.filter(entry => entry.key !== pending.key), pending]
    persist()
    return pending
  }

  async function submit(marketId: string, request: CheckoutRequest, total: number): Promise<SubmitOutcome> {
    try {
      return { kind: 'done', sale: await send(marketId, request) }
    }
    catch (error) {
      const parsed = parseApiError(error)
      if (isRetryableStatus(parsed.status)) return { kind: 'queued', pending: enqueue(marketId, request, total) }
      return { kind: 'rejected', status: parsed.status, fieldErrors: parsed.fieldErrors, title: parsed.title }
    }
  }

  async function flush(): Promise<number> {
    if (flushing.value) return 0
    flushing.value = true
    let synced = 0
    try {
      for (const pending of [...queue.value].filter(entry => entry.state === 'pending').sort((left, right) => left.queuedAt.localeCompare(right.queuedAt))) {
        try {
          await send(pending.marketId, pending.request)
          queue.value = queue.value.filter(entry => entry.key !== pending.key)
          synced++
        }
        catch (error) {
          const parsed = parseApiError(error)
          if (isRetryableStatus(parsed.status)) break
          const message = Object.values(parsed.fieldErrors)[0]?.[0] ?? parsed.title
          queue.value = queue.value.map(entry => entry.key === pending.key ? { ...entry, state: 'rejected', message } : entry)
        }
      }
    }
    finally {
      persist()
      flushing.value = false
    }
    return synced
  }

  function discard(key: string) {
    queue.value = queue.value.filter(entry => entry.key !== key)
    persist()
  }

  return { queue, flushing, hydrate, submit, flush, discard }
}
