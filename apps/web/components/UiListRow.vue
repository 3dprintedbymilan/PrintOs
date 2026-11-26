<script setup lang="ts">
defineProps<{ title: string, subtitle?: string, to?: string }>()
const link = resolveComponent('NuxtLink')
</script>

<template>
  <component
    :is="to ? link : 'div'"
    :to="to"
    class="row"
  >
    <span class="icon"><slot name="icon" /></span>
    <span class="text">
      <span class="title">{{ title }}</span>
      <span
        v-if="subtitle"
        class="subtitle"
      >{{ subtitle }}</span>
    </span>
    <slot name="trailing" />
    <svg
      v-if="to"
      class="chevron"
      viewBox="0 0 24 24"
      width="20"
      height="20"
      aria-hidden="true"
    >
      <path
        d="M9 6l6 6-6 6"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
    </svg>
  </component>
</template>

<style scoped>
.row { display: flex; align-items: center; gap: var(--space-3); min-height: 64px; padding: var(--space-3) var(--space-4); color: var(--text); text-decoration: none; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
.icon:empty { display: none; }
.text { display: grid; flex: 1; }
.title { font-weight: 600; }
.subtitle { font-size: var(--font-size-sm); color: var(--muted); }
.chevron { color: var(--muted); }
</style>
