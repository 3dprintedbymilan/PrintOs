<script setup lang="ts">
import { reportDate, reportQuery } from '~/utils/reports'
import type { MarginReport } from '~/utils/reports'

definePageMeta({ titleKey: 'reports.margin.title' })

const { t, n, d } = useI18n()
const auth = useAuthStore()
const route = useRoute()
const { period, channel, from, to, productId, rangeError, filter, baseQuery, validate, syncUrl } = useReportFilter()

const report = ref<MarginReport | null>(null)
const state = ref<'loading' | 'ready' | 'failed'>('loading')
let requestId = 0

async function load() {
  if (!(await validate())) return
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
  syncUrl()
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

    <ReportFilters
      v-model:period="period"
      v-model:channel="channel"
      v-model:from="from"
      v-model:to="to"
      :range-error="rangeError"
    />

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
        <ReportKpiTiles :report="report" />
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

        <ReportExportButton
          :path="`/api/reports/margin/export?${reportQuery(filter)}`"
          :file-name="`margin-${report.period.from}_${report.period.to}.csv`"
          :label="$t('reports.export.margin')"
        />

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
</style>
