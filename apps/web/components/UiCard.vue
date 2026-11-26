<script setup lang="ts">
withDefaults(defineProps<{ padded?: boolean }>(), { padded: true })
</script>

<template>
  <section
    class="card"
    :class="{ padded }"
  >
    <slot />
  </section>
</template>

<style scoped>
.card {
  background: linear-gradient(160deg, var(--surface), var(--bg-elevated));
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow);
}
.padded { padding: var(--space-4); }
</style>
