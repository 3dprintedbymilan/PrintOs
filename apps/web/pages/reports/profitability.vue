<script setup lang="ts">
import { componentColor } from '~/utils/analytics'
import type { ProfitabilityAnalytics, ProfitRow } from '~/utils/analytics'

definePageMeta({ titleKey: 'analytics.profit.title' })

const { t } = useI18n()
const { period, channel, from, to, productId, rangeError, baseQuery, report, state, load, query, money, percent, rangeText, stamp, delta }
  = useAnalyticsReport<ProfitabilityAnalytics>('/api/reports/analytics/profitability')

const empty = computed(() => !!report.value && report.value.current.revenue === 0 && report.value.current.cost === 0 && report.value.current.standCosts === 0)
const productLabel = computed(() => report.value?.products[0]?.label ?? '')

const tiles = computed(() => {
  if (!report.value) return []
  const { current, previous } = report.value
  const hasPrevious = previous.revenue !== 0 || previous.cost !== 0
  return [
    { key: 'revenue', label: t('analytics.revenue'), value: money(current.revenue), ...delta(current.revenue, previous.revenue, hasPrevious) },
    { key: 'contribution', label: t('analytics.profit.contribution'), value: money(current.contribution), ...delta(current.contribution, previous.contribution, hasPrevious) },
    { key: 'cmargin', label: t('analytics.profit.contributionMargin'), value: percent(current.contributionMarginPercent), delta: undefined, trend: 'up' as const },
    { key: 'net', label: t('reports.netProfit'), value: money(current.netProfit), ...delta(current.netProfit, previous.netProfit, hasPrevious) },
  ]
})

const flow = computed(() => {
  if (!report.value) return []
  const { current } = report.value
  return [
    { key: 'revenue', label: t('analytics.revenue'), value: current.revenue },
    { key: 'cost', label: t('analytics.profit.costOfGoods'), value: -current.cost },
    { key: 'fees', label: t('analytics.profit.fees'), value: -current.fees },
    { key: 'contribution', label: t('analytics.profit.contribution'), value: current.contribution, strong: true },
    { key: 'stand', label: t('reports.standCosts'), value: -current.standCosts },
    { key: 'net', label: t('reports.netProfit'), value: current.netProfit, strong: true },
  ]
})

const componentItems = computed(() => (report.value?.components ?? []).map(row => ({
  key: row.kind,
  label: t(`analytics.component.${row.kind}`),
  value: row.amount,
  valueText: money(row.amount),
  color: componentColor(row.kind),
  detail: row.sharePercent === null ? undefined : t('analytics.profit.componentShare', { share: percent(row.sharePercent) }),
})))
const unallocated = computed(() => (report.value?.components ?? []).find(row => row.kind === 'unallocated'))

const trendPoints = computed(() => (report.value?.trend ?? []).map(point => ({
  start: point.start,
  bar: point.contribution,
  line: point.net,
  cells: [money(point.revenue), money(point.cost), money(point.contribution), money(point.net)],
})))

function rowItems(rows: ProfitRow[], kind: 'channel' | 'product' | 'customer') {
  return rows.map(row => ({
    key: row.key,
    label: kind === 'channel' ? t(`reports.channel.${row.key}`) : kind === 'customer' && !row.id ? t('analytics.sales.walkIn') : row.label,
    value: row.figures.contribution,
    valueText: money(row.figures.contribution),
    to: kind === 'product' && row.id && !productId.value ? `/reports/profitability?${baseQuery.value}&productId=${row.id}` : kind === 'customer' && row.id ? `/customers/${row.id}` : undefined,
    detail: t('analytics.profit.rowMeta', { revenue: money(row.figures.revenue), cost: money(row.figures.cost), fees: money(row.figures.fees), margin: percent(row.figures.contributionMarginPercent) }, row.figures.units),
  }))
}
</script>

