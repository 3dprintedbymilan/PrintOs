<script setup lang="ts">
import { barHeights, trendDate } from '~/utils/dashboard'
import type { DashboardTrendPoint } from '~/utils/dashboard'

const props = defineProps<{ points: DashboardTrendPoint[] }>()
const { t, n, d } = useI18n()

const heights = computed(() => barHeights(props.points))
const total = computed(() => props.points.reduce((sum, point) => sum + point.printHours, 0))

function label(point: DashboardTrendPoint) {
  return t('home.trend.bar', {
    date: d(trendDate(point.date), { dateStyle: 'medium', timeZone: 'UTC' }),
    hours: n(point.printHours, { maximumFractionDigits: 1 }),
    count: point.prints,
  })
}

const first = computed(() => (props.points[0] ? d(trendDate(props.points[0].date), { month: 'short', day: 'numeric', timeZone: 'UTC' }) : ''))
const last = computed(() => {
  const point = props.points[props.points.length - 1]
  return point ? d(trendDate(point.date), { month: 'short', day: 'numeric', timeZone: 'UTC' }) : ''
})
</script>

<template>
  <div class="trend">
    <p
      v-if="total === 0"
      class="calm"
    >
      {{ $t('home.trend.empty') }}
    </p>
    <template v-else>
      <ul
        class="bars"
        :class="{ dense: points.length > 14 }"
      >
        <li
          v-for="(point, index) in points"
          :key="point.date"
          :title="label(point)"
        >
          <span
            class="bar"
            :style="{ height: `${heights[index]}%` }"
          />
          <span class="sr-only">{{ label(point) }}</span>
        </li>
      </ul>
      <p
        v-if="points.length > 1"
        class="axis"
        aria-hidden="true"
      >
        <span>{{ first }}</span>
        <span>{{ last }}</span>
      </p>
    </template>
  </div>
</template>

<style scoped>
.trend { display: grid; gap: var(--space-2); }
.calm { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.bars { display: flex; align-items: flex-end; gap: 6px; height: 120px; margin: 0; padding: 0; list-style: none; border-bottom: 1px solid var(--border); }
.bars.dense { gap: 2px; }
.bars li { flex: 1; display: flex; align-items: flex-end; height: 100%; min-width: 0; }
.bar { display: block; width: 100%; border-radius: 4px 4px 0 0; background: linear-gradient(180deg, var(--accent), color-mix(in srgb, var(--accent) 45%, transparent)); transition: height 0.3s ease; }
.axis { display: flex; justify-content: space-between; margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
@media (prefers-reduced-motion: reduce) { .bar { transition: none; } }
</style>
