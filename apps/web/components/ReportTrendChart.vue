<script setup lang="ts">
import { chartGeometry, reportDate } from '~/utils/reports'
import type { MarginTrendPoint } from '~/utils/reports'

const props = defineProps<{
  points: MarginTrendPoint[]
  currency: string
  granularity: 'day' | 'week' | 'month'
}>()

const { n, d, t } = useI18n()

const width = 320
const height = 140
const geometry = computed(() => chartGeometry(props.points, width, height))
const selected = ref<number | null>(null)
const showTable = ref(false)

const money = (value: number) => n(value, { style: 'currency', currency: props.currency })
const compact = (value: number) => n(value, { notation: 'compact', maximumFractionDigits: 1 })

function dateLabel(start: string) {
  const options: Intl.DateTimeFormatOptions = props.granularity === 'month'
    ? { month: 'short', year: '2-digit', timeZone: 'UTC' }
    : { day: 'numeric', month: 'short', timeZone: 'UTC' }
  return d(reportDate(start), options)
}

function pointText(point: MarginTrendPoint) {
  return t('reports.chart.point', {
    date: dateLabel(point.start),
    revenue: money(point.revenue),
    profit: money(point.grossProfit),
  })
}

const readout = computed(() => {
  const point = selected.value === null ? null : props.points[selected.value]
  return point ? pointText(point) : t('reports.chart.hint')
})

const linePath = computed(() => geometry.value.line.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(1)} ${p.y.toFixed(1)}`).join(' '))
const tableRows = computed(() => props.points.map(point => ({
  date: dateLabel(point.start),
  revenue: money(point.revenue),
  cost: money(point.cost),
  grossProfit: money(point.grossProfit),
  margin: point.marginPercent === null ? '-' : `${n(point.marginPercent, { maximumFractionDigits: 1 })}%`,
})))
const first = computed(() => (props.points[0] ? dateLabel(props.points[0].start) : ''))
const last = computed(() => (props.points.length ? dateLabel(props.points[props.points.length - 1]!.start) : ''))
</script>

<template>
  <div class="chart">
    <p
      v-if="!geometry.hasData"
      class="calm"
    >
      {{ $t('reports.chart.empty') }}
    </p>
    <template v-else>
      <ul
        class="legend"
        :aria-label="$t('reports.chart.legend')"
      >
        <li><span class="swatch bar-swatch" />{{ $t('reports.revenue') }}</li>
        <li><span class="swatch line-swatch" />{{ $t('reports.grossProfit') }}</li>
      </ul>
      <div class="plot">
        <svg
          :viewBox="`0 0 ${width} ${height}`"
          preserveAspectRatio="none"
          role="img"
          :aria-label="$t('reports.chart.label')"
        >
          <g class="grid">
            <line
              v-for="tick in geometry.ticks"
              :key="tick.value"
              x1="0"
              :x2="width"
              :y1="tick.y"
              :y2="tick.y"
            />
          </g>
          <g>
            <rect
              v-for="(bar, index) in geometry.bars"
              :key="points[index]!.start"
              class="bar"
              :class="{ active: selected === index }"
              :x="bar.x"
              :y="bar.y"
              :width="bar.width"
              :height="bar.height"
              rx="3"
            />
          </g>
          <path
            class="line"
            :d="linePath"
            fill="none"
            vector-effect="non-scaling-stroke"
          />
          <g>
            <rect
              v-for="(point, index) in points"
              :key="`hit-${point.start}`"
              class="hit"
              :x="index * (width / points.length)"
              y="0"
              :width="width / points.length"
              :height="height"
              @pointerenter="selected = index"
              @click="selected = index"
            />
          </g>
        </svg>
        <ul
          class="ticks"
          aria-hidden="true"
        >
          <li
            v-for="tick in geometry.ticks"
            :key="tick.value"
            :style="{ top: `${(tick.y / height) * 100}%` }"
          >
            {{ compact(tick.value) }}
          </li>
        </ul>
      </div>
      <p
        v-if="points.length > 1"
        class="axis"
        aria-hidden="true"
      >
        <span>{{ first }}</span>
        <span>{{ last }}</span>
      </p>
      <p
        class="readout"
        role="status"
      >
        {{ readout }}
      </p>
      <button
        type="button"
        class="toggle"
        :aria-expanded="showTable"
        @click="showTable = !showTable"
      >
        {{ showTable ? $t('reports.chart.hideTable') : $t('reports.chart.showTable') }}
      </button>
      <div
        v-if="showTable"
        class="table-wrap"
      >
        <table>
          <caption class="sr-only">
            {{ $t('reports.chart.label') }}
          </caption>
          <thead>
            <tr>
              <th scope="col">
                {{ $t('reports.chart.date') }}
              </th>
              <th scope="col">
                {{ $t('reports.revenue') }}
              </th>
              <th scope="col">
                {{ $t('reports.cost') }}
              </th>
              <th scope="col">
                {{ $t('reports.grossProfit') }}
              </th>
              <th scope="col">
                {{ $t('reports.marginPercent') }}
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="row in tableRows"
              :key="row.date"
            >
              <th scope="row">
                {{ row.date }}
              </th>
              <td>{{ row.revenue }}</td>
              <td>{{ row.cost }}</td>
              <td>{{ row.grossProfit }}</td>
              <td>{{ row.margin }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>

<style scoped>
.chart { display: grid; gap: var(--space-2); }
.calm { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.legend { display: flex; flex-wrap: wrap; gap: var(--space-4); margin: 0; padding: 0; list-style: none; color: var(--text); font-size: var(--font-size-sm); }
.legend li { display: flex; align-items: center; gap: var(--space-2); }
.swatch { display: inline-block; }
.bar-swatch { width: 12px; height: 12px; border-radius: 3px; background: var(--accent); }
.line-swatch { width: 16px; height: 0; border-top: 3px dashed var(--info); }
.plot { position: relative; padding-left: 36px; }
svg { display: block; width: 100%; height: 160px; overflow: visible; }
.grid line { stroke: var(--border); stroke-width: 1; vector-effect: non-scaling-stroke; }
.bar { fill: var(--accent); opacity: 0.85; }
.bar.active { opacity: 1; }
.line { stroke: var(--info); stroke-width: 2; stroke-dasharray: 6 3; stroke-linejoin: round; }
.hit { fill: transparent; cursor: pointer; }
.ticks { position: absolute; inset: 0 auto 0 0; width: 34px; margin: 0; padding: 0; list-style: none; }
.ticks li { position: absolute; right: 0; transform: translateY(-50%); color: var(--muted); font-size: 0.6875rem; }
.axis { display: flex; justify-content: space-between; margin: 0; padding-left: 36px; color: var(--muted); font-size: var(--font-size-sm); }
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
