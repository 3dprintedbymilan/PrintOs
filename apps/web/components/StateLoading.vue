<template>
  <div
    class="state"
    role="status"
    aria-live="polite"
  >
    <span class="spinner" />
    <p>{{ $t('state.loading') }}</p>
  </div>
</template>

<style scoped>
.state { display: grid; place-items: center; gap: 12px; padding: 32px; color: var(--muted); }
.spinner { width: 28px; height: 28px; border: 3px solid var(--surface); border-top-color: var(--accent); border-radius: 50%; animation: spin 0.8s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
</style>
