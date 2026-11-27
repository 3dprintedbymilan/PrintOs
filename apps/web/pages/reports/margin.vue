<script setup lang="ts">
import {
  addDaysIso,
  deltaTrend,
  isIsoDate,
  isReportChannel,
  isReportPeriod,
  reportChannels,
  reportDate,
  reportPeriods,
  reportQuery,
  reportToday,
  validateRange,
} from '~/utils/reports'
import type { MarginReport, ReportChannel, ReportFilter, ReportPeriodKind } from '~/utils/reports'

definePageMeta({ titleKey: 'reports.margin.title' })

const { t, n, d } = useI18n()
const auth = useAuthStore()
const route = useRoute()

const today = reportToday()
const period = ref<ReportPeriodKind>(isReportPeriod(route.query.period) ? route.query.period : 'month')
const channel = ref<ReportChannel>(isReportChannel(route.query.channel) ? route.query.channel : 'all')
const from = ref(isIsoDate(route.query.from) ? route.query.from : addDaysIso(today, -29))
const to = ref(isIsoDate(route.query.to) ? route.query.to : today)
const productId = ref<string | null>(typeof route.query.productId === 'string' ? route.query.productId : null)

const report = ref<MarginReport | null>(null)
const state = ref<'loading' | 'ready' | 'failed'>('loading')
const rangeError = ref<'from' | 'to' | null>(null)
let requestId = 0

const filter = computed<ReportFilter>(() => ({ period: period.value, channel: channel.value, from: from.value, to: to.value, productId: productId.value }))
const periodOptions = computed(() => reportPeriods.map(value => ({ value, label: t(`reports.period.${value}`) })))
const channelOptions = computed(() => reportChannels.map(value => ({ value, label: t(`reports.channel.${value}`) })))
const baseQuery = computed(() => reportQuery({ ...filter.value, productId: null }))

async function load() {
  rangeError.value = validateRange(filter.value)
  if (rangeError.value) {
    await nextTick()
    document.querySelector<HTMLElement>(`[data-range="${rangeError.value}"] input`)?.focus()
    return
  }
  const current = ++requestId
  if (!report.value) state.value = 'loading'
  try {
    const result = await auth.authorizedFetch<MarginReport>(`/api/reports/margin?${reportQuery(filter.value)}`)
    if (current !== requestId) return
    report.value = result
    state.value = 'ready'
  }
  catch {
    if (current !== requestId) return
    report.value = null
    state.value = 'failed'
  }
}

watch([period, channel, from, to], () => {
  history.replaceState(history.state, '', `${route.path}?${reportQuery(filter.value)}`)
  load()
})

watch(() => route.query.productId, (value) => {
  productId.value = typeof value === 'string' ? value : null
  load()
})

onMounted(load)

const money = (value: number) => n(value, { style: 'currency', currency: report.value?.currency ?? 'EUR' })
const percent = (value: number | null) => (value === null ? '-' : `${n(value, { maximumFractionDigits: 1 })}%`)
const dateText = (value: string) => d(reportDate(value), { dateStyle: 'medium', timeZone: 'UTC' })

const rangeText = computed(() => (report.value ? t('reports.range', { from: dateText(report.value.period.from), to: dateText(report.value.period.to) }) : ''))
const previousText = computed(() => (report.value ? t('reports.previousRange', { from: dateText(report.value.period.previousFrom), to: dateText(report.value.period.previousTo) }) : ''))
const empty = computed(() => !!report.value && report.value.current.units === 0 && report.value.current.revenue === 0 && report.value.current.standCosts === 0)
const productLabel = computed(() => report.value?.products[0]?.label ?? '')

function signed(value: number, formatter: (v: number) => string) {
  return `${value > 0 ? '+' : ''}${formatter(value)}`
}

function changeText(change: number, percentChange: number | null) {
  if (percentChange === null) return t('reports.deltaAbsolute', { change: signed(change, money) })
  return t('reports.deltaPercent', { percent: `${percentChange > 0 ? '+' : ''}${n(percentChange, { maximumFractionDigits: 1 })}%` })
}

const tiles = computed(() => {
  const data = report.value
  if (!data) return []
  const { current, delta } = data
  const hasPrevious = data.previous.revenue !== 0 || data.previous.cost !== 0
  return [
    {
      key: 'revenue',
      label: t('reports.revenue'),
      value: money(current.revenue),
      delta: hasPrevious ? changeText(delta.revenueChange, delta.revenueChangePercent) : undefined,
      trend: deltaTrend(delta.revenueChange),
    },
    { key: 'cost', label: t('reports.cost'), value: money(current.cost), delta: undefined, trend: 'up' as const },
    {
      key: 'profit',
      label: t('reports.grossProfit'),
      value: money(current.grossProfit),
      delta: hasPrevious ? changeText(delta.grossProfitChange, delta.grossProfitChangePercent) : undefined,
      trend: deltaTrend(delta.grossProfitChange),
    },
    {
      key: 'margin',
      label: t('reports.marginPercent'),
      value: percent(current.marginPercent),
      delta: delta.marginPointsChange === null ? undefined : t('reports.deltaPoints', { points: `${delta.marginPointsChange > 0 ? '+' : ''}${n(delta.marginPointsChange, { maximumFractionDigits: 1 })}` }),
      trend: deltaTrend(delta.marginPointsChange ?? 0),
    },
  ]
})

