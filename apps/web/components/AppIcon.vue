<script setup lang="ts">
defineProps<{ name: 'home' | 'prints' | 'inventory' | 'business' }>()
</script>

<template>
  <svg
    viewBox="0 0 24 24"
    width="24"
    height="24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.8"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <template v-if="name === 'home'">
      <path d="M4 11l8-7 8 7" />
      <path d="M6 10v10h12V10" />
      <path d="M10 20v-6h4v6" />
    </template>
    <template v-else-if="name === 'prints'">
      <path d="M7 9V4h10v5" />
      <rect
        x="4"
        y="9"
        width="16"
        height="8"
        rx="2"
      />
      <path d="M7 14h10v6H7z" />
    </template>
    <template v-else-if="name === 'inventory'">
      <path d="M12 3l8 4.5v9L12 21l-8-4.5v-9L12 3z" />
      <path d="M4 7.5L12 12l8-4.5" />
      <path d="M12 12v9" />
    </template>
    <template v-else>
      <path d="M5 20v-6" />
      <path d="M11 20V8" />
      <path d="M17 20V4" />
    </template>
  </svg>
</template>
