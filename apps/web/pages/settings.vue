<script setup lang="ts">
import { supportedCurrencies, supportedLanguages, toFormValues, validateSettingsForm } from '~/utils/settingsForm'
import type { SettingsErrorKind, SettingsField, SettingsFormValues, WorkspaceSettings } from '~/utils/settingsForm'

definePageMeta({ titleKey: 'settings.title' })

const { t, locale, setLocale } = useI18n()
const auth = useAuthStore()

const form = reactive<SettingsFormValues>({
  name: '',
  currency: 'EUR',
  electricityPricePerKwh: '',
  labourRatePerHour: '',
  machineRatePerHour: '',
  wastePercent: '',
  language: 'nl',
})
const fieldErrors = ref<Partial<Record<SettingsField, string>>>({})
const status = ref<'loading' | 'ready' | 'loadFailed'>('loading')
const saving = ref(false)
const saveMessage = ref('')
const saveError = ref('')

const currencyOptions = supportedCurrencies.map(code => ({ value: code, label: code }))
const languageOptions = computed(() => supportedLanguages.map(code => ({ value: code, label: t(`settings.languages.${code}`) })))

function applyToForm(settings: WorkspaceSettings) {
  Object.assign(form, toFormValues(settings))
}

async function load() {
  status.value = 'loading'
  try {
    applyToForm(await auth.authorizedFetch<WorkspaceSettings>('/api/workspace/settings'))
    status.value = 'ready'
  }
  catch {
    status.value = 'loadFailed'
  }
}

function errorText(field: SettingsField, kind: SettingsErrorKind) {
  return t(`settings.errors.${kind}`, { field: t(`settings.fields.${field}`) })
}

function focusFirstInvalid() {
  nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
}

async function save() {
  saveMessage.value = ''
  saveError.value = ''
  const { settings, errors } = validateSettingsForm(form)
  fieldErrors.value = Object.fromEntries(
    Object.entries(errors).map(([field, kind]) => [field, errorText(field as SettingsField, kind)]),
  )
  if (!settings) {
    focusFirstInvalid()
    return
  }

  saving.value = true
  try {
    const saved = await auth.authorizedFetch<WorkspaceSettings>('/api/workspace/settings', { method: 'PUT', body: settings })
    applyToForm(saved)
    if (auth.workspace) auth.workspace = { ...auth.workspace, name: saved.name }
    if (saved.language !== locale.value) await setLocale(saved.language as 'nl' | 'en')
    saveMessage.value = t('settings.saved')
  }
  catch (error) {
    saveError.value = parseApiError(error).kind === 'network' ? t('settings.errors.network') : t('settings.errors.save')
  }
  finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/business"
      class="back"
    >
      {{ $t('settings.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'loadFailed'"
      @retry="load"
    />

    <form
      v-else
      class="form"
      novalidate
      @submit.prevent="save"
    >
      <UiCard class="group">
        <h2>{{ $t('settings.groups.business') }}</h2>
        <UiInput
          v-model="form.name"
          :label="$t('settings.fields.name')"
          :error="fieldErrors.name"
          autocomplete="organization"
        />
        <UiSelect
          v-model="form.currency"
          :label="$t('settings.fields.currency')"
          :options="currencyOptions"
        />
        <UiSelect
          v-model="form.language"
          :label="$t('settings.fields.language')"
          :options="languageOptions"
        />
      </UiCard>

      <UiCard class="group">
        <h2>{{ $t('settings.groups.costs') }}</h2>
        <p class="intro">
          {{ $t('settings.costsIntro') }}
        </p>
        <UiInput
          v-model="form.electricityPricePerKwh"
          :label="$t('settings.fields.electricityPricePerKwh')"
          :hint="$t('settings.hints.electricityPricePerKwh')"
          :error="fieldErrors.electricityPricePerKwh"
          inputmode="decimal"
        />
        <UiInput
          v-model="form.labourRatePerHour"
          :label="$t('settings.fields.labourRatePerHour')"
          :hint="$t('settings.hints.labourRatePerHour')"
          :error="fieldErrors.labourRatePerHour"
          inputmode="decimal"
        />
        <UiInput
          v-model="form.machineRatePerHour"
          :label="$t('settings.fields.machineRatePerHour')"
          :hint="$t('settings.hints.machineRatePerHour')"
          :error="fieldErrors.machineRatePerHour"
          inputmode="decimal"
        />
        <UiInput
          v-model="form.wastePercent"
          :label="$t('settings.fields.wastePercent')"
          :hint="$t('settings.hints.wastePercent')"
          :error="fieldErrors.wastePercent"
          inputmode="decimal"
        />
      </UiCard>

      <p
        class="feedback"
        role="status"
      >
        {{ saveMessage }}
      </p>
      <p
        v-if="saveError"
        class="feedback error"
        role="alert"
      >
        {{ saveError }}
      </p>

      <UiButton
        type="submit"
        block
        :loading="saving"
      >
        {{ $t('settings.save') }}
      </UiButton>
    </form>
  </div>
</template>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.group { display: grid; gap: var(--space-3); }
.group h2 { margin: 0; font-size: var(--font-size-md); }
.intro { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.feedback { margin: 0; min-height: 1.25em; color: var(--accent); font-size: var(--font-size-sm); }
.feedback.error { color: var(--danger); }
</style>
