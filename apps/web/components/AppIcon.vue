<script setup lang="ts">
import type { NavIconName } from '~/utils/navigation'

defineProps<{ name: NavIconName | 'menu' | 'chevron-left' | 'chevron-right' }>()
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
    <template v-else-if="name === 'business'">
      <path d="M5 20v-6" />
      <path d="M11 20V8" />
      <path d="M17 20V4" />
    </template>
    <template v-else-if="name === 'printers'">
      <rect
        x="4"
        y="4"
        width="16"
        height="16"
        rx="3"
      />
      <path d="M8 8h8" />
      <path d="M8 14h8v3H8z" />
    </template>
    <template v-else-if="name === 'bambu'">
      <path d="M12 4v4" />
      <path d="M5 12a7 7 0 0114 0" />
      <path d="M8.5 15.5a4.5 4.5 0 017 0" />
      <circle
        cx="12"
        cy="19"
        r="1"
      />
    </template>
    <template v-else-if="name === 'models'">
      <path d="M12 3l8 4.5v9L12 21l-8-4.5v-9L12 3z" />
      <path d="M8 9.5l4 2.5 4-2.5" />
    </template>
    <template v-else-if="name === 'licenses'">
      <path d="M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6l7-3z" />
      <path d="M9 12l2 2 4-4" />
    </template>
    <template v-else-if="name === 'products'">
      <path d="M4 12V4h8l8 8-8 8-8-8z" />
      <circle
        cx="8.5"
        cy="8.5"
        r="1"
      />
    </template>
    <template v-else-if="name === 'calculator'">
      <rect
        x="5"
        y="3"
        width="14"
        height="18"
        rx="2"
      />
      <path d="M8 7h8" />
      <path d="M8 12h1M12 12h1M16 12h0M8 16h1M12 16h1M16 16h0" />
    </template>
    <template v-else-if="name === 'settings'">
      <path d="M4 7h10M18 7h2M4 17h2M10 17h10" />
      <circle
        cx="16"
        cy="7"
        r="2"
      />
      <circle
        cx="8"
        cy="17"
        r="2"
      />
    </template>
    <template v-else-if="name === 'audit'">
      <circle
        cx="12"
        cy="12"
        r="8"
      />
      <path d="M12 7v5l3 2" />
    </template>
    <template v-else-if="name === 'orders'">
      <path d="M7 4h10a1 1 0 011 1v15l-3-2-3 2-3-2-3 2V5a1 1 0 011-1z" />
      <path d="M9 9h6M9 13h6" />
    </template>
    <template v-else-if="name === 'customers'">
      <circle
        cx="12"
        cy="8"
        r="3.5"
      />
      <path d="M5 20c0-3.5 3-6 7-6s7 2.5 7 6" />
    </template>
    <template v-else-if="name === 'chevron-left'">
      <path d="M14 6l-6 6 6 6" />
    </template>
    <template v-else-if="name === 'chevron-right'">
      <path d="M10 6l6 6-6 6" />
    </template>
    <template v-else>
      <path d="M4 7h16M4 12h16M4 17h16" />
    </template>
  </svg>
</template>
