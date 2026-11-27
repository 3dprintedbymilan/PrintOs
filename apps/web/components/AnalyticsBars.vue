<script setup lang="ts">
import { barScale } from '~/utils/analytics'

export interface AnalyticsBarItem {
  key: string
  label: string
  value: number
  valueText: string
  detail?: string
  to?: string
  color?: string
}

const props = defineProps<{
  items: AnalyticsBarItem[]
  label: string
}>()

const scale = computed(() => barScale(props.items.map(item => item.value)))
</script>

<template>
  <ul
    class="bars"
    :aria-label="label"
  >
    <li
      v-for="(item, index) in items"
      :key="item.key"
      class="bar-row"
    >
      <div class="head">
        <NuxtLink
          v-if="item.to"
          :to="item.to"
          class="name"
        >
          {{ item.label }}
        </NuxtLink>
        <span
          v-else
          class="name"
        >{{ item.label }}</span>
        <strong class="value">{{ item.valueText }}</strong>
      </div>
      <svg
        class="track"
        viewBox="0 0 100 8"
        preserveAspectRatio="none"
        aria-hidden="true"
        focusable="false"
      >
        <rect
          class="rail"
          x="0"
          y="0"
          width="100"
          height="8"
          rx="2"
        />
        <rect
          class="fill"
          :class="{ negative: item.value < 0 }"
          :x="scale.bars[index]?.x ?? 0"
          y="0"
          :width="scale.bars[index]?.width ?? 0"
          height="8"
          rx="2"
          :style="item.color && item.value >= 0 ? { fill: item.color } : undefined"
        />
      </svg>
      <p
        v-if="item.detail"
        class="detail"
      >
        {{ item.detail }}
      </p>
    </li>
  </ul>
</template>

<style scoped>
.bars { display: grid; gap: 0; margin: 0; padding: 0; list-style: none; }
.bar-row { display: grid; gap: var(--space-1); padding: var(--space-3) 0; border-bottom: 1px solid var(--border); }
.bar-row:last-child { border-bottom: 0; }
.head { display: flex; align-items: baseline; justify-content: space-between; gap: var(--space-3); }
.name { min-width: 0; color: var(--text); overflow-wrap: anywhere; }
a.name { display: inline-flex; align-items: center; min-height: var(--tap-size); color: var(--accent-text); font-weight: 600; text-decoration: none; }
a.name:focus-visible { box-shadow: var(--focus-ring); border-radius: var(--radius-sm); outline: none; }
.value { font-variant-numeric: tabular-nums; white-space: nowrap; }
.track { display: block; width: 100%; height: 8px; }
.rail { fill: var(--surface-2); }
.fill { fill: var(--accent); }
.fill.negative { fill: var(--danger); }
.detail { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
</style>
