<script setup lang="ts">
definePageMeta({ titleKey: 'prints.title' })
const { t } = useI18n()
const section = ref('printing')
</script>

<template>
  <div class="stack">
    <UiSegmentedControl
      v-model="section"
      :label="t('prints.title')"
      :options="[{ value: 'printing', label: t('prints.printing') }, { value: 'models', label: t('prints.models') }]"
    />
    <PrintLog v-if="section === 'printing'" />
    <UiCard v-else>
      <StateEmpty />
    </UiCard>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-4); }
</style>
