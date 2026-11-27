<script setup lang="ts">
import { isPrimaryActive, primaryDestinations } from '~/utils/navigation'

defineProps<{ expanded?: boolean }>()

const { t } = useI18n()
const route = useRoute()
</script>

<template>
  <nav
    class="nav"
    :class="{ expanded }"
    :aria-label="t('nav.label')"
  >
    <NuxtLink
      v-for="tab in primaryDestinations"
      :key="tab.to"
      :to="tab.to"
      class="tab"
      :class="{ active: isPrimaryActive(tab, route.path) }"
      :aria-current="isPrimaryActive(tab, route.path) ? 'page' : undefined"
    >
      <AppIcon :name="tab.icon" />
      <span>{{ t(tab.labelKey) }}</span>
    </NuxtLink>
  </nav>
</template>

<style scoped>
.nav {
  position: fixed;
  inset: auto 0 0 0;
  z-index: 10;
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  padding: var(--space-2) var(--space-2) calc(var(--space-2) + env(safe-area-inset-bottom));
  background: color-mix(in srgb, var(--bg-elevated) 92%, transparent);
  backdrop-filter: blur(14px);
  border-top: 1px solid var(--border);
}
.tab {
  display: grid;
  justify-items: center;
  gap: 2px;
  min-height: var(--tap-size);
  padding: var(--space-1) 0;
  color: var(--muted);
  font-size: 0.6875rem;
  font-weight: 600;
  text-decoration: none;
  border-radius: var(--radius-md);
}
.tab.active { color: var(--accent-text); }

@media (min-width: 768px) {
  .nav {
    position: static;
    display: grid;
    grid-template-columns: 1fr;
    gap: var(--space-1);
    padding: 0;
    background: none;
    backdrop-filter: none;
    border-top: 0;
  }
  .tab { min-height: 56px; padding: var(--space-2) var(--space-1); }
  .tab.active { background: var(--accent-soft); }
  @media (hover: hover) {
    .tab:hover { background: var(--surface-2); color: var(--text); }
    .tab.active:hover { color: var(--accent-text); }
  }
  .nav.expanded .tab {
    grid-auto-flow: column;
    grid-template-columns: 24px 1fr;
    justify-items: start;
    align-items: center;
    gap: var(--space-3);
    min-height: var(--tap-size);
    padding: var(--space-2) var(--space-3);
    font-size: var(--font-size-md);
  }
}
</style>
