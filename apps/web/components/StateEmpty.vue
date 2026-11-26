<script setup lang="ts">
defineProps<{ message?: string }>()
</script>

<template>
  <div class="state">
    <p>{{ message ?? $t('state.empty') }}</p>
  </div>
</template>

<style scoped>
.state { display: grid; place-items: center; padding: 32px; color: var(--muted); }
</style>
