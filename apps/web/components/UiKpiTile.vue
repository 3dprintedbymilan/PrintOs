<script setup lang="ts">
withDefaults(defineProps<{
  label: string
  value: string
  delta?: string
  trend?: 'up' | 'down'
}>(), { delta: undefined, trend: 'up' })
</script>

<template>
  <UiCard class="tile">
    <p class="label">
      {{ label }}
    </p>
    <p class="value">
      {{ value }}
    </p>
    <p
      v-if="delta"
      class="delta"
      :class="trend"
    >
      <span aria-hidden="true">{{ trend === 'up' ? '▲' : '▼' }}</span>
      {{ delta }}
    </p>
  </UiCard>
</template>

<style scoped>
.tile { min-width: 0; }
p { margin: 0; }
.label { font-size: var(--font-size-sm); color: var(--muted); }
.value { margin-top: var(--space-1); font-size: var(--font-size-xl); font-weight: 700; }
.delta { margin-top: var(--space-1); font-size: var(--font-size-sm); font-weight: 600; }
.up { color: var(--success); }
.down { color: var(--danger); }
</style>
