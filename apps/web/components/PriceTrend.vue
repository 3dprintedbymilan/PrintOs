<script setup lang="ts">
import { changeTone, sparkline } from '~/utils/purchases'
import type { MaterialPriceHistory } from '~/utils/purchases'

const props = defineProps<{ history: MaterialPriceHistory }>()
const { t, n, d } = useI18n()

const width = 280
const height = 56
const line = computed(() => sparkline(props.history.points, width, height))

function money(amount: number) {
  return n(amount, { style: 'currency', currency: props.history.currency })
}

function day(value: string) {
  return d(new Date(`${value}T00:00:00`), { dateStyle: 'medium' })
}

function change(percent: number | null) {
  if (percent === null) return t('prices.noChange')
  const text = n(Math.abs(percent), { maximumFractionDigits: 1 })
  if (percent === 0) return t('prices.unchanged')
  return percent > 0 ? t('prices.up', { percent: text }) : t('prices.down', { percent: text })
}

const summary = computed(() => props.history.points
  .map(point => t('prices.pointLabel', { date: day(point.purchasedOn), supplier: point.supplierName, price: money(point.costPerKg) }))
  .join('; '))
</script>

<template>
  <UiCard class="trend">
    <header class="head">
      <h2>{{ history.materialName }}</h2>
      <p
        v-if="history.latest"
        class="last"
      >
        <strong>{{ $t('prices.perKg', { price: money(history.latest.costPerKg) }) }}</strong>
        <span
          class="change"
          :class="changeTone(history.changePercent)"
        >{{ change(history.changePercent) }}</span>
      </p>
    </header>

    <template v-if="history.latest">
      <p class="meta">
        {{ $t('prices.lastFrom', { supplier: history.latest.supplierName, date: day(history.latest.purchasedOn) }) }}
      </p>
      <svg
        class="spark"
        :viewBox="`0 0 ${width} ${height}`"
        role="img"
        :aria-label="$t('prices.trendLabel', { material: history.materialName, points: summary })"
        preserveAspectRatio="none"
      >
        <path
          v-if="history.points.length > 1"
          :d="line.path"
          fill="none"
          stroke="var(--accent)"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          vector-effect="non-scaling-stroke"
        />
        <circle
          v-if="line.last"
          :cx="line.last.x"
          :cy="line.last.y"
          r="4"
          fill="var(--accent)"
        />
      </svg>
      <p
        v-if="history.points.length < 2"
        class="meta"
      >
        {{ $t('prices.needsTwo') }}
      </p>

      <h3>{{ $t('prices.compare') }}</h3>
      <ul class="suppliers">
        <li
          v-for="supplier in history.suppliers"
          :key="supplier.supplierId"
        >
          <span class="name">
            {{ supplier.supplierName }}
            <UiStatusChip
              v-if="supplier.isCheapest && history.suppliers.length > 1"
              status="success"
            >{{ $t('prices.cheapest') }}</UiStatusChip>
          </span>
          <span class="price">{{ $t('prices.perKg', { price: money(supplier.lastCostPerKg) }) }}</span>
          <span
            class="change small"
            :class="changeTone(supplier.changePercent)"
          >{{ change(supplier.changePercent) }}</span>
        </li>
      </ul>
    </template>
    <p
      v-else
      class="meta"
    >
      {{ $t('prices.noPurchases') }}
    </p>
  </UiCard>
</template>

<style scoped>
.trend { display: grid; gap: var(--space-2); }
.head { display: flex; align-items: baseline; justify-content: space-between; gap: var(--space-2); flex-wrap: wrap; }
h2 { margin: 0; font-size: var(--font-size-md); }
h3 { margin: var(--space-2) 0 0; font-size: var(--font-size-sm); color: var(--muted); font-weight: 600; }
.last { margin: 0; display: flex; align-items: baseline; gap: var(--space-2); }
.meta { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.spark { width: 100%; height: 56px; display: block; }
.change { font-size: var(--font-size-sm); font-variant-numeric: tabular-nums; color: var(--muted); }
.change.up { color: var(--danger); }
.change.down { color: var(--accent); }
.suppliers { list-style: none; margin: 0; padding: 0; display: grid; gap: var(--space-2); }
.suppliers li { display: grid; grid-template-columns: 1fr auto; gap: 0 var(--space-3); align-items: center; padding: var(--space-2) 0; border-top: 1px solid var(--border); }
.name { display: flex; align-items: center; gap: var(--space-2); flex-wrap: wrap; font-weight: 600; }
.price { text-align: right; font-variant-numeric: tabular-nums; }
.small { grid-column: 2; text-align: right; font-size: var(--font-size-xs, 0.75rem); }
</style>
