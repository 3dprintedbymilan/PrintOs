<script setup lang="ts">
import { activeSecondary, secondaryDestinations } from '~/utils/navigation'

const { t } = useI18n()
const route = useRoute()
const { expanded, wide, set } = useNavExpanded()

const overlay = computed(() => expanded.value && !wide.value)
const current = computed(() => activeSecondary(route.path, route.query))
const toggle = useTemplateRef<HTMLButtonElement>('toggle')

watch(() => route.fullPath, () => {
  if (overlay.value) set(false)
})

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape' && overlay.value) {
    set(false)
    toggle.value?.focus()
  }
}
</script>

<template>
  <aside
    id="app-sidebar"
    class="sidebar"
    :class="{ expanded, overlay }"
    @keydown="onKeydown"
  >
    <span class="brand">
      <span
        class="logo"
        aria-hidden="true"
      >P</span>
      <span class="brand-name">{{ $t('app.name') }}</span>
    </span>

    <AppBottomNav :expanded="expanded" />

    <div class="rail-bell">
      <NotificationBell class="in-rail" />
    </div>

    <nav
      class="more"
      :aria-label="t('nav.moreLabel')"
    >
      <p
        class="more-title"
        aria-hidden="true"
      >
        {{ t('nav.more') }}
      </p>
      <NuxtLink
        v-for="item in secondaryDestinations"
        :key="item.to"
        :to="item.to"
        class="item"
        :class="{ active: current === item.to }"
        :aria-current="current === item.to ? 'page' : undefined"
        :title="expanded ? undefined : t(item.labelKey)"
      >
        <AppIcon :name="item.icon" />
        <span class="item-label">{{ t(item.labelKey) }}</span>
      </NuxtLink>
    </nav>

    <button
      ref="toggle"
      type="button"
      class="toggle"
      aria-controls="app-sidebar"
      :aria-expanded="expanded"
      :title="expanded ? t('nav.collapse') : t('nav.expand')"
      @click="set(!expanded)"
    >
      <AppIcon :name="expanded ? 'chevron-left' : 'chevron-right'" />
      <span class="item-label">{{ expanded ? t('nav.collapse') : t('nav.expand') }}</span>
    </button>
  </aside>
</template>

<style scoped>
.sidebar { display: contents; }
.brand, .more, .toggle, .rail-bell { display: none; }

@media (min-width: 768px) {
  .sidebar {
    position: fixed;
    inset: 0 auto 0 0;
    z-index: 20;
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
    width: var(--nav-rail-width);
    padding: var(--space-4) var(--space-2) calc(var(--space-3) + env(safe-area-inset-bottom));
    overflow-y: auto;
    overscroll-behavior: contain;
    background: var(--bg-elevated);
    border-right: 1px solid var(--border);
    transition: width 0.2s ease-out, box-shadow 0.2s ease-out;
  }
  .sidebar.expanded { width: var(--nav-wide-width); padding-inline: var(--space-3); }
  .sidebar.overlay { box-shadow: var(--shadow); }

  .brand { display: flex; align-items: center; justify-content: center; gap: var(--space-2); min-height: var(--tap-size); font-weight: 700; }
  .sidebar.expanded .brand { justify-content: flex-start; padding-inline: var(--space-2); }
  .brand-name { display: none; }
  .sidebar.expanded .brand-name { display: inline; }
  .logo { display: grid; place-items: center; width: 32px; height: 32px; border-radius: 9px; background: var(--accent); color: var(--accent-contrast); font-weight: 800; }

  .rail-bell { display: flex; }
  .sidebar.expanded .rail-bell :deep(.bell) { justify-content: flex-start; width: 100%; }
  .sidebar.expanded .rail-bell :deep(.label) { position: static; width: auto; height: auto; overflow: visible; clip-path: none; }

  .more { display: grid; gap: var(--space-1); padding-top: var(--space-4); border-top: 1px solid var(--border); }
  .more-title { display: none; margin: 0 0 var(--space-1); padding-inline: var(--space-3); color: var(--muted); font-size: var(--font-size-sm); font-weight: 600; }
  .sidebar.expanded .more-title { display: block; }

  .item, .toggle {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: var(--space-3);
    min-height: var(--tap-size);
    padding: var(--space-2) var(--space-3);
    border: 0;
    border-radius: var(--radius-md);
    background: none;
    color: var(--muted);
    font: inherit;
    font-weight: 600;
    text-decoration: none;
    cursor: pointer;
  }
  .sidebar.expanded .item, .sidebar.expanded .toggle { justify-content: flex-start; }
  .item.active { background: var(--accent-soft); color: var(--accent-text); }
  @media (hover: hover) {
    .item:hover, .toggle:hover { background: var(--surface-2); color: var(--text); }
    .item.active:hover { color: var(--accent-text); }
  }

  .toggle { display: flex; margin-top: auto; }
  .item-label {
    position: absolute;
    width: 1px;
    height: 1px;
    overflow: hidden;
    clip-path: inset(50%);
    white-space: nowrap;
  }
  .sidebar.expanded .item-label {
    position: static;
    width: auto;
    height: auto;
    overflow: visible;
    clip-path: none;
  }
}
</style>
