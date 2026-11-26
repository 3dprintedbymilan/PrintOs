<script setup lang="ts">
defineProps<{
  label: string
  options: { value: string, label: string }[]
}>()
const model = defineModel<string>({ required: true })
const name = useId()
</script>

<template>
  <div
    class="segmented"
    role="radiogroup"
    :aria-label="label"
  >
    <label
      v-for="option in options"
      :key="option.value"
      class="segment"
      :class="{ active: model === option.value }"
    >
      <input
        v-model="model"
        type="radio"
        :name="name"
        :value="option.value"
      >
      <span>{{ option.label }}</span>
    </label>
  </div>
</template>

<style scoped>
.segmented { display: flex; gap: 4px; padding: 4px; background: var(--surface-2); border: 1px solid var(--border); border-radius: var(--radius-pill); }
.segment { position: relative; flex: 1; text-align: center; border-radius: var(--radius-pill); cursor: pointer; }
.segment span { display: block; padding: 10px 14px; min-height: 40px; font-weight: 600; color: var(--muted); border-radius: var(--radius-pill); }
.segment input { position: absolute; opacity: 0; inset: 0; margin: 0; cursor: pointer; }
.segment.active span { background: var(--accent); color: var(--accent-contrast); }
.segment:has(input:focus-visible) { box-shadow: var(--focus-ring); }
</style>
