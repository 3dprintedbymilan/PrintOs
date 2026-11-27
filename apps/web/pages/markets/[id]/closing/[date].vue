<script setup lang="ts">
import type { Market, MarketSummary } from '~/utils/markets'
import type { MarketClosing } from '~/utils/marketClosing'
import { cashDifference, expectedCash, validateClosingForm } from '~/utils/marketClosing'
import { parseApiError } from '~/utils/apiError'

definePageMeta({ titleKey: 'closing.title' })

const { n, d, t } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const posQueue = usePosQueue()
const id = String(route.params.id)
const date = String(route.params.date)

const market = ref<Market | null>(null)
const summary = ref<MarketSummary | null>(null)
const closing = ref<MarketClosing | null>(null)
const status = ref<'loading' | 'ready' | 'failed'>('loading')

const form = reactive({ openingFloat: '', countedCash: '', notes: '' })
const errors = ref<Record<string, string>>({})
const confirmOpen = ref(false)
const saving = ref(false)
const saveError = ref('')
const shareNote = ref('')

const money = (amount: number) => n(amount, { style: 'currency', currency: market.value?.currency ?? 'EUR' })
const dateText = computed(() => d(new Date(`${date}T12:00:00`), { dateStyle: 'full' }))
const waiting = computed(() => posQueue.queue.value.filter(entry => entry.marketId === id && entry.state === 'pending'))
const cashSales = computed(() => summary.value?.byPayment.find(entry => entry.method === 'cash')?.amount ?? 0)

const preview = computed(() => {
  const { values } = validateClosingForm(form)
  if (!values) return null
  const expected = expectedCash(values.openingFloat, cashSales.value)
  return { expected, difference: cashDifference(values.countedCash, expected) }
})

const report = computed(() => {
  const value = closing.value
  if (!value) return null
  return {
    expected: value.expectedCash,
    difference: value.difference,
    payments: [
      { method: 'cash', amount: value.cashTotal },
      { method: 'card', amount: value.cardTotal },
      { method: 'other', amount: value.otherTotal },
    ],
  }
})

