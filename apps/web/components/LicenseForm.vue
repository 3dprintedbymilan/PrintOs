<script setup lang="ts">
import { licenseSources, licenseTypes, validateLicenseForm } from '~/utils/licenses'
import type { LicenseErrorKind, LicenseField, LicenseFormValues, LicenseRequest } from '~/utils/licenses'

const props = defineProps<{
  initial: LicenseFormValues
  saving: boolean
  saveError: string
  submitLabel: string
  serverErrors?: Partial<Record<LicenseField, string>>
}>()
const emit = defineEmits<{
  'submit': [request: LicenseRequest]
}>()

const { t } = useI18n()
const form = reactive<LicenseFormValues>({ ...props.initial })
const fieldErrors = ref<Partial<Record<LicenseField, string>>>({})
const attributionId = useId()

watch(() => props.serverErrors, (errors) => {
  if (errors && Object.keys(errors).length > 0) {
    fieldErrors.value = { ...errors }
    focusInvalid()
  }
})

const sourceOptions = computed(() => licenseSources.map(value => ({ value, label: t(`licenses.source.${value}`) })))
const typeOptions = computed(() => licenseTypes.map(value => ({ value, label: t(`licenses.type.${value}`) })))
const typeHint = computed(() => t(`licenses.typeHint.${form.type}`))

function errorText(field: LicenseField, kind: LicenseErrorKind) {
  return t(`licenses.errors.${kind}`, { field: t(`licenses.fields.${field}`) })
}

function focusInvalid() {
  nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
}

function submit() {
  const { request, errors } = validateLicenseForm(form)
  fieldErrors.value = Object.fromEntries(Object.entries(errors).map(([field, kind]) => [field, errorText(field as LicenseField, kind as LicenseErrorKind)]))
  if (!request) {
    focusInvalid()
    return
  }
  emit('submit', request)
}
</script>

<template>
  <form
    class="form"
    novalidate
    @submit.prevent="submit"
  >
    <UiCard class="group">
      <UiInput
        v-model="form.name"
        :label="$t('licenses.fields.name')"
        :hint="$t('licenses.hints.name')"
        :error="fieldErrors.name"
      />
      <UiSelect
        v-model="form.source"
        :label="$t('licenses.fields.source')"
        :options="sourceOptions"
      />
    </UiCard>

    <UiCard class="group">
      <UiSelect
        v-model="form.type"
        :label="$t('licenses.fields.type')"
        :options="typeOptions"
      />
      <p class="help">
        {{ typeHint }}
      </p>
      <UiInput
        v-if="form.type === 'CommercialWithLimits'"
        v-model="form.unitsLimit"
        :label="$t('licenses.fields.unitsLimit')"
        :hint="$t('licenses.hints.unitsLimit')"
        :error="fieldErrors.unitsLimit"
        inputmode="numeric"
      />
      <div class="dates">
        <UiInput
          v-model="form.validFrom"
          :label="$t('licenses.fields.validFrom')"
          type="date"
        />
        <UiInput
          v-model="form.validUntil"
          :label="$t('licenses.fields.validUntil')"
          type="date"
          :error="fieldErrors.validUntil"
        />
      </div>
    </UiCard>

    <UiCard class="group">
      <label class="toggle">
        <input
          v-model="form.attributionRequired"
          type="checkbox"
        >
        {{ $t('licenses.fields.attributionRequired') }}
      </label>
      <div
        v-if="form.attributionRequired"
        class="field"
      >
        <label :for="attributionId">{{ $t('licenses.fields.attributionText') }}</label>
        <textarea
          :id="attributionId"
          v-model="form.attributionText"
          rows="3"
          maxlength="500"
          :aria-invalid="fieldErrors.attributionText ? 'true' : undefined"
        />
        <p class="help">
          {{ $t('licenses.hints.attributionText') }}
        </p>
      </div>
    </UiCard>

    <UiCard class="group">
      <UiInput
        v-model="form.proofUrl"
        :label="$t('licenses.fields.proofUrl')"
        :hint="$t('licenses.hints.proofUrl')"
        type="url"
        :error="fieldErrors.proofUrl"
      />
      <UiInput
        v-model="form.notes"
        :label="$t('licenses.fields.notes')"
      />
    </UiCard>

    <p
      v-if="saveError"
      class="error"
      role="alert"
    >
      {{ saveError }}
    </p>

    <UiButton
      type="submit"
      block
      :loading="saving"
    >
      {{ submitLabel }}
    </UiButton>
  </form>
</template>

<style scoped>
.form { display: grid; gap: var(--space-3); }
.group { display: grid; gap: var(--space-3); }
.dates { display: grid; grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr)); gap: var(--space-3); }
.field { display: grid; gap: var(--space-1); }
.field label { font-size: var(--font-size-sm); color: var(--muted); }
.toggle { display: flex; align-items: center; gap: var(--space-3); min-height: var(--tap-size); }
.toggle input { width: 22px; height: 22px; accent-color: var(--accent); }
textarea { padding: var(--space-3) var(--space-4); background: var(--surface-2); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius-md); font: inherit; resize: vertical; }
textarea[aria-invalid='true'] { border-color: var(--danger); }
.help { margin: 0; font-size: var(--font-size-sm); color: var(--muted); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
</style>
