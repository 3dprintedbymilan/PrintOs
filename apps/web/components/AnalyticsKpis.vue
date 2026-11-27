<script setup lang="ts">
defineProps<{
  tiles: { key: string, label: string, value: string, delta?: string, trend?: 'up' | 'down' }[]
}>()
</script>

<template>
  <div
    class="tiles"
    role="group"
    :aria-label="$t('reports.kpiLabel')"
  >
    <UiKpiTile
      v-for="tile in tiles"
      :key="tile.key"
      :label="tile.label"
      :value="tile.value"
      :delta="tile.delta"
      :trend="tile.trend"
    />
  </div>
</template>

<style scoped>
.tiles { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-3); }
@media (min-width: 768px) { .tiles { grid-template-columns: repeat(4, minmax(0, 1fr)); } }
</style>
