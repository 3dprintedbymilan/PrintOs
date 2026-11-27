<script setup lang="ts">
import { parseApiError } from '~/utils/apiError'
import { parseUnitsPerJob, splitCount } from '~/utils/productionQueue'
import type { Printer } from '~/utils/printerForm'

const props = defineProps<{ orderId: string, lineId?: string, remaining: number, title: string }>()
const open = defineModel<boolean>({ required: true })
const emit = defineEmits<{ created: [count: number] }>()

const { t } = useI18n()
const auth = useAuthStore()

const printers = ref<Printer[]>([])
const printerId = ref('')
const unitsPerJob = ref('')
const error = ref('')
const unitsError = ref('')
const saving = ref(false)
const loaded = ref(false)

const printerOptions = computed(() => printers.value.map(printer => ({ value: printer.id, label: printer.name })))
const jobCount = computed(() => {
  const parsed = parseUnitsPerJob(unitsPerJob.value)
  return splitCount(props.remaining, parsed.valid ? parsed.value : null)
})

watch(open, async (isOpen) => {
  if (!isOpen) return
  error.value = ''
  unitsError.value = ''
  unitsPerJob.value = ''
  if (loaded.value) return
  try {
    printers.value = (await auth.authorizedFetch<Printer[]>('/api/printers')).filter(printer => !printer.isArchived)
    printerId.value = printers.value[0]?.id ?? ''
    loaded.value = true
  }
  catch {
    error.value = t('queue.errors.load')
  }
})

async function create() {
  const parsed = parseUnitsPerJob(unitsPerJob.value)
  unitsError.value = parsed.valid ? '' : t('queue.errors.unitsPerJob')
  if (!parsed.valid) {
    nextTick(() => document.querySelector<HTMLElement>('dialog [aria-invalid="true"]')?.focus())
    return
  }
  if (!printerId.value) {
    error.value = t('queue.errors.noPrinter')
    return
  }
  saving.value = true
  error.value = ''
  try {
    const created = await auth.authorizedFetch<unknown[]>(`/api/production/orders/${props.orderId}/jobs`, {
      method: 'POST',
      body: { lineId: props.lineId ?? null, printerId: printerId.value, unitsPerJob: parsed.value },
    })
    open.value = false
    emit('created', created.length)
  }
  catch (failure) {
    const problem = parseApiError(failure)
    error.value = problem.kind === 'validation' && problem.fieldErrors.printerId ? t('queue.errors.noPrinter') : t('queue.errors.create')
  }
  finally {
    saving.value = false
  }
}
</script>

<template>
  <UiDialog
    v-model="open"
    :title="title"
  >
    <form
      class="form"
      novalidate
      @submit.prevent="create"
    >
      <p class="summary">
        {{ $t('queue.plan.summary', { units: remaining, count: jobCount }, jobCount) }}
      </p>
      <UiSelect
        v-if="printers.length > 1"
        v-model="printerId"
        :label="$t('queue.fields.printer')"
        :options="printerOptions"
      />
      <UiInput
        v-model="unitsPerJob"
        :label="$t('queue.plan.unitsPerJob')"
        :hint="$t('queue.plan.unitsPerJobHint')"
        inputmode="numeric"
        :error="unitsError"
      />
      <p
        v-if="error"
        class="error"
        role="alert"
      >
        {{ error }}
      </p>
      <button
        type="submit"
        class="hidden-submit"
        tabindex="-1"
        aria-hidden="true"
      />
    </form>
    <template #actions>
      <UiButton
        variant="ghost"
        @click="open = false"
      >
        {{ $t('queue.cancel') }}
      </UiButton>
      <UiButton
        :loading="saving"
        :disabled="printers.length === 0 && loaded"
        @click="create"
      >
        {{ $t('queue.plan.create') }}
      </UiButton>
    </template>
  </UiDialog>
</template>

<style scoped>
.form { display: grid; gap: var(--space-3); }
.summary { margin: 0; color: var(--muted); }
.error { margin: 0; color: var(--danger); }
.hidden-submit { display: none; }
</style>
