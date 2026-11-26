<script setup lang="ts">
definePageMeta({ titleKey: 'prints.title' })
const { t } = useI18n()
const route = useRoute()
const section = ref(route.query.section === 'models' ? 'models' : 'printing')
</script>

<template>
  <div class="stack">
    <UiSegmentedControl
      v-model="section"
      :label="t('prints.title')"
      :options="[{ value: 'printing', label: t('prints.printing') }, { value: 'models', label: t('prints.models') }]"
    />
    <PrintLog v-if="section === 'printing'" />
    <ModelLibrary v-else />
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-4); }
</style>
