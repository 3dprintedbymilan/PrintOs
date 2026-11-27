<script setup lang="ts">
defineProps<{
  label: string
  hint?: string
  error?: string
  rows?: number
  maxlength?: number
}>()
const model = defineModel<string>({ default: '' })
const id = useId()
</script>

<template>
  <div class="field">
    <label :for="id">{{ label }}</label>
    <textarea
      :id="id"
      v-model="model"
      :rows="rows ?? 3"
      :maxlength="maxlength"
      :aria-invalid="error ? 'true' : undefined"
      :aria-describedby="error || hint ? `${id}-help` : undefined"
    />
    <p
      v-if="error || hint"
      :id="`${id}-help`"
      class="help"
      :class="{ error: !!error }"
    >
      {{ error || hint }}
    </p>
  </div>
</template>

<style scoped>
.field { display: grid; gap: var(--space-1); }
label { font-size: var(--font-size-sm); color: var(--muted); }
textarea {
  width: 100%;
  min-height: var(--tap-size);
  padding: var(--space-3) var(--space-4);
  background: var(--surface-2);
  color: var(--text);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  font: inherit;
  caret-color: var(--accent);
  resize: vertical;
}
textarea[aria-invalid='true'] { border-color: var(--danger); }
.help { margin: 0; font-size: var(--font-size-sm); color: var(--muted); }
.help.error { color: var(--danger); }
</style>
