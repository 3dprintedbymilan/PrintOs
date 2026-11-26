<script setup lang="ts">
import type { ThemePreference } from '~/utils/theme'

const { t } = useI18n()
const { preference, setPreference } = useTheme()

const options = computed(() => [
  { value: 'system', label: t('theme.system') },
  { value: 'light', label: t('theme.light') },
  { value: 'dark', label: t('theme.dark') },
])

const model = computed({
  get: () => preference.value,
  set: (value: string) => setPreference(value as ThemePreference),
})
</script>

<template>
  <UiSegmentedControl
    v-model="model"
    :label="t('theme.label')"
    :options="options"
  />
</template>
