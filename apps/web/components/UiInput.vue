<script setup lang="ts">
defineProps<{
  label: string
  type?: string
  hint?: string
  error?: string
  placeholder?: string
  inputmode?: 'text' | 'numeric' | 'decimal' | 'email'
}>()
const model = defineModel<string>({ default: '' })
const id = useId()
</script>

<template>
  <div class="field">
    <label :for="id">{{ label }}</label>
    <input
      :id="id"
      v-model="model"
      :type="type ?? 'text'"
      :placeholder="placeholder"
      :inputmode="inputmode"
      :aria-invalid="error ? 'true' : undefined"
      :aria-describedby="error || hint ? `${id}-help` : undefined"
    >
    <p
      v-if="error || hint"
      :id="`${id}-help`"
      class="help"
      :class="{ error: !!error }"
    >
      {{ error ?? hint }}
    </p>
  </div>
</template>

<style scoped>
.field { display: grid; gap: var(--space-1); }
label { font-size: var(--font-size-sm); color: var(--muted); }
input {
  min-height: var(--tap-size);
  padding: 0 var(--space-4);
  background: var(--surface-2);
  color: var(--text);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  font: inherit;
}
input[aria-invalid='true'] { border-color: var(--danger); }
.help { margin: 0; font-size: var(--font-size-sm); color: var(--muted); }
.help.error { color: var(--danger); }
</style>
