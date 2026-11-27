<script setup lang="ts">
import type { MarginRow } from '~/utils/reports'

defineProps<{
  rows: MarginRow[]
  currency: string
  labelFor?: (row: MarginRow) => string
  drillQuery?: string
}>()

const { n } = useI18n()
const money = (value: number, currency: string) => n(value, { style: 'currency', currency })
const percent = (value: number | null) => (value === null ? '-' : `${n(value, { maximumFractionDigits: 1 })}%`)
</script>

<template>
  <ul class="rows">
    <li
      v-for="row in rows"
      :key="row.key"
      class="row"
    >
      <div class="head">
        <NuxtLink
          v-if="drillQuery !== undefined && row.productId && !row.variantId"
          :to="`/reports/margin?${drillQuery}&productId=${row.productId}`"
          class="name"
        >
          {{ labelFor ? labelFor(row) : row.label }}
        </NuxtLink>
        <strong
          v-else
          class="name"
        >{{ labelFor ? labelFor(row) : row.label }}</strong>
        <span class="profit">{{ money(row.figures.grossProfit, currency) }}</span>
      </div>
      <p class="meta">
        {{ $t('reports.rowMeta', { revenue: money(row.figures.revenue, currency), margin: percent(row.figures.marginPercent) }, row.figures.units) }}
      </p>
      <p
        v-if="row.figures.standCosts > 0"
        class="meta"
      >
        {{ $t('reports.rowStand', { cost: money(row.figures.standCosts, currency), net: money(row.figures.netProfit, currency) }) }}
      </p>
    </li>
  </ul>
</template>

<style scoped>
.rows { display: grid; gap: 0; margin: 0; padding: 0; list-style: none; }
.row { display: grid; gap: 2px; padding: var(--space-3) 0; border-bottom: 1px solid var(--border); }
.row:last-child { border-bottom: 0; }
.head { display: flex; align-items: baseline; justify-content: space-between; gap: var(--space-3); }
.name { min-width: 0; color: var(--text); overflow-wrap: anywhere; }
a.name { color: var(--accent-text); font-weight: 600; text-decoration: none; }
.profit { font-weight: 700; font-variant-numeric: tabular-nums; white-space: nowrap; }
.meta { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
</style>
