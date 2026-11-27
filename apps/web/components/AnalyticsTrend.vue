<script setup lang="ts">
import { niceMax, reportDate } from '~/utils/reports'

export interface AnalyticsTrendPoint {
  start: string
  bar: number
  line?: number
  cells: string[]
}

const props = defineProps<{
  points: AnalyticsTrendPoint[]
  granularity: 'day' | 'week' | 'month'
  label: string
  barLabel: string
  lineLabel?: string
  tableHeaders: string[]
  readout: (index: number) => string
  tickFormat: (value: number) => string
}>()

const { d } = useI18n()
const width = 320
const height = 140
const selected = ref<number | null>(null)
const showTable = ref(false)

const geometry = computed(() => {
  const peak = Math.max(0, ...props.points.map(point => Math.max(point.bar, point.line ?? 0)))
  const floor = Math.min(0, ...props.points.map(point => Math.min(point.bar, point.line ?? 0)))
  const top = niceMax(peak)
  const bottom = floor < 0 ? -niceMax(-floor) : 0
  const span = top - bottom
  const scale = (value: number) => height - ((value - bottom) / span) * height
  const slot = props.points.length === 0 ? width : width / props.points.length
  const barWidth = Math.max(2, Math.min(36, slot * 0.7))
  const zero = scale(0)
  const bars = props.points.map((point, index) => {
    const y = scale(Math.max(point.bar, 0))
    return { x: index * slot + (slot - barWidth) / 2, y, width: barWidth, height: Math.max(point.bar > 0 ? 2 : 0, zero - y) }
  })
  const line = props.points.map((point, index) => ({ x: index * slot + slot / 2, y: scale(point.line ?? 0) }))
  const ticks = [top, top / 2, 0].map(value => ({ value, y: scale(value) }))
  return { bars, line, ticks, slot, hasData: peak > 0 }
})

const linePath = computed(() => (props.lineLabel ? geometry.value.line.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(1)} ${p.y.toFixed(1)}`).join(' ') : ''))

function dateLabel(start: string) {
  const options: Intl.DateTimeFormatOptions = props.granularity === 'month'
    ? { month: 'short', year: '2-digit', timeZone: 'UTC' }
    : { day: 'numeric', month: 'short', timeZone: 'UTC' }
  return d(reportDate(start), options)
}

const readoutText = computed(() => (selected.value === null ? '' : props.readout(selected.value)))
const first = computed(() => (props.points[0] ? dateLabel(props.points[0].start) : ''))
const last = computed(() => (props.points.length ? dateLabel(props.points[props.points.length - 1]!.start) : ''))

function select(index: number) {
  selected.value = index
}

function step(event: KeyboardEvent) {
  const count = props.points.length
  if (count === 0) return
  const current = selected.value ?? -1
  if (event.key === 'ArrowRight') selected.value = Math.min(count - 1, current + 1)
  else if (event.key === 'ArrowLeft') selected.value = Math.max(0, current - 1)
  else return
  event.preventDefault()
}
</script>

<template>
  <div class="chart">
    <p
      v-if="!geometry.hasData"
      class="calm"
    >
      {{ $t('analytics.chart.empty') }}
    </p>
    <template v-else>
      <ul
        class="legend"
        :aria-label="$t('analytics.chart.legend')"
      >
        <li><span class="swatch bar-swatch" />{{ barLabel }}</li>
        <li v-if="lineLabel">
          <span class="swatch line-swatch" />{{ lineLabel }}
        </li>
      </ul>
      <div class="plot">
        <svg
          :viewBox="`0 0 ${width} ${height}`"
          preserveAspectRatio="none"
          role="img"
          tabindex="0"
          :aria-label="`${label}. ${$t('analytics.chart.keyboard')}`"
          @keydown="step"
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
          <path
            v-if="linePath"
            class="line"
            :d="linePath"
            fill="none"
            vector-effect="non-scaling-stroke"
          />
          <rect
            v-for="(point, index) in points"
            :key="`hit-${point.start}`"
            class="hit"
            :x="index * geometry.slot"
            y="0"
            :width="geometry.slot"
            :height="height"
            @pointerenter="select(index)"
            @click="select(index)"
          />
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
            {{ tickFormat(tick.value) }}
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
        {{ readoutText || $t('analytics.chart.hint') }}
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
            {{ label }}
          </caption>
          <thead>
            <tr>
              <th scope="col">
                {{ $t('analytics.chart.date') }}
              </th>
              <th
                v-for="header in tableHeaders"
                :key="header"
                scope="col"
              >
                {{ header }}
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="point in points"
              :key="point.start"
            >
              <th scope="row">
                {{ dateLabel(point.start) }}
              </th>
              <td
                v-for="(cell, index) in point.cells"
                :key="index"
              >
                {{ cell }}
              </td>
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
svg:focus-visible { box-shadow: var(--focus-ring); border-radius: var(--radius-sm); outline: none; }
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
