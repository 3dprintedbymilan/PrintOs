<script setup lang="ts">
const { t } = useI18n()

const tabs = computed(() => [
  { to: '/', icon: 'home', label: t('nav.home') },
  { to: '/prints', icon: 'prints', label: t('nav.prints') },
  { to: '/inventory', icon: 'inventory', label: t('nav.inventory') },
  { to: '/business', icon: 'business', label: t('nav.business') },
] as const)
</script>

<template>
  <nav
    class="nav"
    :aria-label="t('nav.label')"
  >
    <NuxtLink
      v-for="tab in tabs"
      :key="tab.to"
      :to="tab.to"
      class="tab"
      active-class="active"
      exact-active-class="active"
    >
      <AppIcon :name="tab.icon" />
      <span>{{ tab.label }}</span>
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
</style>
