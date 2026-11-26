<script setup lang="ts">
const props = withDefaults(defineProps<{
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger'
  type?: 'button' | 'submit'
  loading?: boolean
  disabled?: boolean
  block?: boolean
}>(), { variant: 'primary', type: 'button' })

function blockWhileLoading(event: MouseEvent) {
  if (props.loading) event.preventDefault()
}
</script>

<template>
  <button
    class="btn"
    :class="[variant, { block, loading }]"
    :type="type"
    :disabled="disabled"
    :aria-disabled="loading || undefined"
    :aria-busy="loading || undefined"
    @click="blockWhileLoading"
  >
    <span
      v-if="loading"
      class="spinner"
      aria-hidden="true"
    />
    <slot />
  </button>
</template>

<style scoped>
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-2);
  min-height: var(--tap-size);
  padding: 0 var(--space-5);
  border: 1px solid transparent;
  border-radius: var(--radius-pill);
  font: inherit;
  font-weight: 600;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
  transition: filter 0.15s, background 0.15s;
}
.btn:disabled { opacity: 0.5; cursor: not-allowed; }
.btn.loading { opacity: 0.75; cursor: progress; }
@media (hover: hover) {
  .btn:not(:disabled):hover { filter: brightness(1.08); }
}
.block { width: 100%; }
.primary { background: var(--accent); color: var(--accent-contrast); }
.secondary { background: var(--surface-2); color: var(--text); border-color: var(--border); }
.ghost { background: transparent; color: var(--accent-text); }
.danger { background: var(--danger); color: #fff; }
.spinner { width: 16px; height: 16px; border: 2px solid currentColor; border-right-color: transparent; border-radius: 50%; animation: spin 0.7s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
</style>
