<script setup lang="ts">
definePageMeta({ titleKey: 'prints.title' })
const { t } = useI18n()
const route = useRoute()

function sectionOf(query: unknown) {
  return query === 'models' || query === 'queue' ? query : 'printing'
}

const section = ref<string>(sectionOf(route.query.section))
watch(() => route.query.section, (value) => { section.value = sectionOf(value) })
</script>

<template>
  <div class="stack">
    <UiSegmentedControl
      v-model="section"
      :label="t('prints.title')"
      :options="[
        { value: 'printing', label: t('prints.printing') },
        { value: 'queue', label: t('queue.title') },
        { value: 'models', label: t('prints.models') },
      ]"
    />
    <UiListRow
      v-if="section === 'printing'"
      to="/printers/monitor"
      :title="t('monitor.open')"
      :subtitle="t('monitor.openHint')"
    />
    <PrintLog v-if="section === 'printing'" />
    <ProductionQueue v-else-if="section === 'queue'" />
    <ModelLibrary v-else />
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-4); }
</style>
