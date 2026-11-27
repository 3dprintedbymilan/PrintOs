<script setup lang="ts">
import { unreadBadge } from '~/utils/notifications'

const { unread } = useNotifications()
const badge = computed(() => unreadBadge(unread.value))
</script>

<template>
  <NuxtLink
    to="/notifications"
    class="bell"
    :aria-label="unread > 0 ? $t('notifications.bellUnread', { count: unread }, unread) : $t('notifications.bell')"
  >
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
      <path d="M6 16V11a6 6 0 0112 0v5l1.5 2h-15L6 16z" />
      <path d="M10 20a2 2 0 004 0" />
    </svg>
    <span
      v-if="badge"
      class="badge"
      aria-hidden="true"
    >{{ badge }}</span>
    <span class="label">{{ $t('notifications.title') }}</span>
  </NuxtLink>
</template>

<style scoped>
.bell { position: relative; display: inline-flex; align-items: center; justify-content: center; gap: var(--space-3); min-width: var(--tap-size); min-height: var(--tap-size); color: var(--muted); text-decoration: none; border-radius: var(--radius-md); font-weight: 600; }
.bell:focus-visible { outline: none; box-shadow: var(--focus-ring); }
.label { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
.badge { position: absolute; top: 2px; right: 2px; min-width: 18px; height: 18px; padding: 0 5px; border-radius: var(--radius-pill); background: var(--danger); color: #fff; font-size: 0.6875rem; font-weight: 700; line-height: 18px; text-align: center; }
.bell.in-rail { justify-content: center; padding: var(--space-2) var(--space-3); }
</style>
