<script setup lang="ts">
definePageMeta({ titleKey: 'business.title' })
const { t } = useI18n()

const sections = computed(() => [
  { key: 'orders', title: t('business.orders') },
  { key: 'calculator', title: t('business.calculator') },
  { key: 'margins', title: t('business.margins') },
  { key: 'licenses', title: t('business.licenses') },
  { key: 'marketStock', title: t('business.marketStock') },
])
</script>

<template>
  <div class="stack">
    <UiListRow
      v-for="section in sections"
      :key="section.key"
      :title="section.title"
    />
    <UiCard class="settings">
      <p>{{ $t('theme.label') }}</p>
      <UiThemeSwitch />
    </UiCard>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.settings p { margin: 0 0 var(--space-2); color: var(--muted); font-size: var(--font-size-sm); }
</style>
