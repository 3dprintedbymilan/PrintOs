<script setup lang="ts">
import type { Market, MarketSale, MarketSummary } from '~/utils/markets'
import { averageSale, formatDateRange, marketTone } from '~/utils/markets'
import { parseApiError } from '~/utils/apiError'

definePageMeta({ titleKey: 'markets.detailTitle' })

const { n, d, t } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const posQueue = usePosQueue()
const id = String(route.params.id)

const market = ref<Market | null>(null)
const summary = ref<MarketSummary | null>(null)
const sales = ref<MarketSale[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const busy = ref(false)
const actionError = ref('')

const voidTarget = ref<MarketSale | null>(null)
const voidOpen = ref(false)
const voidReason = ref('')
const voidError = ref('')

const money = (amount: number) => n(amount, { style: 'currency', currency: market.value?.currency ?? 'EUR' })
const dateText = (value: string) => d(new Date(`${value}T12:00:00`), { dateStyle: 'medium' })
const timeText = (value: string) => d(new Date(value), { timeStyle: 'short' })
const waiting = computed(() => posQueue.queue.value.filter(entry => entry.marketId === id))

async function load() {
  status.value = 'loading'
  try {
    const [found, totals, list] = await Promise.all([
      auth.authorizedFetch<Market>(`/api/markets/${id}`),
      auth.authorizedFetch<MarketSummary>(`/api/markets/${id}/summary`),
      auth.authorizedFetch<MarketSale[]>(`/api/markets/${id}/sales`),
    ])
    market.value = found
    summary.value = totals
    sales.value = list
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

async function setStatus(action: 'close' | 'reopen') {
  busy.value = true
  actionError.value = ''
  try {
    market.value = await auth.authorizedFetch<Market>(`/api/markets/${id}/${action}`, { method: 'POST' })
  }
  catch {
    actionError.value = t('markets.errors.save')
  }
  finally {
    busy.value = false
  }
}

function askVoid(sale: MarketSale) {
  voidTarget.value = sale
  voidReason.value = ''
  voidError.value = ''
  voidOpen.value = true
}

async function confirmVoid() {
  if (!voidTarget.value) return
  if (!voidReason.value.trim()) {
    voidError.value = t('markets.errors.voidReason')
    return
  }

  busy.value = true
  try {
    await auth.authorizedFetch<MarketSale>(`/api/market-sales/${voidTarget.value.id}/void`, { method: 'POST', body: { reason: voidReason.value.trim() } })
    voidOpen.value = false
    await load()
  }
  catch (error) {
    voidError.value = parseApiError(error).status === 409 ? t('markets.errors.voidClosed') : t('markets.errors.save')
  }
  finally {
    busy.value = false
  }
}

async function retryWaiting() {
  await posQueue.flush()
  await load()
}

onMounted(() => {
  posQueue.hydrate()
  load()
})
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/markets"
      class="back"
    >
      {{ $t('markets.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed' || !market || !summary"
      @retry="load"
    />

    <template v-else>
      <UiCard class="head">
        <div class="title">
          <h2>{{ market.name }}</h2>
          <UiStatusChip :status="marketTone(market.status)">
            {{ $t(`markets.status.${market.status}`) }}
          </UiStatusChip>
        </div>
        <p class="meta">
          {{ formatDateRange(market.startDate, market.endDate, dateText) }}<template v-if="market.location"> · {{ market.location }}</template>
        </p>
        <p
          v-if="market.standCost > 0"
          class="meta"
        >
          {{ $t('markets.standCost', { amount: money(market.standCost) }) }}
        </p>
        <p
          v-if="market.notes"
          class="meta notes"
        >
          {{ market.notes }}
        </p>
        <div class="actions">
          <NuxtLink
            v-if="market.status === 'Open'"
            :to="`/markets/${id}/pos`"
            class="register"
          >
            {{ $t('markets.openRegister') }}
          </NuxtLink>
          <NuxtLink
            :to="`/markets/${id}/edit`"
            class="secondary"
          >
            {{ $t('markets.edit') }}
          </NuxtLink>
          <UiButton
            v-if="market.status === 'Open'"
            variant="secondary"
            :loading="busy"
            @click="setStatus('close')"
          >
            {{ $t('markets.closeMarket') }}
          </UiButton>
          <UiButton
            v-else
            variant="secondary"
            :loading="busy"
            @click="setStatus('reopen')"
          >
            {{ $t('markets.reopen') }}
          </UiButton>
        </div>
        <p
          v-if="actionError"
          class="error"
          role="alert"
        >
          {{ actionError }}
        </p>
      </UiCard>

      <div
        v-if="waiting.length > 0"
        class="waiting"
        role="status"
      >
        <p>{{ $t('markets.waiting', { count: waiting.length }, waiting.length) }}</p>
        <UiButton
          variant="secondary"
          :loading="posQueue.flushing.value"
          @click="retryWaiting"
        >
          {{ $t('pos.retryNow') }}
        </UiButton>
      </div>

      <div class="kpis">
        <UiKpiTile
          :label="$t('markets.kpi.revenue')"
          :value="money(summary.revenue)"
        />
        <UiKpiTile
          :label="$t('markets.kpi.sales')"
          :value="n(summary.saleCount)"
        />
        <UiKpiTile
          :label="$t('markets.kpi.items')"
          :value="n(summary.itemCount)"
        />
        <UiKpiTile
          :label="$t('markets.kpi.average')"
          :value="money(averageSale(summary.revenue, summary.saleCount))"
        />
      </div>
      <p
        v-if="summary.saleCount > 0"
        class="meta"
      >
        {{ summary.costComplete ? $t('markets.grossProfit', { amount: money(summary.grossProfit) }) : $t('markets.grossProfitPartial', { amount: money(summary.grossProfit) }) }}
      </p>

      <StateEmpty
        v-if="sales.length === 0"
        :message="$t('markets.noSales')"
      />

      <template v-else>
        <section class="block">
          <h3>{{ $t('markets.byPayment') }}</h3>
          <ul class="rows">
            <li
              v-for="entry in summary.byPayment"
              :key="entry.method"
            >
              <span>{{ $t(`markets.method.${entry.method}`) }} · {{ $t('markets.salesCount', { count: entry.saleCount }, entry.saleCount) }}</span>
              <strong>{{ money(entry.amount) }}</strong>
            </li>
          </ul>
        </section>

        <section class="block">
          <h3>{{ $t('markets.itemsSold') }}</h3>
          <ul class="rows">
            <li
              v-for="item in summary.items"
              :key="`${item.variantId}-${item.description}`"
            >
              <span>{{ item.quantity }} × {{ item.description }}</span>
              <strong>{{ money(item.revenue) }}</strong>
            </li>
          </ul>
        </section>

        <section class="block">
          <h3>{{ $t('markets.salesList') }}</h3>
          <ul class="sales">
            <li
              v-for="sale in sales"
              :key="sale.id"
            >
              <details :class="{ voided: sale.status === 'Voided' }">
                <summary>
                  <span class="receipt">#{{ sale.receiptNumber }}</span>
                  <span class="when">{{ timeText(sale.soldAt) }} · {{ $t(`markets.method.${sale.paymentMethod}`) }}</span>
                  <UiStatusChip
                    v-if="sale.status === 'Voided'"
                    status="danger"
                  >
                    {{ $t('markets.voided') }}
                  </UiStatusChip>
                  <strong>{{ money(sale.totalAmount) }}</strong>
                </summary>
                <ul class="lines">
                  <li
                    v-for="line in sale.lines"
                    :key="line.id"
                  >
                    <span>{{ line.quantity }} × {{ line.description }}</span>
                    <span>{{ money(line.lineTotal) }}</span>
                  </li>
                  <li
                    v-if="sale.discountAmount > 0"
                    class="discount"
                  >
                    <span>{{ $t('markets.discount') }}</span>
                    <span>-{{ money(sale.discountAmount) }}</span>
                  </li>
                  <li
                    v-if="sale.amountTendered !== null && sale.paymentMethod === 'cash'"
                    class="discount"
                  >
                    <span>{{ $t('pos.tendered') }} / {{ $t('pos.change') }}</span>
                    <span>{{ money(sale.amountTendered) }} / {{ money(sale.changeGiven) }}</span>
                  </li>
                  <li
                    v-if="sale.voidReason"
                    class="discount"
                  >
                    <span>{{ $t('markets.voidReason', { reason: sale.voidReason }) }}</span>
                  </li>
                </ul>
                <UiButton
                  v-if="sale.status === 'Completed'"
                  variant="secondary"
                  @click="askVoid(sale)"
                >
                  {{ $t('markets.void') }}
                </UiButton>
              </details>
            </li>
          </ul>
        </section>
      </template>
    </template>

    <UiDialog
      v-model="voidOpen"
      :title="$t('markets.voidTitle', { number: voidTarget?.receiptNumber ?? 0 })"
    >
      <p class="meta">
        {{ $t('markets.voidHelp') }}
      </p>
      <UiInput
        v-model="voidReason"
        :label="$t('markets.voidReasonLabel')"
        :error="voidError"
        autocomplete="off"
      />
      <template #actions>
        <UiButton
          variant="ghost"
          @click="voidOpen = false"
        >
          {{ $t('markets.cancel') }}
        </UiButton>
        <UiButton
          variant="danger"
          :loading="busy"
          @click="confirmVoid"
        >
          {{ $t('markets.void') }}
        </UiButton>
      </template>
    </UiDialog>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.head { display: grid; gap: var(--space-2); }
.title { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-2); }
h2 { margin: 0; font-size: var(--font-size-lg); overflow-wrap: anywhere; }
h3 { margin: 0 0 var(--space-2); color: var(--muted); font-size: var(--font-size-sm); font-weight: 600; text-transform: uppercase; letter-spacing: 0.04em; }
.meta { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.notes { white-space: pre-line; }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
.actions { display: flex; flex-wrap: wrap; gap: var(--space-2); margin-top: var(--space-2); }
.register, .secondary { display: inline-flex; align-items: center; justify-content: center; min-height: var(--tap-size); padding: 0 var(--space-5); font-weight: 600; text-decoration: none; border-radius: var(--radius-pill); }
.register { flex: 1 1 100%; min-height: 52px; color: var(--accent-contrast); background: var(--accent); font-weight: 700; }
.secondary { color: var(--text); background: var(--surface-2); border: 1px solid var(--border); }
.waiting { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); padding: var(--space-3) var(--space-4); background: var(--accent-soft); border: 1px solid var(--border); border-radius: var(--radius-md); }
.waiting p { margin: 0; font-weight: 600; }
.kpis { display: grid; grid-template-columns: repeat(2, 1fr); gap: var(--space-2); }
.rows, .sales, .lines { display: grid; gap: var(--space-1); margin: 0; padding: 0; list-style: none; }
.rows li { display: flex; justify-content: space-between; gap: var(--space-3); padding: var(--space-3) var(--space-4); background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
.sales > li { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
details { display: grid; }
summary { display: flex; align-items: center; gap: var(--space-2); min-height: 56px; padding: var(--space-2) var(--space-4); cursor: pointer; list-style: none; }
summary::-webkit-details-marker { display: none; }
summary:focus-visible { outline: none; box-shadow: var(--focus-ring); border-radius: var(--radius-md); }
.receipt { font-weight: 700; }
.when { flex: 1; color: var(--muted); font-size: var(--font-size-sm); }
.voided strong, .voided .receipt { text-decoration: line-through; color: var(--muted); }
.lines { padding: 0 var(--space-4) var(--space-3); }
.lines li { display: flex; justify-content: space-between; gap: var(--space-3); font-size: var(--font-size-sm); }
.lines .discount { color: var(--muted); }
details > :deep(.btn) { margin: 0 var(--space-4) var(--space-3); }

@media (min-width: 768px) {
  .kpis { grid-template-columns: repeat(4, 1fr); }
  .register { flex: 0 0 auto; }
}
</style>
