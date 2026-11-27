<script setup lang="ts">
import { reportDate, reportQuery } from '~/utils/reports'
import type { MarginReport } from '~/utils/reports'

definePageMeta({ titleKey: 'reports.title' })

const { t, d } = useI18n()
const auth = useAuthStore()
const { period, channel, from, to, rangeError, filter, baseQuery, validate, syncUrl } = useReportFilter()

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
onMounted(load)

const dateText = (value: string) => d(reportDate(value), { dateStyle: 'medium', timeZone: 'UTC' })
const rangeText = computed(() => (report.value ? t('reports.range', { from: dateText(report.value.period.from), to: dateText(report.value.period.to) }) : ''))
const empty = computed(() => !!report.value && report.value.current.units === 0 && report.value.current.revenue === 0 && report.value.current.standCosts === 0)
const topProducts = computed(() => (report.value ? report.value.products.slice(0, 3) : []))
const stamp = computed(() => (report.value ? `${report.value.period.from}_${report.value.period.to}` : ''))
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
        :message="$t('reports.margin.empty')"
      />
      <template v-else>
        <ReportKpiTiles :report="report" />

        <UiCard>
          <h2>{{ $t('reports.trendTitle') }}</h2>
          <ReportTrendChart
            :points="report.trend"
            :currency="report.currency"
            :granularity="report.granularity"
          />
        </UiCard>

        <UiCard v-if="topProducts.length > 0">
          <h2>{{ $t('reports.topProducts') }}</h2>
          <ReportMarginRows
            :rows="topProducts"
            :currency="report.currency"
            :drill-query="baseQuery"
          />
        </UiCard>
      </template>

      <nav
        class="links"
        :aria-label="$t('reports.detailLinks')"
      >
        <UiListRow
          :to="`/reports/margin?${baseQuery}`"
          :title="$t('reports.margin.title')"
          :subtitle="$t('business.marginsSubtitle')"
        />
        <UiListRow
          :to="`/reports/bestsellers?${baseQuery}`"
          :title="$t('reports.bestsellers.title')"
          :subtitle="$t('business.bestsellersSubtitle')"
        />
        <UiListRow
          :to="`/reports/production?${baseQuery}`"
          :title="$t('analytics.production.title')"
          :subtitle="$t('analytics.production.subtitle')"
        />
        <UiListRow
          :to="`/reports/sales?${baseQuery}`"
          :title="$t('analytics.sales.title')"
          :subtitle="$t('analytics.sales.subtitle')"
        />
        <UiListRow
          :to="`/reports/inventory?${baseQuery}`"
          :title="$t('analytics.inventory.title')"
          :subtitle="$t('analytics.inventory.subtitle')"
        />
        <UiListRow
          :to="`/reports/profitability?${baseQuery}`"
          :title="$t('analytics.profit.title')"
          :subtitle="$t('analytics.profit.subtitle')"
        />
      </nav>

      <UiCard>
        <h2>{{ $t('reports.export.title') }}</h2>
        <p class="hint">
          {{ $t('reports.export.hint') }}
        </p>
        <div class="exports">
          <ReportExportButton
            :path="`/api/reports/margin/export?${reportQuery(filter)}`"
            :file-name="`margin-${stamp}.csv`"
            :label="$t('reports.export.margin')"
          />
          <ReportExportButton
            :path="`/api/reports/margin/export?${reportQuery(filter)}&part=trend`"
            :file-name="`margin-trend-${stamp}.csv`"
            :label="$t('reports.export.trend')"
          />
          <ReportExportButton
            :path="`/api/reports/bestsellers/export?${reportQuery(filter)}&sort=revenue`"
            :file-name="`bestsellers-${stamp}.csv`"
            :label="$t('reports.export.bestsellers')"
          />
          <ReportExportButton
            path="/api/reports/slow-movers/export?days=30"
            file-name="slow-movers-30d.csv"
            :label="$t('reports.export.slow')"
          />
        </div>
      </UiCard>
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
h2 { margin: 0 0 var(--space-2); font-size: var(--font-size-md); }
.range-text { margin: 0; color: var(--text); font-size: var(--font-size-sm); font-weight: 600; }
.links { display: grid; gap: var(--space-3); }
.hint { margin: 0 0 var(--space-3); color: var(--muted); font-size: var(--font-size-sm); }
.exports { display: grid; gap: var(--space-3); }
@media (min-width: 768px) { .exports { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
</style>
