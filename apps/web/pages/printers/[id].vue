<script setup lang="ts">
import { depreciationPerHour, emptyPrinterForm, printerToForm, validatePrinterForm } from '~/utils/printerForm'
import type { Printer, PrinterErrorKind, PrinterField, PrinterFormValues, PrinterRequest } from '~/utils/printerForm'
import { parseDecimal } from '~/utils/settingsForm'

definePageMeta({ titleKey: 'printers.formTitle' })

const { t, n } = useI18n()
const route = useRoute()
const auth = useAuthStore()

const id = String(route.params.id)
const isNew = id === 'new'

const form = reactive<PrinterFormValues>(emptyPrinterForm())
const fieldErrors = ref<Partial<Record<PrinterField, string>>>({})
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const saving = ref(false)
const saveError = ref('')
const archived = ref(false)

const rate = computed(() => {
  const cost = parseDecimal(form.cost)
  const lifetime = parseDecimal(form.lifetime)
  if (cost === null || lifetime === null) return null
  return depreciationPerHour(cost, lifetime)
})

async function load() {
  status.value = 'loading'
  try {
    const printer = isNew
      ? await auth.authorizedFetch<PrinterRequest>('/api/printers/defaults')
      : await auth.authorizedFetch<Printer>(`/api/printers/${id}`)
    Object.assign(form, printerToForm(printer))
    if (isNew) form.name = ''
    archived.value = !isNew && (printer as Printer).isArchived
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function errorText(field: PrinterField, kind: PrinterErrorKind) {
  return t(`printers.errors.${kind}`, { field: t(`printers.fields.${field}`) })
}

async function save() {
  saveError.value = ''
  const { printer, errors } = validatePrinterForm(form)
  fieldErrors.value = Object.fromEntries(Object.entries(errors).map(([field, kind]) => [field, errorText(field as PrinterField, kind)]))
  if (!printer) {
    nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
    return
  }

  saving.value = true
  try {
    await auth.authorizedFetch(isNew ? '/api/printers' : `/api/printers/${id}`, { method: isNew ? 'POST' : 'PUT', body: printer })
    await navigateTo('/printers')
  }
  catch {
    saveError.value = t('printers.errors.save')
  }
  finally {
    saving.value = false
  }
}

async function setArchived(value: boolean) {
  saveError.value = ''
  try {
    await auth.authorizedFetch(`/api/printers/${id}/${value ? 'archive' : 'restore'}`, { method: 'POST' })
    await navigateTo('/printers')
  }
  catch {
    saveError.value = t('printers.errors.save')
  }
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/printers"
      class="back"
    >
      {{ $t('printers.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />

    <form
      v-else
      class="form"
      novalidate
      @submit.prevent="save"
    >
      <h2 class="heading">
        {{ isNew ? $t('printers.addTitle') : $t('printers.editTitle') }}
      </h2>

      <UiCard class="group">
        <UiInput
          v-model="form.name"
          :label="$t('printers.fields.name')"
          :hint="$t('printers.hints.name')"
          :error="fieldErrors.name"
        />
        <UiInput
          v-model="form.model"
          :label="$t('printers.fields.model')"
          :error="fieldErrors.model"
        />
        <div class="volume">
          <UiInput
            v-model="form.volumeX"
            :label="$t('printers.fields.volumeX')"
            :error="fieldErrors.volumeX"
            inputmode="numeric"
          />
          <UiInput
            v-model="form.volumeY"
            :label="$t('printers.fields.volumeY')"
            :error="fieldErrors.volumeY"
            inputmode="numeric"
          />
          <UiInput
            v-model="form.volumeZ"
            :label="$t('printers.fields.volumeZ')"
            :error="fieldErrors.volumeZ"
            inputmode="numeric"
          />
        </div>
        <UiInput
          v-model="form.nozzle"
          :label="$t('printers.fields.nozzle')"
          :error="fieldErrors.nozzle"
          inputmode="decimal"
        />
      </UiCard>

      <UiCard class="group">
        <UiInput
          v-model="form.power"
          :label="$t('printers.fields.power')"
          :hint="$t('printers.hints.power')"
          :error="fieldErrors.power"
          inputmode="numeric"
        />
        <UiInput
          v-model="form.cost"
          :label="$t('printers.fields.cost')"
          :error="fieldErrors.cost"
          inputmode="decimal"
        />
        <UiInput
          v-model="form.lifetime"
          :label="$t('printers.fields.lifetime')"
          :hint="$t('printers.hints.lifetime')"
          :error="fieldErrors.lifetime"
          inputmode="numeric"
        />
        <p
          class="rate"
          role="status"
        >
          <template v-if="rate !== null">
            {{ $t('printers.depreciation', { amount: n(rate, { minimumFractionDigits: 2, maximumFractionDigits: 4 }) }) }}
          </template>
        </p>
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
        {{ $t('printers.save') }}
      </UiButton>
      <UiButton
        v-if="!isNew"
        variant="secondary"
        block
        @click="setArchived(!archived)"
      >
        {{ archived ? $t('printers.restore') : $t('printers.archive') }}
      </UiButton>
    </form>

    <PrinterMaintenanceCard
      v-if="!isNew && status === 'ready'"
      :printer-id="id"
    />
  </div>
</template>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.heading { margin: 0; font-size: var(--font-size-lg); }
.group { display: grid; gap: var(--space-3); }
.volume { display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--space-2); }
.rate { margin: 0; min-height: 1.25em; color: var(--accent); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
</style>
