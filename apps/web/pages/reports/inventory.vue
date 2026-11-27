<script setup lang="ts">
import { deadDayOptions } from '~/utils/analytics'
import type { InventoryAnalytics } from '~/utils/analytics'

definePageMeta({ titleKey: 'analytics.inventory.title' })

const { t } = useI18n()
const deadDays = ref('90')
const { period, channel, from, to, rangeError, report, state, load, query, money, percent, number, rangeText, stamp }
  = useAnalyticsReport<InventoryAnalytics>('/api/reports/analytics/inventory', () => ({ deadDays: deadDays.value }))

const deadOptions = deadDayOptions.map(value => ({ value: String(value), label: t('analytics.inventory.deadOption', { days: value }) }))
const empty = computed(() => !!report.value && report.value.summary.totalValue === 0 && report.value.materials.length === 0 && report.value.finishedGoods.length === 0)
const grams = (value: number) => t('analytics.gramsValue', { grams: number(value, 0) })
const days = (value: number | null) => (value === null ? '-' : t('analytics.daysValue', { days: number(value, 0) }))

const tiles = computed(() => {
  if (!report.value) return []
  const { summary } = report.value
  return [
    { key: 'total', label: t('analytics.inventory.totalValue'), value: money(summary.totalValue) },
    { key: 'filament', label: t('analytics.inventory.filamentValue'), value: money(summary.filamentValue) },
    { key: 'finished', label: t('analytics.inventory.finishedValue'), value: money(summary.finishedValue) },
    { key: 'dead', label: t('analytics.inventory.deadValue'), value: money(summary.deadStockValue) },
    { key: 'fcover', label: t('analytics.inventory.filamentCover'), value: days(summary.filamentDaysOfCover) },
    { key: 'gcover', label: t('analytics.inventory.finishedCover'), value: days(summary.finishedDaysOfCover) },
    { key: 'fturn', label: t('analytics.inventory.filamentTurnover'), value: number(summary.filamentTurnover, 2) },
    { key: 'gturn', label: t('analytics.inventory.finishedTurnover'), value: number(summary.finishedTurnover, 2) },
  ]
})

const materialItems = computed(() => (report.value?.materials ?? []).map(row => ({
  key: row.material,
  label: row.material,
  value: row.value,
  valueText: money(row.value),
  detail: t('analytics.inventory.materialMeta', { onHand: grams(row.onHandGrams), used: grams(row.usedGrams), rate: number(row.usageGramsPerDay, 1), cover: days(row.daysOfCover) }),
})))

const priceRows = computed(() => (report.value?.materials ?? []).filter(row => row.pricePerKgCurrent !== null))

const finishedItems = computed(() => (report.value?.finishedGoods ?? []).map(row => ({
  key: row.productId,
  label: row.label,
  value: row.value,
  valueText: money(row.value),
  to: `/inventory/finished`,
  detail: t('analytics.inventory.finishedMeta', { onHand: row.onHand, sold: row.unitsSold, cover: days(row.daysOfCover) }),
})))

const deadFilamentItems = computed(() => (report.value?.deadFilament ?? []).map(row => ({
  key: row.spoolId,
  label: `${row.material} - ${row.color}`,
  value: row.value,
  valueText: money(row.value),
  to: `/inventory/${row.spoolId}`,
  detail: row.daysSinceLastUse === null
    ? t('analytics.inventory.neverUsed', { grams: grams(row.remainingGrams) })
    : t('analytics.inventory.lastUsed', { grams: grams(row.remainingGrams), days: row.daysSinceLastUse }),
})))

