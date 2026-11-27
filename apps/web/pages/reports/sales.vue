<script setup lang="ts">
import type { SalesAnalytics } from '~/utils/analytics'

definePageMeta({ titleKey: 'analytics.sales.title' })

const { t } = useI18n()
const { period, channel, from, to, rangeError, baseQuery, report, state, load, query, money, percent, number, rangeText, stamp, delta }
  = useAnalyticsReport<SalesAnalytics>('/api/reports/analytics/sales')

const empty = computed(() => !!report.value && report.value.current.documents === 0)

const tiles = computed(() => {
  if (!report.value) return []
  const { current, previous } = report.value
  const hasPrevious = previous.documents > 0
  return [
    { key: 'revenue', label: t('analytics.revenue'), value: money(current.revenue), ...delta(current.revenue, previous.revenue, hasPrevious) },
    { key: 'orders', label: t('analytics.sales.documents'), value: String(current.documents), ...delta(current.documents, previous.documents, hasPrevious) },
    { key: 'aov', label: t('analytics.sales.aov'), value: money(current.averageOrderValue), ...delta(current.averageOrderValue ?? 0, previous.averageOrderValue ?? 0, hasPrevious && previous.averageOrderValue !== null) },
    { key: 'discount', label: t('analytics.sales.discount'), value: money(current.discount), ...delta(current.discount, previous.discount, hasPrevious, true) },
    { key: 'customers', label: t('analytics.sales.customers'), value: String(current.customers), ...delta(current.customers, previous.customers, hasPrevious && previous.customers > 0) },
    { key: 'repeat', label: t('analytics.sales.repeat'), value: percent(current.repeatRatePercent), delta: undefined, trend: 'up' as const },
  ]
})

const trendPoints = computed(() => (report.value?.trend ?? []).map(point => ({
  start: point.start,
  bar: point.revenue,
  cells: [money(point.revenue), String(point.documents)],
})))

const channelItems = computed(() => (report.value?.channels ?? []).map(row => ({
  key: row.key,
  label: t(`analytics.source.${row.key}`),
  value: row.revenue,
  valueText: money(row.revenue),
  detail: t('analytics.sales.channelMeta', { documents: row.documents, aov: money(row.averageOrderValue), share: percent(row.revenueSharePercent), discount: money(row.discount) }),
})))

const customerItems = computed(() => (report.value?.customers ?? []).map(row => ({
  key: row.customerId ?? 'none',
  label: row.customerId ? row.label : t('analytics.sales.walkIn'),
  value: row.revenue,
  valueText: money(row.revenue),
  to: row.customerId ? `/customers/${row.customerId}` : undefined,
  detail: t('analytics.sales.customerMeta', { documents: row.documents, aov: money(row.averageOrderValue), repeat: row.repeat ? t('analytics.sales.returning') : t('analytics.sales.newCustomer') }),
})))

const productItems = computed(() => (report.value?.products ?? []).map(row => ({
  key: row.key,
  label: row.label,
  value: row.revenue,
  valueText: money(row.revenue),
  to: row.productId ? `/reports/margin?${baseQuery.value}&productId=${row.productId}` : undefined,
  detail: t('analytics.sales.productMeta', { units: row.units, margin: percent(row.marginPercent) }),
})))

const leadItems = computed(() => (report.value?.leadTime.buckets ?? []).map(bucket => ({
  key: bucket.key,
  label: t(`analytics.leadBucket.${bucket.key}`),
  value: bucket.orders,
  valueText: String(bucket.orders),
})))

const leadText = computed(() => {
  const lead = report.value?.leadTime
  if (!lead || lead.orders === 0) return ''
  return t('analytics.sales.leadSummary', { average: number(lead.averageDays), median: number(lead.medianDays), longest: number(lead.longestDays), count: lead.orders })
})
</script>

