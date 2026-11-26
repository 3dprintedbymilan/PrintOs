<script setup lang="ts">
import { finishedStatuses, fromLocalInput, isFinished, mapServerErrors, jobToFormValues, validateJobForm } from '~/utils/printJobs'
import type { JobFormValues, PrintJob, PrintJobStatus } from '~/utils/printJobs'
import type { Printer } from '~/utils/printerForm'
import type { Spool } from '~/utils/inventory'

const props = defineProps<{ mode: 'create' | 'edit' | 'finish', job?: PrintJob }>()
const emit = defineEmits<{ saved: [job: PrintJob], cancel: [] }>()

const { t, n } = useI18n()
const auth = useAuthStore()

function nowLocal() {
  const date = new Date()
  date.setSeconds(0, 0)
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

const printers = ref<Printer[]>([])
const spools = ref<Spool[]>([])
const loadStatus = ref<'loading' | 'ready' | 'failed'>('loading')
const saving = ref(false)
const saveError = ref('')
const errors = ref<Record<string, string>>({})
let lineKey = 100

const form = reactive<JobFormValues>(props.job
  ? jobToFormValues(props.job)
  : { name: '', printerId: '', status: 'Printing', startedAt: nowLocal(), finishedAt: '', finishedUnits: '', notes: '', lines: [] })

if (props.mode === 'finish') {
  form.status = 'Done'
  form.finishedAt = nowLocal()
}

const statusOptions = computed(() => {
  const list: PrintJobStatus[] = props.mode === 'finish'
    ? finishedStatuses
    : props.mode === 'create' ? ['Planned', 'Printing', 'Done', 'Failed'] : ['Planned', 'Printing', ...finishedStatuses]
  return list.map(value => ({ value, label: t(`printJobs.status.${value}`) }))
})
const printerOptions = computed(() => printers.value.map(printer => ({ value: printer.id, label: printer.name })))
const finished = computed(() => isFinished(form.status))
const showDetails = computed(() => props.mode !== 'finish')
const title = computed(() => t(`printJobs.form.${props.mode}`))

function spoolOptions(selected: string) {
  return [
    { value: '', label: t('printJobs.chooseSpool') },
    ...spools.value
      .filter(spool => !spool.isArchived || spool.id === selected)
      .map(spool => ({ value: spool.id, label: `${spool.materialName} · ${spool.colorName} (${n(spool.remainingWeightGrams)} g)` })),
  ]
}

async function load() {
  loadStatus.value = 'loading'
  try {
    const [loadedPrinters, loadedSpools] = await Promise.all([
      auth.authorizedFetch<Printer[]>('/api/printers'),
      auth.authorizedFetch<Spool[]>('/api/spools', { query: { includeArchived: true } }),
    ])
    printers.value = loadedPrinters
    spools.value = loadedSpools
    if (!form.printerId) form.printerId = loadedPrinters[0]?.id ?? ''
    loadStatus.value = 'ready'
  }
  catch {
    loadStatus.value = 'failed'
  }
}

function addLine() {
  form.lines.push({ key: lineKey++, spoolId: '', grams: '', wasteGrams: '' })
}

function removeLine(index: number) {
  form.lines.splice(index, 1)
}

watch(() => form.status, (status) => {
  if (isFinished(status) && !form.finishedAt) form.finishedAt = nowLocal()
  if (status === 'Printing' && !form.startedAt) form.startedAt = nowLocal()
})

function fieldLabel(field: string) {
  return t(`printJobs.fields.${field}`)
}

async function save() {
  saveError.value = ''
  const external = { externalSource: props.job?.externalSource ?? null, externalId: props.job?.externalId ?? null }
  const { body, errors: found } = validateJobForm(form, external)
  errors.value = Object.fromEntries(Object.entries(found).map(([key, kind]) => {
    const field = key.startsWith('lines.') ? ({ spoolId: 'spool', grams: 'grams', wasteGrams: 'waste' } as Record<string, string>)[key.split('.')[2] ?? ''] ?? 'grams' : key
    return [key, t(`printJobs.errors.${kind}`, { field: fieldLabel(field) })]
  }))
  if (!body) {
    await nextTick()
    document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    return
  }

  saving.value = true
  try {
    let saved: PrintJob
    if (props.mode === 'create') {
      saved = await auth.authorizedFetch<PrintJob>('/api/print-jobs', { method: 'POST', body })
    }
    else if (props.mode === 'edit') {
      saved = await auth.authorizedFetch<PrintJob>(`/api/print-jobs/${props.job!.id}`, { method: 'PUT', body })
    }
    else {
      saved = await auth.authorizedFetch<PrintJob>(`/api/print-jobs/${props.job!.id}/finish`, {
        method: 'POST',
        body: {
          status: body.status,
          finishedAt: body.finishedAt ?? fromLocalInput(nowLocal()),
          durationMinutes: null,
          notes: body.notes,
          finishedUnits: body.finishedUnits,
          filaments: body.filaments,
        },
      })
    }
    emit('saved', saved)
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation') {
      const { fields, general } = mapServerErrors(parsed.fieldErrors)
      errors.value = fields
      saveError.value = general[0] ?? (Object.keys(fields).length ? '' : t('printJobs.errors.save'))
      await nextTick()
      document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    }
    else {
      saveError.value = parsed.kind === 'network' ? t('printJobs.errors.network') : t('printJobs.errors.save')
    }
  }
  finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div>
    <StateLoading v-if="loadStatus === 'loading'" />
    <StateError
      v-else-if="loadStatus === 'failed'"
      @retry="load"
    />
    <StateEmpty
      v-else-if="printers.length === 0"
      :message="$t('printJobs.noPrinters')"
    />

    <form
      v-else
      class="form"
      novalidate
      :aria-label="title"
      @submit.prevent="save"
    >
      <UiCard class="group">
        <h2 class="heading">
          {{ title }}
        </h2>
        <template v-if="showDetails">
          <UiInput
            v-model="form.name"
            :label="$t('printJobs.fields.name')"
            :hint="$t('printJobs.hints.name')"
            :error="errors.name"
          />
          <UiSelect
            v-model="form.printerId"
            :label="$t('printJobs.fields.printer')"
            :options="printerOptions"
            :error="errors.printerId"
          />
        </template>
        <UiSelect
          v-model="form.status"
          :label="mode === 'finish' ? $t('printJobs.fields.outcome') : $t('printJobs.fields.status')"
          :options="statusOptions"
          :error="errors.status"
        />
        <UiInput
          v-if="showDetails"
          v-model="form.startedAt"
          type="datetime-local"
          :label="$t('printJobs.fields.startedAt')"
          :error="errors.startedAt"
        />
        <UiInput
          v-if="finished"
          v-model="form.finishedAt"
          type="datetime-local"
          :label="$t('printJobs.fields.finishedAt')"
          :error="errors.finishedAt"
        />
      </UiCard>

      <UiCard class="group">
        <h2 class="heading">
          {{ $t('printJobs.filament') }}
        </h2>
        <p class="hint">
          {{ finished ? $t('printJobs.hints.consumes') : $t('printJobs.hints.planned') }}
        </p>
        <fieldset
          v-for="(line, index) in form.lines"
          :key="line.key"
          class="line"
        >
          <legend>{{ $t('printJobs.spoolNumber', { number: index + 1 }) }}</legend>
          <UiSelect
            v-model="line.spoolId"
            :label="$t('printJobs.fields.spool')"
            :options="spoolOptions(line.spoolId)"
            :error="errors[`lines.${index}.spoolId`]"
          />
          <div class="pair">
            <UiInput
              v-model="line.grams"
              :label="$t('printJobs.fields.grams')"
              inputmode="numeric"
              :error="errors[`lines.${index}.grams`]"
            />
            <UiInput
              v-model="line.wasteGrams"
              :label="$t('printJobs.fields.waste')"
              inputmode="numeric"
              :error="errors[`lines.${index}.wasteGrams`]"
            />
          </div>
          <UiButton
            variant="ghost"
            @click="removeLine(index)"
          >
            {{ $t('printJobs.removeSpool', { number: index + 1 }) }}
          </UiButton>
        </fieldset>
        <UiButton
          variant="secondary"
          block
          @click="addLine"
        >
          {{ $t('printJobs.addSpool') }}
        </UiButton>
      </UiCard>

      <UiCard class="group">
        <UiInput
          v-if="form.status === 'Done'"
          v-model="form.finishedUnits"
          :label="$t('printJobs.fields.finishedUnits')"
          :hint="$t('printJobs.hints.finishedUnits')"
          inputmode="numeric"
          :error="errors.finishedUnits"
        />
        <div class="field">
          <label :for="`${mode}-notes`">{{ $t('printJobs.fields.notes') }}</label>
          <textarea
            :id="`${mode}-notes`"
            v-model="form.notes"
            rows="3"
            :aria-invalid="errors.notes ? 'true' : undefined"
          />
          <p
            v-if="errors.notes"
            class="error"
          >
            {{ errors.notes }}
          </p>
        </div>
      </UiCard>

      <p
        v-if="saveError"
        class="error"
        role="alert"
      >
        {{ saveError }}
      </p>

      <div class="actions">
        <UiButton
          type="submit"
          block
          :loading="saving"
        >
          {{ $t(`printJobs.save.${mode}`) }}
        </UiButton>
        <UiButton
          variant="ghost"
          block
          @click="emit('cancel')"
        >
          {{ $t('printJobs.cancel') }}
        </UiButton>
      </div>
    </form>
  </div>
</template>

<style scoped>
.form { display: grid; gap: var(--space-3); }
.group { display: grid; gap: var(--space-3); }
.heading { margin: 0; font-size: var(--font-size-md); }
.hint { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.line { display: grid; gap: var(--space-3); margin: 0; padding: var(--space-3); border: 1px solid var(--border); border-radius: var(--radius-md); background: var(--surface); }
.line legend { padding: 0 var(--space-2); color: var(--muted); font-size: var(--font-size-sm); font-weight: 600; }
.pair { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-3); }
.field { display: grid; gap: var(--space-1); }
.field label { font-size: var(--font-size-sm); color: var(--muted); }
textarea { width: 100%; padding: var(--space-3) var(--space-4); background: var(--surface-2); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius-md); font: inherit; resize: vertical; }
textarea[aria-invalid='true'] { border-color: var(--danger); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
.actions { display: grid; gap: var(--space-2); }
</style>