const deadFinishedItems = computed(() => (report.value?.deadFinished ?? []).map(row => ({
  key: row.variantId,
  label: row.label,
  value: row.stockValue,
  valueText: money(row.stockValue),
  to: `/inventory/finished/${row.variantId}`,
  detail: row.daysSinceLastSale === null
    ? t('analytics.inventory.neverSold', { units: row.onHand })
    : t('analytics.inventory.lastSold', { units: row.onHand, days: row.daysSinceLastSale }),
})))
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
      v-model="deadDays"
      :label="$t('analytics.inventory.deadAfter')"
      :options="deadOptions"
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
        :message="$t('analytics.inventory.empty')"
      />
      <template v-else>
        <AnalyticsKpis :tiles="tiles" />
        <p
          class="note"
          role="note"
        >
          {{ $t('analytics.inventory.periodNote') }}
        </p>

        <UiCard v-if="materialItems.length > 0">
          <h2>{{ $t('analytics.inventory.byMaterial') }}</h2>
          <AnalyticsBars
            :items="materialItems"
            :label="$t('analytics.inventory.byMaterial')"
          />
        </UiCard>

        <UiCard v-if="priceRows.length > 0">
          <h2>{{ $t('analytics.inventory.priceTitle') }}</h2>
          <p class="hint">
            {{ $t('analytics.inventory.priceHint') }}
          </p>
          <div class="table-wrap">
            <table>
              <caption class="sr-only">
                {{ $t('analytics.inventory.priceTitle') }}
              </caption>
              <thead>
                <tr>
                  <th scope="col">
                    {{ $t('analytics.inventory.material') }}
                  </th>
                  <th scope="col">
                    {{ $t('analytics.inventory.pricePerKg') }}
                  </th>
                  <th scope="col">
                    {{ $t('analytics.inventory.previousPrice') }}
                  </th>
                  <th scope="col">
                    {{ $t('analytics.inventory.change') }}
                  </th>
                  <th scope="col">
                    {{ $t('analytics.inventory.impact') }}
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in priceRows"
                  :key="row.material"
                >
                  <th scope="row">
                    {{ row.material }}
                  </th>
                  <td>{{ money(row.pricePerKgCurrent) }}</td>
                  <td>{{ money(row.pricePerKgPrevious) }}</td>
                  <td>{{ percent(row.priceChangePercent) }}</td>
                  <td>{{ money(row.priceImpact) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </UiCard>

        <UiCard v-if="finishedItems.length > 0">
          <h2>{{ $t('analytics.inventory.byProduct') }}</h2>
          <AnalyticsBars
            :items="finishedItems"
            :label="$t('analytics.inventory.byProduct')"
          />
        </UiCard>

        <UiCard>
          <h2>{{ $t('analytics.inventory.deadTitle') }}</h2>
          <p
            v-if="deadFilamentItems.length === 0 && deadFinishedItems.length === 0"
            class="hint"
          >
            {{ $t('analytics.inventory.deadEmpty', { days: report.deadDays }) }}
          </p>
          <template v-else>
            <p class="hint">
              {{ $t('analytics.inventory.deadHint', { days: report.deadDays }) }}
            </p>
            <h3 v-if="deadFilamentItems.length > 0">
              {{ $t('analytics.inventory.deadFilament') }}
            </h3>
            <AnalyticsBars
              v-if="deadFilamentItems.length > 0"
              :items="deadFilamentItems"
              :label="$t('analytics.inventory.deadFilament')"
            />
            <h3 v-if="deadFinishedItems.length > 0">
              {{ $t('analytics.inventory.deadFinished') }}
            </h3>
            <AnalyticsBars
              v-if="deadFinishedItems.length > 0"
              :items="deadFinishedItems"
              :label="$t('analytics.inventory.deadFinished')"
            />
          </template>
        </UiCard>

        <UiCard>
          <h2>{{ $t('reports.export.title') }}</h2>
          <div class="exports">
            <ReportExportButton
              :path="`/api/reports/analytics/inventory/export?${query}&part=materials`"
              :file-name="`inventory-materials-${stamp}.csv`"
              :label="$t('analytics.export.materials')"
            />
            <ReportExportButton
              :path="`/api/reports/analytics/inventory/export?${query}&part=finished`"
              :file-name="`inventory-finished-${stamp}.csv`"
              :label="$t('analytics.export.finished')"
            />
            <ReportExportButton
              :path="`/api/reports/analytics/inventory/export?${query}&part=dead`"
              :file-name="`inventory-dead-${stamp}.csv`"
              :label="$t('analytics.export.dead')"
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
h3 { margin: var(--space-3) 0 0; font-size: var(--font-size-sm); color: var(--muted); }
.range-text { margin: 0; color: var(--text); font-size: var(--font-size-sm); font-weight: 600; }
.note { margin: 0; padding: var(--space-3); color: var(--text); font-size: var(--font-size-sm); background: var(--accent-soft); border-radius: var(--radius-sm); }
.hint { margin: 0 0 var(--space-2); color: var(--muted); font-size: var(--font-size-sm); }
.exports { display: grid; gap: var(--space-3); }
@media (min-width: 768px) { .exports { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
.table-wrap { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; font-size: var(--font-size-sm); font-variant-numeric: tabular-nums; }
th, td { padding: var(--space-2); text-align: right; border-bottom: 1px solid var(--border); white-space: nowrap; }
th:first-child { text-align: left; }
thead th { color: var(--muted); font-weight: 600; }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
</style>
