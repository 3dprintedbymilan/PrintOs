<script setup lang="ts">
import { heatLevel, hourLabel, weekdayName, weekdayNumbers } from '~/utils/analytics'

const props = defineProps<{
  cells: { weekday: number, hour: number, sales: number, revenue: number }[]
  currency: string
}>()

const { n, t, locale } = useI18n()
const cellWidth = 10
const cellHeight = 14
const labelWidth = 26
const width = labelWidth + 24 * cellWidth
const height = 7 * cellHeight

const selected = ref<{ weekday: number, hour: number } | null>(null)
const showTable = ref(false)

const money = (value: number) => n(value, { style: 'currency', currency: props.currency })
const peak = computed(() => Math.max(0, ...props.cells.map(cell => cell.revenue)))
const byKey = computed(() => new Map(props.cells.map(cell => [`${cell.weekday}-${cell.hour}`, cell])))
const grid = computed(() => weekdayNumbers.flatMap(weekday => Array.from({ length: 24 }, (_, hour) => {
  const cell = byKey.value.get(`${weekday}-${hour}`)
  return { weekday, hour, sales: cell?.sales ?? 0, revenue: cell?.revenue ?? 0, level: heatLevel(cell?.revenue ?? 0, peak.value) }
})))
const readout = computed(() => {
  if (!selected.value) return ''
  const cell = byKey.value.get(`${selected.value.weekday}-${selected.value.hour}`)
  return t('analytics.sales.heatReadout', { day: weekdayName(selected.value.weekday, locale.value), hour: hourLabel(selected.value.hour), sales: cell?.sales ?? 0, revenue: money(cell?.revenue ?? 0) })
})
const rows = computed(() => props.cells.map(cell => ({
  key: `${cell.weekday}-${cell.hour}`,
  day: weekdayName(cell.weekday, locale.value),
  hour: hourLabel(cell.hour),
  sales: String(cell.sales),
  revenue: money(cell.revenue),
})))
</script>

<template>
  <div class="heat">
    <p
      v-if="cells.length === 0"
      class="calm"
    >
      {{ $t('analytics.chart.empty') }}
    </p>
    <template v-else>
      <svg
        :viewBox="`0 0 ${width} ${height + 12}`"
        role="img"
        :aria-label="$t('analytics.sales.heatLabel')"
      >
        <text
          v-for="weekday in weekdayNumbers"
          :key="`d${weekday}`"
          class="axis-text"
          x="0"
          :y="(weekday - 1) * cellHeight + 10"
        >
          {{ weekdayName(weekday, locale) }}
        </text>
        <rect
          v-for="cell in grid"
          :key="`${cell.weekday}-${cell.hour}`"
          class="cell"
          :class="{ active: selected?.weekday === cell.weekday && selected?.hour === cell.hour }"
          :x="labelWidth + cell.hour * cellWidth"
          :y="(cell.weekday - 1) * cellHeight"
          :width="cellWidth - 1.5"
          :height="cellHeight - 1.5"
          rx="2"
          :style="{ fillOpacity: cell.level === 0 ? 0.08 : 0.2 + cell.level * 0.8 }"
          @pointerenter="selected = { weekday: cell.weekday, hour: cell.hour }"
          @click="selected = { weekday: cell.weekday, hour: cell.hour }"
        />
        <text
          v-for="hour in [0, 6, 12, 18]"
          :key="`h${hour}`"
          class="axis-text"
          :x="labelWidth + hour * cellWidth"
          :y="height + 10"
        >
          {{ hourLabel(hour) }}
        </text>
      </svg>
      <p
        class="readout"
        role="status"
      >
        {{ readout || $t('analytics.chart.hint') }}
      </p>
      <button
        type="button"
        class="toggle"
        :aria-expanded="showTable"
        @click="showTable = !showTable"
      >
        {{ showTable ? $t('analytics.chart.hideTable') : $t('analytics.chart.showTable') }}
      </button>
      <div
        v-if="showTable"
        class="table-wrap"
      >
        <table>
          <caption class="sr-only">
            {{ $t('analytics.sales.heatLabel') }}
          </caption>
          <thead>
            <tr>
              <th scope="col">
                {{ $t('analytics.sales.weekday') }}
              </th>
              <th scope="col">
                {{ $t('analytics.sales.hour') }}
              </th>
              <th scope="col">
                {{ $t('analytics.sales.sales') }}
              </th>
              <th scope="col">
                {{ $t('analytics.revenue') }}
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="row in rows"
              :key="row.key"
            >
              <th scope="row">
                {{ row.day }}
              </th>
              <td>{{ row.hour }}</td>
              <td>{{ row.sales }}</td>
              <td>{{ row.revenue }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>

<style scoped>
.heat { display: grid; gap: var(--space-2); }
.calm { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
svg { display: block; width: 100%; height: auto; }
.cell { fill: var(--accent); cursor: pointer; }
.cell.active { stroke: var(--text); stroke-width: 1; }
.axis-text { fill: var(--muted); font-size: 7px; }
.readout { margin: 0; min-height: 1.5em; color: var(--muted); font-size: var(--font-size-sm); }
.toggle { justify-self: start; min-height: var(--tap-size); padding: 0 var(--space-3); color: var(--accent-text); font-weight: 600; background: none; border: 0; cursor: pointer; }
.toggle:focus-visible { box-shadow: var(--focus-ring); border-radius: var(--radius-sm); outline: none; }
.table-wrap { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; font-size: var(--font-size-sm); font-variant-numeric: tabular-nums; }
th, td { padding: var(--space-2); text-align: right; border-bottom: 1px solid var(--border); white-space: nowrap; }
th:first-child { text-align: left; }
thead th { color: var(--muted); font-weight: 600; }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
</style>
