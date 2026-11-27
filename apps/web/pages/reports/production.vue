<script setup lang="ts">
import { hoursPerDayOptions } from '~/utils/analytics'
import type { ProductionAnalytics } from '~/utils/analytics'

definePageMeta({ titleKey: 'analytics.production.title' })

const { t } = useI18n()
const hoursPerDay = ref('24')
const { period, channel, from, to, rangeError, report, state, load, query, money, percent, number, hours, rangeText, stamp, delta }
  = useAnalyticsReport<ProductionAnalytics>('/api/reports/analytics/production', () => ({ hoursPerDay: hoursPerDay.value }))

const hourOptions = hoursPerDayOptions.map(value => ({ value: String(value), label: t('analytics.production.hoursOption', { hours: value }) }))
const empty = computed(() => !!report.value && report.value.current.jobs === 0)

const tiles = computed(() => {
  if (!report.value) return []
  const { current, previous } = report.value
  const hasPrevious = previous.jobs > 0
  return [
    { key: 'hours', label: t('analytics.production.printHours'), value: hours(current.printHours), ...delta(current.printHours, previous.printHours, hasPrevious) },
    { key: 'util', label: t('analytics.production.utilisation'), value: percent(current.utilisationPercent), delta: undefined, trend: 'up' as const },
    { key: 'success', label: t('analytics.production.successRate'), value: percent(current.successRatePercent), ...delta(current.successRatePercent ?? 0, previous.successRatePercent ?? 0, hasPrevious && previous.successRatePercent !== null) },
    { key: 'waste', label: t('analytics.production.wastePercent'), value: percent(current.wastePercent), ...delta(current.wastePercent ?? 0, previous.wastePercent ?? 0, hasPrevious && previous.wastePercent !== null, true) },
    { key: 'grams', label: t('analytics.production.filament'), value: t('analytics.gramsValue', { grams: number(current.filamentGrams, 0) }), ...delta(current.filamentGrams, previous.filamentGrams, hasPrevious) },
    { key: 'cph', label: t('analytics.production.costPerHour'), value: money(current.costPerPrintHour), delta: undefined, trend: 'up' as const },
    { key: 'jobs', label: t('analytics.production.jobs'), value: String(current.done + current.failed), ...delta(current.done + current.failed, previous.done + previous.failed, hasPrevious) },
    { key: 'failed', label: t('analytics.production.failed'), value: String(current.failed), ...delta(current.failed, previous.failed, hasPrevious, true) },
  ]
})

const trendPoints = computed(() => (report.value?.trend ?? []).map(point => ({
  start: point.start,
  bar: point.printHours,
  cells: [hours(point.printHours), String(point.jobs), String(point.failed), t('analytics.gramsValue', { grams: number(point.filamentGrams, 0) })],
})))

const printerItems = computed(() => (report.value?.printers ?? []).map(row => ({
  key: row.printerId,
  label: row.name,
  value: row.printHours,
  valueText: hours(row.printHours),
  to: `/printers/${row.printerId}`,
  detail: t('analytics.production.printerMeta', {
    utilisation: percent(row.utilisationPercent),
    success: percent(row.successRatePercent),
    waste: percent(row.wastePercent),
    cost: money(row.costPerPrintHour),
  }),
})))

const materialItems = computed(() => (report.value?.materials ?? []).map(row => ({
  key: row.material,
  label: row.material,
  value: row.grams,
  valueText: t('analytics.gramsValue', { grams: number(row.grams, 0) }),
  detail: t('analytics.production.materialMeta', {
    jobs: row.jobs,
    failed: row.failed,
    rate: percent(row.failureRatePercent),
    waste: percent(row.wastePercent),
    lost: number(row.failedGrams, 0),
  }),
})))

const durationText = computed(() => {
  const totals = report.value?.current
  if (!totals || totals.averageEstimatedMinutes === null || totals.averageActualMinutes === null) return ''
  return t('analytics.production.durationVsEstimate', {
    actual: number(totals.averageActualMinutes, 0),
    estimate: number(totals.averageEstimatedMinutes, 0),
    variance: `${(totals.durationVariancePercent ?? 0) > 0 ? '+' : ''}${percent(totals.durationVariancePercent)}`,
    count: totals.jobsWithEstimate,
  })
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
      :show-channel="false"
    />
    <UiSelect
      v-model="hoursPerDay"
      :label="$t('analytics.production.hoursPerDay')"
      :options="hourOptions"
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
        :message="$t('analytics.production.empty')"
      />
      <template v-else>
        <AnalyticsKpis :tiles="tiles" />
        <p
          v-if="durationText"
          class="note"
          role="note"
        >
          {{ durationText }}
        </p>

        <UiCard>
          <h2>{{ $t('analytics.production.trendTitle') }}</h2>
          <AnalyticsTrend
            :points="trendPoints"
            :granularity="report.granularity"
            :label="$t('analytics.production.trendLabel')"
            :bar-label="$t('analytics.production.printHours')"
            :table-headers="[$t('analytics.production.printHours'), $t('analytics.production.jobs'), $t('analytics.production.failed'), $t('analytics.production.filament')]"
            :readout="index => $t('analytics.production.trendPoint', { hours: hours(report!.trend[index]!.printHours), jobs: report!.trend[index]!.jobs, failed: report!.trend[index]!.failed })"
            :tick-format="value => number(value, 1)"
          />
        </UiCard>

        <UiCard v-if="printerItems.length > 0">
          <h2>{{ $t('analytics.production.byPrinter') }}</h2>
          <AnalyticsBars
            :items="printerItems"
            :label="$t('analytics.production.byPrinter')"
          />
        </UiCard>

        <UiCard v-if="materialItems.length > 0">
          <h2>{{ $t('analytics.production.byMaterial') }}</h2>
          <p class="hint">
            {{ $t('analytics.production.causesHint') }}
          </p>
          <AnalyticsBars
            :items="materialItems"
            :label="$t('analytics.production.byMaterial')"
          />
        </UiCard>

        <UiCard>
          <h2>{{ $t('reports.export.title') }}</h2>
          <div class="exports">
            <ReportExportButton
              :path="`/api/reports/analytics/production/export?${query}&part=printers`"
              :file-name="`production-printers-${stamp}.csv`"
              :label="$t('analytics.export.printers')"
            />
            <ReportExportButton
              :path="`/api/reports/analytics/production/export?${query}&part=materials`"
              :file-name="`production-materials-${stamp}.csv`"
              :label="$t('analytics.export.materials')"
            />
            <ReportExportButton
              :path="`/api/reports/analytics/production/export?${query}&part=trend`"
              :file-name="`production-trend-${stamp}.csv`"
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
