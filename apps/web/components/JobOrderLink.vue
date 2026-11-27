<script setup lang="ts">
import { parseApiError } from '~/utils/apiError'
import type { PrintJob } from '~/utils/printJobs'
import { parseUnits } from '~/utils/productionQueue'
import type { ProductionSuggestion } from '~/utils/productionQueue'

const props = defineProps<{ job: PrintJob }>()
const emit = defineEmits<{ changed: [job: PrintJob], requeued: [job: PrintJob] }>()

const { t } = useI18n()
const auth = useAuthStore()

const busy = ref(false)
const message = ref('')
const failure = ref('')
const open = ref(false)
const suggestions = ref<ProductionSuggestion[]>([])
const lineId = ref('')
const units = ref('')
const unitsError = ref('')
const lineError = ref('')
const dialogError = ref('')
const loadingLines = ref(false)

const lineOptions = computed(() => [
  { value: '', label: t('queue.link.chooseLine') },
  ...suggestions.value.flatMap(order => order.lines.map(line => ({
    value: line.lineId,
    label: t('queue.link.option', { number: order.orderNumber, description: line.description, remaining: line.remaining }),
  }))),
])
const canRequeue = computed(() => (props.job.status === 'Failed' || props.job.status === 'Cancelled') && !props.job.requeued)
const unitsLabel = computed(() => t(props.job.status === 'Done' ? 'queue.link.unitsDone' : 'queue.link.unitsPlanned'))

async function openDialog() {
  open.value = true
  lineId.value = ''
  units.value = props.job.status === 'Done' ? String(props.job.finishedUnits ?? '') : String(props.job.plannedUnits ?? '')
  unitsError.value = ''
  lineError.value = ''
  dialogError.value = ''
  loadingLines.value = true
  try {
    suggestions.value = await auth.authorizedFetch<ProductionSuggestion[]>('/api/production/suggestions')
  }
  catch {
    dialogError.value = t('queue.errors.load')
  }
  finally {
    loadingLines.value = false
  }
}

async function link() {
  lineError.value = lineId.value ? '' : t('queue.link.lineRequired')
  const parsed = parseUnits(units.value)
  unitsError.value = parsed === null ? t('queue.errors.units') : ''
  if (lineError.value || unitsError.value) {
    nextTick(() => document.querySelector<HTMLElement>('dialog [aria-invalid="true"]')?.focus())
    return
  }
  busy.value = true
  dialogError.value = ''
  try {
    const updated = await auth.authorizedFetch<PrintJob>(`/api/print-jobs/${props.job.id}/order-line`, { method: 'POST', body: { orderLineId: lineId.value, units: parsed } })
    open.value = false
    message.value = t('queue.link.linked')
    emit('changed', updated)
  }
  catch (error) {
    dialogError.value = parseApiError(error).kind === 'validation' ? t('queue.errors.rejected') : t('queue.errors.action')
  }
  finally {
    busy.value = false
  }
}

async function run(task: () => Promise<PrintJob>, done: string, event: 'changed' | 'requeued') {
  busy.value = true
  failure.value = ''
  message.value = ''
  try {
    const result = await task()
    message.value = done
    if (event === 'changed') emit('changed', result)
    else emit('requeued', result)
  }
  catch {
    failure.value = t('queue.errors.action')
  }
  finally {
    busy.value = false
  }
}

const unlink = () => run(
  () => auth.authorizedFetch<PrintJob>(`/api/print-jobs/${props.job.id}/order-line`, { method: 'POST', body: { orderLineId: null } }),
  t('queue.link.unlinked'),
  'changed',
)
const requeue = () => run(
  () => auth.authorizedFetch<PrintJob>(`/api/print-jobs/${props.job.id}/requeue`, { method: 'POST' }),
  t('queue.requeued', { name: props.job.name }),
  'requeued',
)
</script>

<template>
  <UiCard class="group">
    <h2 class="heading">
      {{ $t('queue.link.title') }}
    </h2>
    <p
      v-if="job.orderLineId && job.orderId"
      class="order"
    >
      <NuxtLink :to="`/orders/${job.orderId}`">
        #{{ job.orderNumber }} · {{ job.orderLineDescription }}
      </NuxtLink>
      <span class="hint">
        {{ job.status === 'Done'
          ? $t('queue.link.producedUnits', { count: job.finishedUnits ?? 0 }, job.finishedUnits ?? 0)
          : $t('queue.link.plannedUnits', { count: job.plannedUnits ?? 0 }, job.plannedUnits ?? 0) }}
      </span>
    </p>
    <p
      v-else
      class="hint"
    >
      {{ $t('queue.link.none') }}
    </p>
    <p
      v-if="job.requeued"
      class="hint"
    >
      {{ $t('queue.link.alreadyRequeued') }}
    </p>
    <p
      v-if="job.requeuedFromId"
      class="hint"
    >
      <NuxtLink :to="`/print-jobs/${job.requeuedFromId}`">
        {{ $t('queue.link.reprintOf') }}
      </NuxtLink>
    </p>
    <p
      class="status"
      role="status"
    >
      {{ message }}
    </p>
    <p
      v-if="failure"
      class="error"
      role="alert"
    >
      {{ failure }}
    </p>
    <div class="actions">
      <UiButton
        variant="secondary"
        block
        @click="openDialog"
      >
        {{ job.orderLineId ? $t('queue.link.change') : $t('queue.link.action') }}
      </UiButton>
      <UiButton
        v-if="job.orderLineId"
        variant="ghost"
        block
        :loading="busy"
        @click="unlink"
      >
        {{ $t('queue.link.unlink') }}
      </UiButton>
      <UiButton
        v-if="canRequeue"
        block
        :loading="busy"
        @click="requeue"
      >
        {{ $t('queue.requeue') }}
      </UiButton>
    </div>

    <UiDialog
      v-model="open"
      :title="$t('queue.link.title')"
    >
      <form
        class="form"
        novalidate
        @submit.prevent="link"
      >
        <StateLoading v-if="loadingLines" />
        <template v-else>
          <p
            v-if="lineOptions.length === 1 && !dialogError"
            class="hint"
          >
            {{ $t('queue.link.noLines') }}
          </p>
          <UiSelect
            v-model="lineId"
            :label="$t('queue.link.line')"
            :options="lineOptions"
            :error="lineError"
          />
          <UiInput
            v-model="units"
            :label="unitsLabel"
            inputmode="numeric"
            :error="unitsError"
          />
        </template>
        <p
          v-if="dialogError"
          class="error"
          role="alert"
        >
          {{ dialogError }}
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
          :loading="busy"
          @click="link"
        >
          {{ $t('queue.link.save') }}
        </UiButton>
      </template>
    </UiDialog>
  </UiCard>
</template>

<style scoped>
.group { display: grid; gap: var(--space-3); }
.heading { margin: 0; font-size: var(--font-size-lg); }
.order { margin: 0; display: grid; gap: var(--space-1); }
.order a { color: var(--accent-text); overflow-wrap: anywhere; }
.hint { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.hint a { color: var(--accent-text); }
.status { margin: 0; min-height: 1.2em; color: var(--accent-text); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); }
.actions { display: grid; gap: var(--space-2); }
.form { display: grid; gap: var(--space-3); }
.hidden-submit { display: none; }
</style>
