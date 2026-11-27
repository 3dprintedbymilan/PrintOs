<script setup lang="ts">
const props = defineProps<{
  label: string
  type?: string
  hint?: string
  error?: string
  placeholder?: string
  inputmode?: 'text' | 'numeric' | 'decimal' | 'email'
  autocomplete?: string
  name?: string
  revealable?: boolean
  showLabel?: string
  hideLabel?: string
}>()
const model = defineModel<string>({ default: '' })
const id = useId()
const revealed = ref(false)
const inputType = computed(() => (props.revealable && revealed.value ? 'text' : (props.type ?? 'text')))
</script>

<template>
  <div class="field">
    <label :for="id">{{ label }}</label>
    <div class="control">
      <input
        :id="id"
        v-model="model"
        :type="inputType"
        :placeholder="placeholder"
        :inputmode="inputmode"
        :autocomplete="autocomplete"
        :name="name"
        :aria-invalid="error ? 'true' : undefined"
        :aria-describedby="error || hint ? `${id}-help` : undefined"
        :class="{ withToggle: revealable }"
      >
      <button
        v-if="revealable"
        type="button"
        class="toggle"
        :aria-label="revealed ? (hideLabel ?? $t('common.hidePassword')) : (showLabel ?? $t('common.showPassword'))"
        @click="revealed = !revealed"
      >
        <svg
          viewBox="0 0 24 24"
          width="22"
          height="22"
          fill="none"
          stroke="currentColor"
          stroke-width="1.8"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path d="M2 12s3.6-6.5 10-6.5S22 12 22 12s-3.6 6.5-10 6.5S2 12 2 12z" />
          <circle
            cx="12"
            cy="12"
            r="3"
          />
          <path
            v-if="!revealed"
            d="M4 4l16 16"
          />
        </svg>
      </button>
    </div>
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
.control { position: relative; }
input {
  width: 100%;
  min-height: var(--tap-size);
  padding: 0 var(--space-4);
  background: var(--surface-2);
  color: var(--text);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  font: inherit;
  caret-color: var(--accent);
}
input::placeholder { color: var(--muted); opacity: 0.7; }
input::selection { background: var(--accent-soft); }
input.withToggle { padding-right: 52px; }
input[aria-invalid='true'] { border-color: var(--danger); }
.toggle {
  position: absolute;
  inset: 0 0 0 auto;
  display: grid;
  place-items: center;
  width: var(--tap-size);
  color: var(--muted);
  background: transparent;
  border: 0;
  border-radius: var(--radius-md);
  cursor: pointer;
}
.toggle { -webkit-tap-highlight-color: transparent; }
@media (hover: hover) {
  .toggle:hover { color: var(--text); }
}
.help { margin: 0; font-size: var(--font-size-sm); color: var(--muted); }
.help.error { color: var(--danger); }
</style>
