<script setup lang="ts">
import type { ProfileFormValues } from '~/utils/modelLibrary'

defineProps<{ errors: { layerHeight?: string, infill?: string } }>()
const values = defineModel<ProfileFormValues>({ required: true })
const notesId = useId()
const { t } = useI18n()

const supportOptions = computed(() => [
  { value: '', label: t('models.profile.supportsUnknown') },
  { value: 'yes', label: t('models.profile.supportsYes') },
  { value: 'no', label: t('models.profile.supportsNo') },
])
</script>

<template>
  <div class="fields">
    <UiInput
      v-model="values.layerHeight"
      :label="$t('models.profile.layerHeight')"
      :hint="$t('models.profile.layerHeightHint')"
      :error="errors.layerHeight"
      inputmode="decimal"
    />
    <UiInput
      v-model="values.infill"
      :label="$t('models.profile.infill')"
      :error="errors.infill"
      inputmode="numeric"
    />
    <UiSelect
      v-model="values.supports"
      :label="$t('models.profile.supports')"
      :options="supportOptions"
    />
    <UiInput
      v-model="values.materialType"
      :label="$t('models.profile.material')"
      :hint="$t('models.profile.materialHint')"
    />
    <div class="area">
      <label :for="notesId">{{ $t('models.profile.notes') }}</label>
      <textarea
        :id="notesId"
        v-model="values.profileNotes"
        rows="3"
        maxlength="1000"
      />
    </div>
  </div>
</template>

<style scoped>
.fields { display: grid; gap: var(--space-3); }
.area { display: grid; gap: var(--space-1); }
label { font-size: var(--font-size-sm); color: var(--muted); }
textarea { width: 100%; padding: var(--space-3) var(--space-4); background: var(--surface-2); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius-md); font: inherit; resize: vertical; }
</style>