async function load() {
  status.value = 'loading'
  try {
    const [found, totals] = await Promise.all([
      auth.authorizedFetch<Market>(`/api/markets/${id}`),
      auth.authorizedFetch<MarketSummary>(`/api/markets/${id}/summary`, { query: { date } }),
    ])
    market.value = found
    summary.value = totals
    closing.value = await auth.authorizedFetch<MarketClosing>(`/api/markets/${id}/closings/${date}`).catch((error) => {
      if (parseApiError(error).status === 404) return null
      throw error
    })
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function askConfirm() {
  saveError.value = ''
  const { values, errors: found } = validateClosingForm(form)
  errors.value = Object.fromEntries(Object.entries(found).map(([field, kind]) => [field, t(`closing.errors.${kind}`)]))
  if (!values) {
    nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
    return
  }
  confirmOpen.value = true
}

async function closeDay() {
  const { values } = validateClosingForm(form)
  if (!values) return
  saving.value = true
  try {
    closing.value = await auth.authorizedFetch<MarketClosing>(`/api/markets/${id}/closings`, {
      method: 'POST',
      body: { date, openingFloat: values.openingFloat, countedCash: values.countedCash, notes: values.notes },
    })
    confirmOpen.value = false
    await load()
  }
  catch (error) {
    confirmOpen.value = false
    saveError.value = parseApiError(error).status === 409 ? t('closing.errors.already') : t('closing.errors.save')
  }
  finally {
    saving.value = false
  }
}

function reportText(value: MarketClosing): string {
  const lines = [
    `${value.marketName} · ${dateText.value}`,
    `${t('closing.sales')}: ${value.saleCount} (${t('closing.voided')}: ${value.voidedCount})`,
    `${t('closing.revenue')}: ${money(value.revenue)}`,
    `${t('markets.method.cash')}: ${money(value.cashTotal)}`,
    `${t('markets.method.card')}: ${money(value.cardTotal)}`,
    `${t('markets.method.other')}: ${money(value.otherTotal)}`,
    `${t('closing.expectedCash')}: ${money(value.expectedCash)}`,
    `${t('closing.countedCash')}: ${money(value.countedCash)}`,
    `${t('closing.difference')}: ${money(value.difference)}`,
  ]
  if (value.standCost > 0) lines.push(`${t('closing.standCost')}: ${money(value.standCost)}`)
  return lines.join('\n')
}

function printReport() {
  window.print()
}

async function share() {
  if (!closing.value) return
  const text = reportText(closing.value)
  shareNote.value = ''
  try {
    if (typeof navigator.share === 'function') {
      await navigator.share({ title: t('closing.shareTitle', { market: closing.value.marketName }), text })
      return
    }
    await navigator.clipboard.writeText(text)
    shareNote.value = t('closing.copied')
  }
  catch {
    shareNote.value = ''
  }
}

onMounted(() => {
  posQueue.hydrate()
  load()
})
</script>

<template>
  <div class="stack">
    <NuxtLink
      :to="`/markets/${id}`"
      class="back noprint"
    >
      {{ $t('markets.backToMarket') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed' || !market || !summary"
      @retry="load"
    />

    <template v-else-if="closing && report">
      <UiCard class="head">
        <div class="title">
          <h2>{{ closing.marketName }}</h2>
          <UiStatusChip status="success">
            {{ $t('closing.closed') }}
          </UiStatusChip>
        </div>
        <p class="meta">
          {{ dateText }}
        </p>
        <p class="meta">
          {{ $t('closing.closedAt', { time: d(new Date(closing.closedAt), { dateStyle: 'medium', timeStyle: 'short' }) }) }}
        </p>
      </UiCard>

      <section class="block">
        <h3>{{ $t('closing.totals') }}</h3>
        <dl class="rows">
          <div><dt>{{ $t('closing.sales') }}</dt><dd>{{ closing.saleCount }}</dd></div>
          <div v-if="closing.voidedCount > 0"><dt>{{ $t('closing.voided') }}</dt><dd>{{ closing.voidedCount }}</dd></div>
          <div><dt>{{ $t('closing.items') }}</dt><dd>{{ closing.itemCount }}</dd></div>
          <div v-if="closing.discounts > 0"><dt>{{ $t('markets.discount') }}</dt><dd>-{{ money(closing.discounts) }}</dd></div>
          <div class="strong"><dt>{{ $t('closing.revenue') }}</dt><dd>{{ money(closing.revenue) }}</dd></div>
        </dl>
      </section>

      <section class="block">
        <h3>{{ $t('markets.byPayment') }}</h3>
        <dl class="rows">
          <div
            v-for="entry in report.payments"
            :key="entry.method"
          >
            <dt>{{ $t(`markets.method.${entry.method}`) }}</dt>
            <dd>{{ money(entry.amount) }}</dd>
          </div>
        </dl>
      </section>

      <section class="block">
        <h3>{{ $t('closing.cash') }}</h3>
        <dl class="rows">
          <div><dt>{{ $t('closing.openingFloat') }}</dt><dd>{{ money(closing.openingFloat) }}</dd></div>
          <div><dt>{{ $t('closing.expectedCash') }}</dt><dd>{{ money(closing.expectedCash) }}</dd></div>
          <div><dt>{{ $t('closing.countedCash') }}</dt><dd>{{ money(closing.countedCash) }}</dd></div>
          <div
            class="strong"
            :class="{ off: closing.difference !== 0 }"
          >
            <dt>{{ $t('closing.difference') }}</dt>
            <dd>{{ closing.difference > 0 ? '+' : '' }}{{ money(closing.difference) }}</dd>
          </div>
        </dl>
      </section>

      <section class="block">
        <h3>{{ $t('closing.result') }}</h3>
        <dl class="rows">
          <div><dt>{{ $t('closing.grossProfit') }}</dt><dd>{{ money(closing.grossProfit) }}</dd></div>
          <div v-if="closing.standCost > 0"><dt>{{ $t('closing.standCost') }}</dt><dd>{{ money(closing.standCost) }}</dd></div>
        </dl>
      </section>

      <section
        v-if="closing.items.length > 0"
        class="block"
      >
        <h3>{{ $t('markets.itemsSold') }}</h3>
        <dl class="rows">
          <div
            v-for="item in closing.items"
            :key="`${item.variantId}-${item.description}`"
          >
            <dt>{{ item.quantity }} × {{ item.description }}</dt>
            <dd>{{ money(item.revenue) }}</dd>
          </div>
        </dl>
      </section>

      <p
        v-if="closing.notes"
        class="meta notes"
      >
        {{ closing.notes }}
      </p>

      <div class="actions noprint">
        <UiButton @click="share">
          {{ $t('closing.share') }}
        </UiButton>
        <UiButton
          variant="secondary"
          @click="printReport"
        >
          {{ $t('closing.print') }}
        </UiButton>
      </div>
      <p
        v-if="shareNote"
        class="meta noprint"
        role="status"
      >
        {{ shareNote }}
      </p>
    </template>

    <template v-else>
      <UiCard class="head">
        <h2>{{ market.name }}</h2>
        <p class="meta">
          {{ dateText }}
        </p>
      </UiCard>

      <div
        v-if="waiting.length > 0"
        class="warning"
        role="alert"
      >
        {{ $t('closing.waiting', { count: waiting.length }, waiting.length) }}
      </div>

      <section class="block">
        <h3>{{ $t('closing.totals') }}</h3>
        <dl class="rows">
          <div><dt>{{ $t('closing.sales') }}</dt><dd>{{ summary.saleCount }}</dd></div>
          <div v-if="summary.voidedCount > 0"><dt>{{ $t('closing.voided') }}</dt><dd>{{ summary.voidedCount }}</dd></div>
          <div class="strong"><dt>{{ $t('closing.revenue') }}</dt><dd>{{ money(summary.revenue) }}</dd></div>
          <div
            v-for="entry in summary.byPayment"
            :key="entry.method"
          >
            <dt>{{ $t(`markets.method.${entry.method}`) }}</dt>
            <dd>{{ money(entry.amount) }}</dd>
          </div>
        </dl>
      </section>

      <form
        class="form"
        novalidate
        @submit.prevent="askConfirm"
      >
        <h3>{{ $t('closing.countTitle') }}</h3>
        <UiInput
          v-model="form.openingFloat"
          inputmode="decimal"
          :label="$t('closing.openingFloat')"
          :hint="$t('closing.openingFloatHint')"
          :error="errors.openingFloat"
        />
        <UiInput
          v-model="form.countedCash"
          inputmode="decimal"
          :label="$t('closing.countedCash')"
          :hint="$t('closing.countedHint')"
          :error="errors.countedCash"
        />
        <div
          class="compare"
          role="status"
        >
          <template v-if="preview">
            <p>{{ $t('closing.expectedCash') }}: <strong>{{ money(preview.expected) }}</strong></p>
            <p :class="{ off: preview.difference !== 0 }">
              {{ $t('closing.difference') }}: <strong>{{ preview.difference > 0 ? '+' : '' }}{{ money(preview.difference) }}</strong>
            </p>
          </template>
        </div>
        <UiTextarea
          v-model="form.notes"
          :label="$t('closing.notes')"
          :error="errors.notes"
          :rows="2"
          :maxlength="2000"
        />
        <p
          v-if="saveError"
          class="error"
          role="alert"
        >
          {{ saveError }}
        </p>
        <UiButton
          type="submit"
          block
        >
          {{ $t('closing.closeDay') }}
        </UiButton>
      </form>
    </template>

    <UiDialog
      v-model="confirmOpen"
      :title="$t('closing.confirmTitle')"
    >
      <p class="meta">
        {{ $t('closing.confirmHelp') }}
      </p>
      <template #actions>
        <UiButton
          variant="ghost"
          @click="confirmOpen = false"
        >
          {{ $t('markets.cancel') }}
        </UiButton>
        <UiButton
          :loading="saving"
          @click="closeDay"
        >
          {{ $t('closing.closeDay') }}
        </UiButton>
      </template>
    </UiDialog>
  </div>
</template>

<style>
@media print {
  aside, .top, .skip, nav, .noprint { display: none !important; }
  body { background: #fff !important; color: #000 !important; }
  .shell { padding-left: 0 !important; }
}
</style>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.head { display: grid; gap: var(--space-1); }
.title { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-2); }
h2 { margin: 0; font-size: var(--font-size-lg); overflow-wrap: anywhere; }
h3 { margin: 0 0 var(--space-2); color: var(--muted); font-size: var(--font-size-sm); font-weight: 600; text-transform: uppercase; letter-spacing: 0.04em; }
.meta { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.notes { white-space: pre-line; }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
.rows { display: grid; gap: 1px; margin: 0; overflow: hidden; background: var(--border); border: 1px solid var(--border); border-radius: var(--radius-md); }
.rows div { display: flex; justify-content: space-between; gap: var(--space-3); padding: var(--space-3) var(--space-4); background: var(--surface); }
.rows dt { color: var(--muted); }
.rows dd { margin: 0; font-variant-numeric: tabular-nums; font-weight: 600; }
.rows .strong dt { color: var(--text); font-weight: 700; }
.rows .strong dd { font-size: var(--font-size-lg); }
.off dd, .off { color: var(--warning); }
.compare { display: grid; gap: var(--space-1); padding: var(--space-3) var(--space-4); background: var(--surface-2); border-radius: var(--radius-md); }
.compare:empty { display: none; }
.compare p { margin: 0; }
.compare strong { font-size: var(--font-size-lg); font-variant-numeric: tabular-nums; }
.warning { padding: var(--space-3) var(--space-4); font-weight: 600; background: color-mix(in srgb, var(--warning) 18%, transparent); border: 1px solid var(--warning); border-radius: var(--radius-md); }
.actions { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.actions :deep(.btn) { flex: 1; min-height: 52px; }
</style>
