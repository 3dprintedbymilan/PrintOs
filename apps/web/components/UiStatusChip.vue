<script setup lang="ts">
withDefaults(defineProps<{ status?: 'success' | 'warning' | 'danger' | 'info' | 'neutral' }>(), { status: 'neutral' })
</script>

<template>
  <span
    class="chip"
    :class="status"
  >
    <span
      class="dot"
      aria-hidden="true"
    />
    <slot />
  </span>
</template>

<style scoped>
.chip { display: inline-flex; align-items: center; gap: 6px; padding: 4px 10px; border-radius: var(--radius-pill); background: var(--surface-2); font-size: var(--font-size-sm); font-weight: 600; color: var(--text); }
.dot { width: 8px; height: 8px; border-radius: 50%; background: var(--muted); }
.success .dot { background: var(--success); }
.warning .dot { background: var(--warning); }
.danger .dot { background: var(--danger); }
.info .dot { background: var(--info); }
</style>