<template>
  <div class="stack">
    <p
      v-if="productId"
      class="back"
    >
      <NuxtLink :to="`/reports/profitability?${baseQuery}`">
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
        {{ rangeText }}
      </p>
      <StateEmpty
        v-if="empty"
        :message="$t('analytics.profit.empty')"
      />
      <template v-else>
        <AnalyticsKpis :tiles="tiles" />
        <p
          v-if="!report.current.costComplete"
          class="note"
          role="note"
        >
          {{ $t('reports.costIncomplete') }}
        </p>

        <UiCard>
          <h2>{{ $t('analytics.profit.flowTitle') }}</h2>
          <dl class="net">
            <div
              v-for="item in flow"
              :key="item.key"
              :class="{ total: item.strong }"
            >
              <dt>{{ item.label }}</dt>
              <dd>{{ money(item.value) }}</dd>
            </div>
          </dl>
          <p class="hint">
            {{ $t('analytics.profit.flowHint') }}
          </p>
        </UiCard>

        <UiCard v-if="componentItems.length > 0">
          <h2>{{ $t('analytics.profit.componentsTitle') }}</h2>
          <AnalyticsBars
            :items="componentItems"
            :label="$t('analytics.profit.componentsTitle')"
          />
          <p
            v-if="unallocated"
            class="hint"
          >
            {{ $t('analytics.profit.unallocatedHint') }}
          </p>
        </UiCard>

        <UiCard>
          <h2>{{ $t('analytics.profit.trendTitle') }}</h2>
          <AnalyticsTrend
            :points="trendPoints"
            :granularity="report.granularity"
            :label="$t('analytics.profit.trendLabel')"
            :bar-label="$t('analytics.profit.contribution')"
            :line-label="$t('reports.netProfit')"
            :table-headers="[$t('analytics.revenue'), $t('analytics.profit.costOfGoods'), $t('analytics.profit.contribution'), $t('reports.netProfit')]"
            :readout="index => $t('analytics.profit.trendPoint', { contribution: money(report!.trend[index]!.contribution), net: money(report!.trend[index]!.net) })"
            :tick-format="value => $n(value, { notation: 'compact', maximumFractionDigits: 1 })"
          />
        </UiCard>

        <UiCard v-if="report.channels.length > 1">
          <h2>{{ $t('reports.byChannel') }}</h2>
          <AnalyticsBars
            :items="rowItems(report.channels, 'channel')"
            :label="$t('reports.byChannel')"
          />
        </UiCard>

        <UiCard v-if="report.products.length > 0 && !productId">
          <h2>{{ $t('reports.byProduct') }}</h2>
          <AnalyticsBars
            :items="rowItems(report.products, 'product')"
            :label="$t('reports.byProduct')"
          />
        </UiCard>

        <UiCard v-if="report.customers.length > 0">
          <h2>{{ $t('analytics.profit.byCustomer') }}</h2>
          <AnalyticsBars
            :items="rowItems(report.customers, 'customer')"
            :label="$t('analytics.profit.byCustomer')"
          />
        </UiCard>

        <UiCard>
          <h2>{{ $t('reports.export.title') }}</h2>
          <div class="exports">
            <ReportExportButton
              :path="`/api/reports/analytics/profitability/export?${query}&part=breakdown`"
              :file-name="`profitability-breakdown-${stamp}.csv`"
              :label="$t('analytics.export.breakdown')"
            />
            <ReportExportButton
              :path="`/api/reports/analytics/profitability/export?${query}&part=components`"
              :file-name="`profitability-components-${stamp}.csv`"
              :label="$t('analytics.export.components')"
            />
            <ReportExportButton
              :path="`/api/reports/analytics/profitability/export?${query}&part=trend`"
              :file-name="`profitability-trend-${stamp}.csv`"
              :label="$t('analytics.export.trend')"
            />
          </div>
        </UiCard>
      </template>
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
h2 { margin: 0 0 var(--space-2); font-size: var(--font-size-md); }
.range-text { margin: 0; color: var(--text); font-size: var(--font-size-sm); font-weight: 600; }
.product { margin: 0; font-size: var(--font-size-lg); font-weight: 700; overflow-wrap: anywhere; }
.back { margin: 0; }
.back a { display: inline-flex; align-items: center; min-height: var(--tap-size); color: var(--accent-text); font-weight: 600; text-decoration: none; }
.note { margin: 0; padding: var(--space-3); color: var(--text); font-size: var(--font-size-sm); background: var(--accent-soft); border-radius: var(--radius-sm); }
.hint { margin: var(--space-2) 0 0; color: var(--muted); font-size: var(--font-size-sm); }
.net { display: grid; gap: var(--space-2); margin: 0; }
.net div { display: flex; justify-content: space-between; gap: var(--space-3); }
.net dt { color: var(--muted); }
.net dd { margin: 0; font-variant-numeric: tabular-nums; font-weight: 600; }
.net .total dt, .net .total dd { color: var(--text); font-weight: 700; }
.exports { display: grid; gap: var(--space-3); }
@media (min-width: 768px) { .exports { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
</style>