const channelLabel = (row: { key: string }) => t(`reports.channel.${row.key}`)
</script>

<template>
  <div class="stack">
    <p
      v-if="productId"
      class="back"
    >
      <NuxtLink :to="`/reports/margin?${baseQuery}`">
        {{ $t('reports.margin.allProducts') }}
      </NuxtLink>
    </p>

    <UiCard class="filters">
      <UiSelect
        v-model="period"
        :label="$t('reports.period.label')"
        :options="periodOptions"
      />
      <div
        v-if="period === 'custom'"
        class="range"
      >
        <div data-range="from">
          <UiInput
            v-model="from"
            type="date"
            :label="$t('reports.from')"
            :error="rangeError === 'from' ? $t('reports.errors.from') : undefined"
          />
        </div>
        <div data-range="to">
          <UiInput
            v-model="to"
            type="date"
            :label="$t('reports.to')"
            :error="rangeError === 'to' ? $t('reports.errors.to') : undefined"
          />
        </div>
      </div>
      <UiSegmentedControl
        v-model="channel"
        :label="$t('reports.channel.label')"
        :options="channelOptions"
      />
    </UiCard>

    <StateLoading v-if="state === 'loading'" />
    <StateError
      v-else-if="state === 'failed'"
      @retry="load"
    />

    <template v-else-if="report">
      <p
        v-if="productId && productLabel"
        class="product"
      >
        {{ productLabel }}
      </p>
      <p class="range-text">
        {{ rangeText }}<br>
        <span>{{ previousText }}</span>
      </p>

      <StateEmpty
        v-if="empty"
        :message="$t('reports.margin.empty')"
      />
      <template v-else>
        <div
          class="tiles"
          role="group"
          :aria-label="$t('reports.kpiLabel')"
        >
          <UiKpiTile
            v-for="tile in tiles"
            :key="tile.key"
            :label="tile.label"
            :value="tile.value"
            :delta="tile.delta"
            :trend="tile.trend"
          />
        </div>
        <p
          v-if="!report.current.costComplete"
          class="note"
          role="note"
        >
          {{ $t('reports.costIncomplete') }}
        </p>

        <UiCard v-if="report.current.standCosts > 0">
          <h2>{{ $t('reports.standTitle') }}</h2>
          <dl class="net">
            <div><dt>{{ $t('reports.grossProfit') }}</dt><dd>{{ money(report.current.grossProfit) }}</dd></div>
            <div><dt>{{ $t('reports.standCosts') }}</dt><dd>{{ money(-report.current.standCosts) }}</dd></div>
            <div class="total"><dt>{{ $t('reports.netProfit') }}</dt><dd>{{ money(report.current.netProfit) }}</dd></div>
            <div><dt>{{ $t('reports.netMarginPercent') }}</dt><dd>{{ percent(report.current.netMarginPercent) }}</dd></div>
          </dl>
        </UiCard>

        <UiCard>
          <h2>{{ $t('reports.trendTitle') }}</h2>
          <ReportTrendChart
            :points="report.trend"
            :currency="report.currency"
            :granularity="report.granularity"
          />
        </UiCard>

        <UiCard v-if="report.channels.length > 1">
          <h2>{{ $t('reports.byChannel') }}</h2>
          <ReportMarginRows
            :rows="report.channels"
            :currency="report.currency"
            :label-for="channelLabel"
          />
        </UiCard>

        <UiCard v-if="report.markets.length > 0">
          <h2>{{ $t('reports.byMarket') }}</h2>
          <ReportMarginRows
            :rows="report.markets"
            :currency="report.currency"
          />
        </UiCard>

        <UiCard v-if="report.products.length > 0 && !productId">
          <h2>{{ $t('reports.byProduct') }}</h2>
          <ReportMarginRows
            :rows="report.products"
            :currency="report.currency"
            :drill-query="baseQuery"
          />
        </UiCard>

        <UiCard v-if="productId && report.variants.length > 0">
          <h2>{{ $t('reports.byVariant') }}</h2>
          <ReportMarginRows
            :rows="report.variants"
            :currency="report.currency"
          />
        </UiCard>
      </template>
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.filters { display: grid; gap: var(--space-3); }
.range { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: var(--space-3); }
.tiles { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-3); }
h2 { margin: 0 0 var(--space-2); font-size: var(--font-size-md); }
.range-text { margin: 0; color: var(--text); font-size: var(--font-size-sm); font-weight: 600; }
.range-text span { color: var(--muted); font-weight: 400; }
.product { margin: 0; font-size: var(--font-size-lg); font-weight: 700; overflow-wrap: anywhere; }
.back { margin: 0; }
.back a { display: inline-flex; align-items: center; min-height: var(--tap-size); color: var(--accent-text); font-weight: 600; text-decoration: none; }
.note { margin: 0; padding: var(--space-3); color: var(--text); font-size: var(--font-size-sm); background: var(--accent-soft); border-radius: var(--radius-sm); }
.net { display: grid; gap: var(--space-2); margin: 0; }
.net div { display: flex; justify-content: space-between; gap: var(--space-3); }
.net dt { color: var(--muted); }
.net dd { margin: 0; font-variant-numeric: tabular-nums; font-weight: 600; }
.net .total dt, .net .total dd { color: var(--text); font-weight: 700; }
@media (min-width: 768px) { .tiles { grid-template-columns: repeat(4, minmax(0, 1fr)); } }
</style>
