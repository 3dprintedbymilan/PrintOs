<script setup lang="ts">
import { reportChannels, reportPeriods } from '~/utils/reports'
import type { ReportChannel, ReportPeriodKind } from '~/utils/reports'

withDefaults(defineProps<{ rangeError: 'from' | 'to' | null, showChannel?: boolean }>(), { showChannel: true })

const period = defineModel<ReportPeriodKind>('period', { required: true })
const channel = defineModel<ReportChannel>('channel', { required: true })
const from = defineModel<string>('from', { required: true })
const to = defineModel<string>('to', { required: true })

const { t } = useI18n()
const periodOptions = computed(() => reportPeriods.map(value => ({ value, label: t(`reports.period.${value}`) })))
const channelOptions = computed(() => reportChannels.map(value => ({ value, label: t(`reports.channel.${value}`) })))
</script>

<template>
  <UiCard class="filters">
    <UiSelect
      v-model="period"
      :label="$t('reports.period.label')"
      :options="periodOptions"
    />
    <div
      v-if="period === 'custom'"
      class="range"
    >
      <div data-range="from">
        <UiInput
          v-model="from"
          type="date"
          :label="$t('reports.from')"
          :error="rangeError === 'from' ? $t('reports.errors.from') : undefined"
        />
      </div>
      <div data-range="to">
        <UiInput
          v-model="to"
          type="date"
          :label="$t('reports.to')"
          :error="rangeError === 'to' ? $t('reports.errors.to') : undefined"
        />
      </div>
    </div>
    <UiSegmentedControl
      v-if="showChannel"
      v-model="channel"
      :label="$t('reports.channel.label')"
      :options="channelOptions"
    />
  </UiCard>
</template>

<style scoped>
.filters { display: grid; gap: var(--space-3); }
.range { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: var(--space-3); }
</style>
