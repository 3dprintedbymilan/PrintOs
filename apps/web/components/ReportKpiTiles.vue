<script setup lang="ts">
import { deltaTrend } from '~/utils/reports'
import type { MarginReport } from '~/utils/reports'

const props = defineProps<{ report: MarginReport }>()
const { t, n } = useI18n()

const money = (value: number) => n(value, { style: 'currency', currency: props.report.currency })
const percent = (value: number | null) => (value === null ? '-' : `${n(value, { maximumFractionDigits: 1 })}%`)
const signedNumber = (value: number) => `${value > 0 ? '+' : ''}${n(value, { maximumFractionDigits: 1 })}`

function changeText(change: number, percentChange: number | null) {
  if (percentChange === null) return t('reports.deltaAbsolute', { change: `${change > 0 ? '+' : ''}${money(change)}` })
  return t('reports.deltaPercent', { percent: `${signedNumber(percentChange)}%` })
}

const tiles = computed(() => {
  const { current, previous, delta } = props.report
  const hasPrevious = previous.revenue !== 0 || previous.cost !== 0
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
      delta: delta.marginPointsChange === null ? undefined : t('reports.deltaPoints', { points: signedNumber(delta.marginPointsChange) }),
      trend: deltaTrend(delta.marginPointsChange ?? 0),
    },
  ]
})
</script>

<template>
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
</template>

<style scoped>
.tiles { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-3); }
@media (min-width: 768px) { .tiles { grid-template-columns: repeat(4, minmax(0, 1fr)); } }
</style>
