<script setup lang="ts">
const route = useRoute()
const { t } = useI18n()
const { expanded, wide } = useNavExpanded()

const title = computed(() => {
  const titleKey = route.meta.titleKey as string | undefined
  return titleKey ? t(titleKey) : t('app.name')
})

const pushed = computed(() => expanded.value && wide.value)

const notifications = useNotifications()
onMounted(() => notifications.start())

useHead({ title: computed(() => `${title.value} · ${t('app.name')}`) })

function skipToContent(event: Event) {
  event.preventDefault()
  document.querySelector<HTMLElement>('main h1')?.focus()
}
</script>

<template>
  <div
    class="shell"
    :class="{ pushed }"
  >
    <a
      class="skip"
      href="#main"
      @click="skipToContent"
    >{{ $t('nav.skip') }}</a>
    <AppShellNav />
    <header
      class="top"
      :class="{ bare: !$slots.actions }"
    >
      <span class="brand">
        <span
          class="logo"
          aria-hidden="true"
        >P</span>
        <span>{{ $t('app.name') }}</span>
      </span>
      <NotificationBell class="mobile-bell" />
      <slot name="actions" />
    </header>
    <main
      id="main"
      class="content"
    >
      <h1 tabindex="-1">
        {{ title }}
      </h1>
      <slot />
    </main>
  </div>
</template>

<style scoped>
.shell { min-height: 100dvh; }
.skip {
  position: fixed;
  top: var(--space-2);
  left: var(--space-2);
  z-index: 100;
  padding: var(--space-3) var(--space-4);
  border-radius: var(--radius-md);
  background: var(--accent);
  color: var(--accent-contrast);
  font-weight: 700;
  text-decoration: none;
  transform: translateY(-200%);
}
.skip:focus-visible { transform: none; }
.top { display: flex; align-items: center; justify-content: space-between; max-width: 560px; margin: 0 auto; padding: var(--space-4); }
.top { gap: var(--space-3); }
.mobile-bell { margin-left: auto; }
.brand { display: inline-flex; align-items: center; gap: var(--space-2); font-weight: 700; }
.logo { display: grid; place-items: center; width: 28px; height: 28px; border-radius: 8px; background: var(--accent); color: var(--accent-contrast); font-weight: 800; }
.content { display: grid; gap: var(--space-4); max-width: 560px; margin: 0 auto; padding: 0 var(--space-4) calc(88px + env(safe-area-inset-bottom)); }
h1 { margin: 0; font-size: var(--font-size-xl); }
h1:focus { outline: none; }

@media (min-width: 768px) {
  .shell { padding-left: var(--nav-rail-width); transition: padding-left 0.2s ease-out; }
  .shell.pushed { padding-left: var(--nav-wide-width); }
  .brand { display: none; }
  .mobile-bell { display: none; }
  .top { max-width: var(--content-max); padding: var(--space-5) var(--space-5) 0; }
  .top.bare { display: none; }
  .content {
    max-width: var(--content-max);
    gap: var(--space-5);
    padding: var(--space-5) var(--space-5) var(--space-6);
  }
  .top:not(.bare) + .content { padding-top: var(--space-4); }
}

@media (min-width: 1200px) {
  .content { padding-inline: var(--space-6); }
}
</style>
