<script setup lang="ts">
definePageMeta({ titleKey: 'home.title' })
</script>

<template>
  <div class="stack">
    <p class="subtitle">
      {{ $t('home.subtitle') }}
    </p>
    <StockAlertsCard />
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-4); }
.subtitle { margin: 0; color: var(--muted); }
</style>
