<script setup lang="ts">
import { lineShare, profitMarginPercent } from '~/utils/costCalculator'
import type { CostBreakdown, CostLine } from '~/utils/costCalculator'

const props = defineProps<{ breakdown: CostBreakdown }>()

const { t, locale } = useI18n()

function money(amount: number, precise = false) {
  return new Intl.NumberFormat(locale.value, {
    style: 'currency',
    currency: props.breakdown.currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: precise ? 4 : 2,
  }).format(amount)
}

function quantity(value: number) {
  return new Intl.NumberFormat(locale.value, { maximumFractionDigits: 3 }).format(value)
}

function label(line: CostLine) {
  return line.kind === 'extra' ? line.label : t(`calculator.lines.${line.kind}`)
}

function detail(line: CostLine) {
  if (line.kind === 'packaging' || line.kind === 'extra') return ''
  return `${quantity(line.quantity)} ${line.unit} × ${money(line.unitCost, true)}`
}

const margin = computed(() => profitMarginPercent(props.breakdown))
</script>

<template>
  <div class="breakdown">
    <ul
      class="lines"
      :aria-label="$t('calculator.breakdown')"
    >
      <li
        v-for="(line, index) in breakdown.lines"
        :key="`${line.kind}-${index}`"
        class="line"
      >
        <div class="text">
          <span class="name">{{ label(line) }}</span>
          <span
            v-if="detail(line)"
            class="detail"
          >{{ detail(line) }}</span>
          <span
            class="bar"
            aria-hidden="true"
          ><span :style="{ width: `${lineShare(line, breakdown)}%` }" /></span>
        </div>
        <span class="amount">{{ money(line.amount) }}</span>
      </li>
    </ul>

    <dl class="totals">
      <div class="row">
        <dt>{{ $t('calculator.totalCost') }}</dt>
        <dd>{{ money(breakdown.totalCost) }}</dd>
      </div>
      <div
        v-if="breakdown.feePercent > 0"
        class="row"
      >
        <dt>{{ $t('calculator.fees', { percent: breakdown.feePercent }) }}</dt>
        <dd>{{ money(breakdown.feeAmount) }}</dd>
      </div>
      <div class="row">
        <dt>{{ $t('calculator.profit') }}</dt>
        <dd class="profit">
          {{ money(breakdown.profit) }} <span class="share">({{ margin }}%)</span>
        </dd>
      </div>
      <div class="row price">
        <dt>{{ $t('calculator.suggestedPrice') }}</dt>
        <dd>{{ money(breakdown.suggestedPrice) }}</dd>
      </div>
    </dl>
  </div>
</template>

<style scoped>
.breakdown { display: grid; gap: var(--space-4); }
.lines { display: grid; gap: var(--space-3); margin: 0; padding: 0; list-style: none; }
.line { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-3); }
.text { display: grid; gap: 2px; min-width: 0; flex: 1; }
.name { font-weight: 600; overflow-wrap: anywhere; }
.detail { color: var(--muted); font-size: var(--font-size-sm); }
.bar { display: block; height: 4px; margin-top: var(--space-1); border-radius: var(--radius-pill); background: var(--surface-2); overflow: hidden; }
.bar span { display: block; height: 100%; border-radius: inherit; background: var(--accent); }
.amount { font-variant-numeric: tabular-nums; font-weight: 600; white-space: nowrap; }
.totals { display: grid; gap: var(--space-2); margin: 0; padding-top: var(--space-3); border-top: 1px solid var(--border); }
.row { display: flex; justify-content: space-between; gap: var(--space-3); }
dt { color: var(--muted); }
dd { margin: 0; font-variant-numeric: tabular-nums; font-weight: 600; text-align: right; }
.profit { color: var(--accent-text); }
.share { color: var(--muted); font-weight: 400; font-size: var(--font-size-sm); }
.price dt { color: var(--text); font-weight: 700; }
.price dd { font-size: var(--font-size-lg); font-weight: 800; }
</style>
