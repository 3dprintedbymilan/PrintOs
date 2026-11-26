<script setup lang="ts">
defineProps<{
  label: string
  options: { value: string, label: string }[]
  error?: string
}>()
const model = defineModel<string>({ default: '' })
const id = useId()
</script>

<template>
  <div class="field">
    <label :for="id">{{ label }}</label>
    <select
      :id="id"
      v-model="model"
      :aria-invalid="error ? 'true' : undefined"
      :aria-describedby="error ? `${id}-help` : undefined"
    >
      <option
        v-for="option in options"
        :key="option.value"
        :value="option.value"
      >
        {{ option.label }}
      </option>
    </select>
    <p
      v-if="error"
      :id="`${id}-help`"
      class="help"
    >
      {{ error }}
    </p>
  </div>
</template>

<style scoped>
.field { display: grid; gap: var(--space-1); }
label { font-size: var(--font-size-sm); color: var(--muted); }
select {
  min-height: var(--tap-size);
  padding: 0 var(--space-4);
  background: var(--surface-2);
  color: var(--text);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  font: inherit;
}
select[aria-invalid='true'] { border-color: var(--danger); }
.help { margin: 0; font-size: var(--font-size-sm); color: var(--danger); }
</style>
