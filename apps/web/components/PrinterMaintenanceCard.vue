<script setup lang="ts">
import { parseApiError } from '~/utils/apiError'
import { parseDecimal } from '~/utils/settingsForm'

const props = defineProps<{ printerId: string }>()

interface Maintenance {
  everyHours: number | null
  everyDays: number | null
  lastMaintenanceAt: string | null
  since: string
  printMinutesSince: number
  daysSince: number
  due: boolean
}

const { t, d } = useI18n()
const auth = useAuthStore()

const maintenance = ref<Maintenance | null>(null)
const hours = ref('')
const days = ref('')
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const saving = ref(false)
const message = ref('')
const failure = ref('')
const errors = ref<{ hours?: string, days?: string }>({})

function apply(result: Maintenance) {
  maintenance.value = result
  hours.value = result.everyHours === null ? '' : String(result.everyHours)
  days.value = result.everyDays === null ? '' : String(result.everyDays)
}

async function load() {
  status.value = 'loading'
  try {
    apply(await auth.authorizedFetch<Maintenance>(`/api/printers/${props.printerId}/maintenance`))
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function parseInterval(value: string, max: number): number | null | undefined {
  if (!value.trim()) return null
  const parsed = parseDecimal(value)
  return parsed !== null && Number.isInteger(parsed) && parsed >= 1 && parsed <= max ? parsed : undefined
}

async function save() {
  message.value = ''
  failure.value = ''
  const everyHours = parseInterval(hours.value, 100_000)
  const everyDays = parseInterval(days.value, 3650)
  errors.value = {}
  if (everyHours === undefined) errors.value.hours = t('maintenance.errors.hours')
  if (everyDays === undefined) errors.value.days = t('maintenance.errors.days')
  if (errors.value.hours || errors.value.days) {
    nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
    return
  }
  saving.value = true
  try {
    apply(await auth.authorizedFetch<Maintenance>(`/api/printers/${props.printerId}/maintenance`, { method: 'PUT', body: { everyHours, everyDays } }))
    message.value = t('maintenance.saved')
  }
  catch (error) {
    failure.value = parseApiError(error).kind === 'validation' ? t('maintenance.errors.rejected') : t('maintenance.errors.save')
  }
  finally {
    saving.value = false
  }
}

async function markDone() {
  message.value = ''
  failure.value = ''
  saving.value = true
  try {
    apply(await auth.authorizedFetch<Maintenance>(`/api/printers/${props.printerId}/maintenance/done`, { method: 'POST' }))
    message.value = t('maintenance.doneSaved')
  }
  catch {
    failure.value = t('maintenance.errors.save')
  }
  finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <UiCard class="card">
    <h2>{{ $t('maintenance.title') }}</h2>
    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <form
      v-else-if="maintenance"
      class="form"
      novalidate
      @submit.prevent="save"
    >
      <p class="hint">
        {{ $t('maintenance.hint') }}
      </p>
      <UiInput
        v-model="hours"
        :label="$t('maintenance.everyHours')"
        inputmode="numeric"
        :error="errors.hours"
      />
      <UiInput
        v-model="days"
        :label="$t('maintenance.everyDays')"
        inputmode="numeric"
        :error="errors.days"
      />
      <p class="facts">
        {{ $t('maintenance.since', { date: d(new Date(maintenance.since), { dateStyle: 'medium' }), hours: Math.floor(maintenance.printMinutesSince / 60) }) }}
      </p>
      <UiStatusChip
        v-if="maintenance.due"
        status="warning"
      >
        {{ $t('maintenance.due') }}
      </UiStatusChip>
      <p
        class="feedback"
        role="status"
      >
        {{ message }}
      </p>
      <p
        v-if="failure"
        class="feedback error"
        role="alert"
      >
        {{ failure }}
      </p>
      <div class="actions">
        <UiButton
          type="submit"
          :loading="saving"
        >
          {{ $t('maintenance.save') }}
        </UiButton>
        <UiButton
          variant="secondary"
          :disabled="saving"
          @click="markDone"
        >
          {{ $t('maintenance.markDone') }}
        </UiButton>
      </div>
    </form>
  </UiCard>
</template>

<style scoped>
.card, .form { display: grid; gap: var(--space-3); }
h2 { margin: 0; font-size: var(--font-size-md); }
.hint, .facts { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.actions { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.feedback { margin: 0; min-height: 1.25em; color: var(--accent); font-size: var(--font-size-sm); }
.feedback.error { color: var(--danger); }
.card :deep(.chip) { justify-self: start; }
</style>
