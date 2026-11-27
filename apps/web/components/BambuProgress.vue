<script setup lang="ts">
import type { BambuPrinterStatus } from '~/utils/bambu'
import { clampPercent, formatRemaining } from '~/utils/bambu'

const props = defineProps<{ printer: BambuPrinterStatus, detailed?: boolean }>()
const { t } = useI18n()

const percent = computed(() => clampPercent(props.printer.progressPercent))
const remaining = computed(() => formatRemaining(props.printer.remainingMinutes))
const layers = computed(() => (props.printer.layer !== null && props.printer.totalLayers
  ? t('bambu.live.layer', { layer: props.printer.layer, total: props.printer.totalLayers })
  : ''))
const filament = computed(() => props.printer.filamentType ?? '')
</script>

<template>
  <div class="progress">
    <div
      class="bar"
      role="progressbar"
      :aria-label="printer.jobName ?? $t('bambu.now.title')"
      aria-valuemin="0"
      aria-valuemax="100"
      :aria-valuenow="percent"
    >
      <span
        class="fill"
        :style="{ width: `${percent}%` }"
      />
    </div>
    <p class="meta">
      <span class="percent">{{ percent }}%</span>
      <span v-if="remaining">{{ $t('bambu.live.remaining', { time: remaining }) }}</span>
      <span v-if="layers">{{ layers }}</span>
    </p>
    <p
      v-if="detailed"
      class="meta"
    >
      <span v-if="printer.nozzleTemperature !== null">{{ $t('bambu.live.nozzle', { value: Math.round(printer.nozzleTemperature) }) }}</span>
      <span v-if="printer.bedTemperature !== null">{{ $t('bambu.live.bed', { value: Math.round(printer.bedTemperature) }) }}</span>
      <span
        v-if="filament || printer.filamentColor"
        class="filament"
      >
        <span
          v-if="printer.filamentColor"
          class="swatch"
          :style="{ background: printer.filamentColor }"
          aria-hidden="true"
        />
        {{ filament }}
      </span>
    </p>
  </div>
</template>

<style scoped>
.progress { display: grid; gap: var(--space-1); }
.bar { height: 8px; overflow: hidden; background: var(--surface-2); border-radius: var(--radius-pill); }
.fill { display: block; height: 100%; background: var(--accent); border-radius: var(--radius-pill); transition: width 0.4s; }
.meta { display: flex; flex-wrap: wrap; gap: var(--space-1) var(--space-3); margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.percent { color: var(--text); font-weight: 600; }
.filament { display: inline-flex; align-items: center; gap: 6px; }
.swatch { width: 12px; height: 12px; border: 1px solid var(--border); border-radius: 50%; }
@media (prefers-reduced-motion: reduce) { .fill { transition: none; } }
</style>
