<script setup lang="ts">
defineProps<{ message?: string }>()
defineEmits<{ retry: [] }>()
</script>

<template>
  <div
    class="state"
    role="alert"
  >
    <p>{{ message ?? $t('state.error') }}</p>
    <button
      type="button"
      @click="$emit('retry')"
    >
      {{ $t('state.retry') }}
    </button>
  </div>
</template>

<style scoped>
.state { display: grid; place-items: center; gap: 12px; padding: 32px; color: var(--danger); }
button { background: var(--accent); color: #04110c; border: 0; border-radius: 12px; padding: 10px 18px; font-weight: 600; }
</style>
