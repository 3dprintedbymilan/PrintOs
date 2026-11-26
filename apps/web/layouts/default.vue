<script setup lang="ts">
const route = useRoute()
const { t } = useI18n()

const title = computed(() => {
  const titleKey = route.meta.titleKey as string | undefined
  return titleKey ? t(titleKey) : t('app.name')
})

useHead({ title: computed(() => `${title.value} · ${t('app.name')}`) })
</script>

<template>
  <div class="shell">
    <header class="top">
      <span class="brand">
        <span
          class="logo"
          aria-hidden="true"
        >P</span>
        <span>{{ $t('app.name') }}</span>
      </span>
      <slot name="actions" />
    </header>
    <main class="content">
      <h1 tabindex="-1">
        {{ title }}
      </h1>
      <slot />
    </main>
    <AppBottomNav />
  </div>
</template>

<style scoped>
.shell { min-height: 100dvh; }
.top { display: flex; align-items: center; justify-content: space-between; max-width: 560px; margin: 0 auto; padding: var(--space-4); }
.brand { display: inline-flex; align-items: center; gap: var(--space-2); font-weight: 700; }
.logo { display: grid; place-items: center; width: 28px; height: 28px; border-radius: 8px; background: var(--accent); color: var(--accent-contrast); font-weight: 800; }
.content { display: grid; gap: var(--space-4); max-width: 560px; margin: 0 auto; padding: 0 var(--space-4) calc(88px + env(safe-area-inset-bottom)); }
h1 { margin: 0; font-size: var(--font-size-xl); }
h1:focus { outline: none; }
</style>