<template>
  <div class="stack">
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
      <p class="range-text">
        {{ rangeText }}
      </p>
      <StateEmpty
        v-if="empty"
        :message="$t('analytics.sales.empty')"
      />
      <template v-else>
        <AnalyticsKpis :tiles="tiles" />
        <p
          class="note"
          role="note"
        >
          {{ $t('analytics.sales.returnsNote') }}
        </p>

        <UiCard>
          <h2>{{ $t('analytics.sales.trendTitle') }}</h2>
          <AnalyticsTrend
            :points="trendPoints"
            :granularity="report.granularity"
            :label="$t('analytics.sales.trendLabel')"
            :bar-label="$t('analytics.revenue')"
            :table-headers="[$t('analytics.revenue'), $t('analytics.sales.documents')]"
            :readout="index => $t('analytics.sales.trendPoint', { revenue: money(report!.trend[index]!.revenue), documents: report!.trend[index]!.documents })"
            :tick-format="value => number(value, 0)"
          />
        </UiCard>

        <UiCard>
          <h2>{{ $t('analytics.sales.byChannel') }}</h2>
          <AnalyticsBars
            :items="channelItems"
            :label="$t('analytics.sales.byChannel')"
          />
        </UiCard>

        <UiCard v-if="customerItems.length > 0">
          <h2>{{ $t('analytics.sales.byCustomer') }}</h2>
          <AnalyticsBars
            :items="customerItems"
            :label="$t('analytics.sales.byCustomer')"
          />
        </UiCard>

        <UiCard v-if="productItems.length > 0">
          <h2>{{ $t('analytics.sales.byProduct') }}</h2>
          <AnalyticsBars
            :items="productItems"
            :label="$t('analytics.sales.byProduct')"
          />
        </UiCard>

        <UiCard>
          <h2>{{ $t('analytics.sales.leadTitle') }}</h2>
          <p
            v-if="!leadText"
            class="hint"
          >
            {{ $t('analytics.sales.leadEmpty') }}
          </p>
          <template v-else>
            <p class="hint">
              {{ leadText }}
            </p>
            <AnalyticsBars
              :items="leadItems"
              :label="$t('analytics.sales.leadTitle')"
            />
          </template>
        </UiCard>

        <UiCard v-if="report.channel !== 'orders'">
          <h2>{{ $t('analytics.sales.timingTitle') }}</h2>
          <p class="hint">
            {{ $t('analytics.sales.timingHint') }}
          </p>
          <AnalyticsHeatmap
            :cells="report.marketTiming.cells"
            :currency="report.currency"
          />
        </UiCard>

        <UiCard>
          <h2>{{ $t('reports.export.title') }}</h2>
          <div class="exports">
            <ReportExportButton
              :path="`/api/reports/analytics/sales/export?${query}&part=channels`"
              :file-name="`sales-channels-${stamp}.csv`"
              :label="$t('analytics.export.channels')"
            />
            <ReportExportButton
              :path="`/api/reports/analytics/sales/export?${query}&part=customers`"
              :file-name="`sales-customers-${stamp}.csv`"
              :label="$t('analytics.export.customers')"
            />
            <ReportExportButton
              :path="`/api/reports/analytics/sales/export?${query}&part=products`"
              :file-name="`sales-products-${stamp}.csv`"
              :label="$t('analytics.export.products')"
            />
            <ReportExportButton
              :path="`/api/reports/analytics/sales/export?${query}&part=timing`"
              :file-name="`sales-timing-${stamp}.csv`"
              :label="$t('analytics.export.timing')"
            />
            <ReportExportButton
              :path="`/api/reports/analytics/sales/export?${query}&part=trend`"
              :file-name="`sales-trend-${stamp}.csv`"
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
.note { margin: 0; padding: var(--space-3); color: var(--text); font-size: var(--font-size-sm); background: var(--accent-soft); border-radius: var(--radius-sm); }
.hint { margin: 0 0 var(--space-2); color: var(--muted); font-size: var(--font-size-sm); }
.exports { display: grid; gap: var(--space-3); }
@media (min-width: 768px) { .exports { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
</style>
