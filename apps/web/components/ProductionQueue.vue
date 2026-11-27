<script setup lang="ts">
import { parseApiError } from '~/utils/apiError'
import { formatDuration, statusTone } from '~/utils/printJobs'
import type { PrintJob } from '~/utils/printJobs'
import type { Printer } from '~/utils/printerForm'
import { filamentIssues, parseUnits, plannedItems, printingItems } from '~/utils/productionQueue'
import type { MoveDirection, ProductionSuggestion, Queue, QueueItem } from '~/utils/productionQueue'

const { t, n, d } = useI18n()
const auth = useAuthStore()

const queue = ref<Queue>({ items: [], estimatedMinutes: 0, withoutEstimate: 0, manualOrder: false })
const suggestions = ref<ProductionSuggestion[]>([])
const failed = ref<PrintJob[]>([])
const printers = ref<Printer[]>([])
const loadStatus = ref<'loading' | 'ready' | 'failed'>('loading')
const notice = ref('')
const actionError = ref('')
const busyId = ref('')

const printing = computed(() => printingItems(queue.value.items))
const planned = computed(() => plannedItems(queue.value.items))
const hasWork = computed(() => queue.value.items.length > 0 || suggestions.value.length > 0 || failed.value.length > 0)
const printerOptions = computed(() => printers.value.filter(printer => !printer.isArchived).map(printer => ({ value: printer.id, label: printer.name })))

const planTarget = ref<{ orderId: string, number: number, remaining: number } | null>(null)
const planOpen = ref(false)

const editing = ref<QueueItem | null>(null)
const editOpen = ref(false)
const editPrinter = ref('')
const editUnits = ref('')
const editDuration = ref('')
const editErrors = ref<Record<string, string>>({})
const editSaving = ref(false)
const editError = ref('')

async function load(silent = false) {
  if (!silent) loadStatus.value = 'loading'
  try {
    const [loadedQueue, loadedSuggestions, loadedFailed, loadedPrinters] = await Promise.all([
      auth.authorizedFetch<Queue>('/api/production/queue'),
      auth.authorizedFetch<ProductionSuggestion[]>('/api/production/suggestions'),
      auth.authorizedFetch<PrintJob[]>('/api/print-jobs', { query: { status: 'Failed,Cancelled', limit: 100 } }),
      auth.authorizedFetch<Printer[]>('/api/printers'),
    ])
    queue.value = loadedQueue
    suggestions.value = loadedSuggestions
    failed.value = loadedFailed.filter(job => job.orderLineId && !job.requeued)
    printers.value = loadedPrinters
    loadStatus.value = 'ready'
  }
  catch {
    loadStatus.value = 'failed'
  }
}

async function run(id: string, task: () => Promise<void>, message: string) {
  busyId.value = id
  actionError.value = ''
  notice.value = ''
  try {
    await task()
    notice.value = message
  }
  catch {
    actionError.value = t('queue.errors.action')
  }
  finally {
    busyId.value = ''
  }
}

async function move(item: QueueItem, direction: MoveDirection) {
  await run(item.id, async () => {
    queue.value = await auth.authorizedFetch<Queue>(`/api/production/queue/${item.id}/move`, { method: 'POST', body: { direction } })
    await nextTick()
    const wanted = document.querySelector<HTMLElement>(`[data-move="${item.id}-${direction === 'top' ? 'down' : direction}"]:not(:disabled)`)
      ?? document.querySelector<HTMLElement>(`[data-move^="${item.id}-"]:not(:disabled)`)
    wanted?.focus()
  }, t('queue.moved', { name: item.name }))
}

async function resetOrder() {
  await run('reset', async () => {
    queue.value = await auth.authorizedFetch<Queue>('/api/production/queue/reset', { method: 'POST' })
  }, t('queue.resetDone'))
}

async function start(item: QueueItem) {
  await run(item.id, async () => {
    await auth.authorizedFetch(`/api/print-jobs/${item.id}/start`, { method: 'POST' })
    await load(true)
  }, t('queue.started', { name: item.name }))
}

async function requeue(job: PrintJob) {
  await run(job.id, async () => {
    await auth.authorizedFetch(`/api/print-jobs/${job.id}/requeue`, { method: 'POST' })
    await load(true)
  }, t('queue.requeued', { name: job.name }))
}

function openPlan(suggestion: ProductionSuggestion) {
  planTarget.value = {
    orderId: suggestion.orderId,
    number: suggestion.orderNumber,
    remaining: suggestion.lines.reduce((sum, line) => sum + line.remaining, 0),
  }
  planOpen.value = true
}

async function onPlanCreated(count: number) {
  notice.value = t('queue.plan.created', { count }, count)
  await load(true)
}

function openEdit(item: QueueItem) {
  editing.value = item
  editPrinter.value = item.printerId
  editUnits.value = item.plannedUnits === null ? '' : String(item.plannedUnits)
  editDuration.value = item.durationMinutes === null ? '' : String(item.durationMinutes)
  editErrors.value = {}
  editError.value = ''
  editOpen.value = true
}

async function saveEdit() {
  if (!editing.value) return
  const errors: Record<string, string> = {}
  const units = editUnits.value.trim() ? parseUnits(editUnits.value) : null
  if (editUnits.value.trim() && units === null) errors.units = t('queue.errors.units')
  const minutes = editDuration.value.trim() ? parseUnits(editDuration.value) : null
  if (editDuration.value.trim() && minutes === null) errors.duration = t('queue.errors.duration')
  editErrors.value = errors
  if (Object.keys(errors).length > 0) {
    nextTick(() => document.querySelector<HTMLElement>('dialog [aria-invalid="true"]')?.focus())
    return
  }
  editSaving.value = true
  editError.value = ''
  try {
    queue.value = await auth.authorizedFetch<Queue>(`/api/production/queue/${editing.value.id}`, {
      method: 'PATCH',
      body: { printerId: editPrinter.value || null, plannedUnits: units, durationMinutes: minutes },
    })
    editOpen.value = false
    notice.value = t('queue.planSaved', { name: editing.value.name })
  }
  catch (failure) {
    editError.value = parseApiError(failure).kind === 'validation' ? t('queue.errors.rejected') : t('queue.errors.action')
  }
  finally {
    editSaving.value = false
  }
}

const dueDay = (value: string) => d(new Date(`${value}T00:00:00`), { dateStyle: 'medium' })

function orderLine(item: QueueItem) {
  if (item.orderNumber === null) return ''
  const parts = [`#${item.orderNumber}`]
  if (item.customerName) parts.push(item.customerName)
  if (item.dueDate) parts.push(t('queue.due', { date: dueDay(item.dueDate) }))
  return parts.join(' · ')
}

function issueText(item: QueueItem) {
  const first = filamentIssues(item)[0]
  if (!first) return ''
  if (first.archived) return t('queue.filament.archived', { color: `${first.materialName} ${first.colorName}` })
  return t('queue.filament.short', { color: `${first.materialName} ${first.colorName}`, needed: n(first.neededGrams), available: n(first.availableGrams) })
}

onMounted(() => load())
</script>

<template>
  <div class="queue">
    <StateLoading v-if="loadStatus === 'loading'" />
    <StateError
      v-else-if="loadStatus === 'failed'"
      @retry="load()"
    />

    <template v-else>
      <p
        class="notice"
        role="status"
      >
        {{ notice }}
      </p>
      <p
        v-if="actionError"
        class="error"
        role="alert"
      >
        {{ actionError }}
      </p>

      <StateEmpty
        v-if="!hasWork"
        :message="$t('queue.empty')"
      />

      <section
        v-if="queue.items.length > 0"
        class="block"
        aria-labelledby="queue-now"
      >
        <header class="block-head">
          <h2
            id="queue-now"
            class="heading"
          >
            {{ $t('queue.title') }}
          </h2>
          <span class="meta">
            {{ $t('queue.summary', { count: queue.items.length, duration: formatDuration(queue.estimatedMinutes) || '0 min' }, queue.items.length) }}
          </span>
          <NuxtLink
            to="/schedule"
            class="schedule-link"
          >
            {{ $t('schedule.open') }}
          </NuxtLink>
        </header>
        <p
          v-if="queue.withoutEstimate > 0"
          class="hint"
        >
          {{ $t('queue.withoutEstimate', { count: queue.withoutEstimate }, queue.withoutEstimate) }}
        </p>

        <h3
          v-if="printing.length > 0"
          class="sub"
        >
          {{ $t('queue.printingNow') }}
        </h3>
        <ul
          v-if="printing.length > 0"
          class="cards"
        >
          <li
            v-for="item in printing"
            :key="item.id"
          >
            <UiCard class="card">
              <div class="card-head">
                <NuxtLink
                  :to="`/print-jobs/${item.id}`"
                  class="name"
                >
                  {{ item.name }}
                </NuxtLink>
                <UiStatusChip :status="statusTone(item.status)">
                  {{ $t(`printJobs.status.${item.status}`) }}
                </UiStatusChip>
              </div>
              <p class="facts">
                {{ item.printerName }}<template v-if="item.plannedUnits"> · {{ $t('queue.units', { count: item.plannedUnits }, item.plannedUnits) }}</template>
              </p>
              <p
                v-if="item.orderNumber !== null"
                class="order"
              >
                <NuxtLink :to="`/orders/${item.orderId}`">
                  {{ orderLine(item) }}
                </NuxtLink>
              </p>
              <p
                v-if="item.filamentShort"
                class="warn"
                role="note"
              >
                {{ issueText(item) }}
              </p>
            </UiCard>
          </li>
        </ul>

        <h3
          v-if="planned.length > 0"
          class="sub"
        >
          {{ $t('queue.upNext') }}
        </h3>
        <ol
          v-if="planned.length > 0"
          class="cards"
        >
          <li
            v-for="(item, index) in planned"
            :key="item.id"
          >
            <UiCard class="card">
              <div class="card-head">
                <span
                  class="rank"
                  aria-hidden="true"
                >{{ index + 1 }}</span>
                <NuxtLink
                  :to="`/print-jobs/${item.id}`"
                  class="name"
                >
                  {{ item.name }}
                </NuxtLink>
                <UiStatusChip
                  v-if="item.isOverdue"
                  status="danger"
                >
                  {{ $t('orders.overdue') }}
                </UiStatusChip>
              </div>
              <p
                v-if="item.orderNumber !== null"
                class="order"
              >
                <NuxtLink :to="`/orders/${item.orderId}`">
                  {{ orderLine(item) }}
                </NuxtLink>
              </p>
              <p class="facts">
                {{ item.printerName }}
                <template v-if="item.plannedUnits"> · {{ $t('queue.units', { count: item.plannedUnits }, item.plannedUnits) }}</template>
                <template v-if="item.durationMinutes !== null"> · {{ formatDuration(item.durationMinutes) }}</template>
                <template v-else> · {{ $t('queue.noEstimate') }}</template>
              </p>
              <p
                v-if="!item.hasFilament"
                class="hint"
              >
                {{ $t('queue.filament.none') }}
              </p>
              <p
                v-else-if="item.filamentShort"
                class="warn"
                role="note"
              >
                {{ issueText(item) }}
              </p>
              <div class="card-actions">
                <div
                  class="mover"
                  role="group"
                  :aria-label="$t('queue.reorder', { name: item.name })"
                >
                  <button
                    type="button"
                    class="icon-btn"
                    :data-move="`${item.id}-up`"
                    :disabled="!item.canMoveUp || busyId === item.id"
                    :aria-label="$t('queue.moveUp', { name: item.name })"
                    @click="move(item, 'up')"
                  >
                    <AppIcon name="chevron-left" class="rot-up" />
                  </button>
                  <button
                    type="button"
                    class="icon-btn"
                    :data-move="`${item.id}-down`"
                    :disabled="!item.canMoveDown || busyId === item.id"
                    :aria-label="$t('queue.moveDown', { name: item.name })"
                    @click="move(item, 'down')"
                  >
                    <AppIcon name="chevron-right" class="rot-down" />
                  </button>
                  <UiButton
                    variant="ghost"
                    :disabled="!item.canMoveUp || busyId === item.id"
                    :aria-label="$t('queue.moveTopLabel', { name: item.name })"
                    @click="move(item, 'top')"
                  >
                    {{ $t('queue.moveTop') }}
                  </UiButton>
                </div>
                <UiButton
                  variant="secondary"
                  :aria-label="$t('queue.editPlanLabel', { name: item.name })"
                  @click="openEdit(item)"
                >
                  {{ $t('queue.editPlan') }}
                </UiButton>
                <UiButton
                  :loading="busyId === item.id"
                  :aria-label="$t('queue.startLabel', { name: item.name })"
                  @click="start(item)"
                >
                  {{ $t('queue.start') }}
                </UiButton>
              </div>
            </UiCard>
          </li>
        </ol>
        <UiButton
          v-if="queue.manualOrder"
          variant="ghost"
          block
          :loading="busyId === 'reset'"
          @click="resetOrder"
        >
          {{ $t('queue.resetOrder') }}
        </UiButton>
        <p
          v-if="queue.manualOrder"
          class="hint"
        >
          {{ $t('queue.manualHint') }}
        </p>
        <p
          v-else-if="planned.length > 1"
          class="hint"
        >
          {{ $t('queue.autoHint') }}
        </p>
      </section>

      <section
        v-if="failed.length > 0"
        class="block"
        aria-labelledby="queue-failed"
      >
        <h2
          id="queue-failed"
          class="heading"
        >
          {{ $t('queue.failedTitle') }}
        </h2>
        <p class="hint">
          {{ $t('queue.failedHint') }}
        </p>
        <ul class="cards">
          <li
            v-for="job in failed"
            :key="job.id"
          >
            <UiCard class="card">
              <div class="card-head">
                <NuxtLink
                  :to="`/print-jobs/${job.id}`"
                  class="name"
                >
                  {{ job.name }}
                </NuxtLink>
                <UiStatusChip :status="statusTone(job.status)">
                  {{ $t(`printJobs.status.${job.status}`) }}
                </UiStatusChip>
              </div>
              <p
                v-if="job.orderNumber"
                class="order"
              >
                <NuxtLink :to="`/orders/${job.orderId}`">
                  #{{ job.orderNumber }}
                </NuxtLink>
              </p>
              <div class="card-actions">
                <UiButton
                  variant="secondary"
                  :loading="busyId === job.id"
                  :aria-label="$t('queue.requeueLabel', { name: job.name })"
                  @click="requeue(job)"
                >
                  {{ $t('queue.requeue') }}
                </UiButton>
              </div>
            </UiCard>
          </li>
        </ul>
      </section>

      <section
        v-if="suggestions.length > 0"
        class="block"
        aria-labelledby="queue-todo"
      >
        <h2
          id="queue-todo"
          class="heading"
        >
          {{ $t('queue.toPlanTitle') }}
        </h2>
        <p class="hint">
          {{ $t('queue.toPlanHint') }}
        </p>
        <ul class="cards">
          <li
            v-for="suggestion in suggestions"
            :key="suggestion.orderId"
          >
            <UiCard class="card">
              <div class="card-head">
                <NuxtLink
                  :to="`/orders/${suggestion.orderId}`"
                  class="name"
                >
                  #{{ suggestion.orderNumber }}<template v-if="suggestion.customerName"> · {{ suggestion.customerName }}</template>
                </NuxtLink>
                <UiStatusChip
                  v-if="suggestion.isOverdue"
                  status="danger"
                >
                  {{ $t('orders.overdue') }}
                </UiStatusChip>
              </div>
              <p
                v-if="suggestion.dueDate"
                class="facts"
              >
                {{ $t('queue.due', { date: dueDay(suggestion.dueDate) }) }}
              </p>
              <ul class="lines">
                <li
                  v-for="line in suggestion.lines"
                  :key="line.lineId"
                >
                  <span>{{ line.description }}</span>
                  <strong>{{ $t('queue.toPlanUnits', { remaining: line.remaining, ordered: line.ordered }) }}</strong>
                </li>
              </ul>
              <div class="card-actions">
                <UiButton
                  :aria-label="$t('queue.planLabel', { number: suggestion.orderNumber })"
                  @click="openPlan(suggestion)"
                >
                  {{ $t('queue.plan.open') }}
                </UiButton>
              </div>
            </UiCard>
          </li>
        </ul>
      </section>
    </template>

    <ProductionPlanDialog
      v-model="planOpen"
      :order-id="planTarget?.orderId ?? ''"
      :remaining="planTarget?.remaining ?? 0"
      :title="$t('queue.plan.title', { number: planTarget?.number ?? 0 })"
      @created="onPlanCreated"
    />

    <UiDialog
      v-model="editOpen"
      :title="$t('queue.editPlan')"
    >
      <form
        class="form"
        novalidate
        @submit.prevent="saveEdit"
      >
        <UiSelect
          v-model="editPrinter"
          :label="$t('queue.fields.printer')"
          :options="printerOptions"
        />
        <UiInput
          v-model="editUnits"
          :label="$t('queue.fields.units')"
          inputmode="numeric"
          :error="editErrors.units"
        />
        <UiInput
          v-model="editDuration"
          :label="$t('queue.fields.duration')"
          :hint="$t('queue.fields.durationHint')"
          inputmode="numeric"
          :error="editErrors.duration"
        />
        <p
          v-if="editError"
          class="error"
          role="alert"
        >
          {{ editError }}
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
          @click="editOpen = false"
        >
          {{ $t('queue.cancel') }}
        </UiButton>
        <UiButton
          :loading="editSaving"
          @click="saveEdit"
        >
          {{ $t('queue.save') }}
        </UiButton>
      </template>
    </UiDialog>
  </div>
</template>

<style scoped>
.queue { display: grid; gap: var(--space-4); }
.block { display: grid; gap: var(--space-3); }
.block-head { display: flex; align-items: baseline; justify-content: space-between; gap: var(--space-3); flex-wrap: wrap; }
.heading { margin: 0; font-size: var(--font-size-lg); }
.sub { margin: var(--space-2) 0 0; font-size: var(--font-size-md); color: var(--muted); font-weight: 600; }
.schedule-link { min-height: var(--tap-size); display: inline-flex; align-items: center; color: var(--accent-text); font-weight: 600; }
.meta, .hint { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.cards { display: grid; gap: var(--space-3); list-style: none; margin: 0; padding: 0; }
.card { display: grid; gap: var(--space-2); }
.card-head { display: flex; align-items: center; gap: var(--space-3); flex-wrap: wrap; }
.name { flex: 1; min-width: 0; font-weight: 600; color: var(--text); text-decoration: none; overflow-wrap: anywhere; }
.name:hover { text-decoration: underline; }
.rank { display: inline-grid; place-items: center; min-width: 28px; height: 28px; border-radius: var(--radius-pill); background: var(--accent-soft); color: var(--accent-text); font-weight: 700; font-size: var(--font-size-sm); }
.facts, .order { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.order a { color: var(--accent-text); }
.warn { margin: 0; padding: var(--space-2) var(--space-3); border-radius: var(--radius-sm); background: color-mix(in srgb, var(--warning) 16%, transparent); color: var(--text); font-size: var(--font-size-sm); border-left: 3px solid var(--warning); }
.card-actions { display: flex; flex-wrap: wrap; gap: var(--space-2); align-items: center; }
.mover { display: inline-flex; align-items: center; gap: var(--space-1); margin-right: auto; }
.icon-btn { min-width: var(--tap-size); min-height: var(--tap-size); display: inline-grid; place-items: center; background: var(--surface-2); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius-md); cursor: pointer; }
.icon-btn:disabled { opacity: 0.4; cursor: default; }
.icon-btn:focus-visible { outline: none; box-shadow: var(--focus-ring); }
.rot-up { transform: rotate(90deg); }
.rot-down { transform: rotate(90deg); }
.lines { list-style: none; margin: 0; padding: 0; display: grid; gap: var(--space-1); }
.lines li { display: flex; justify-content: space-between; gap: var(--space-3); font-size: var(--font-size-sm); }
.notice { margin: 0; min-height: 1.2em; color: var(--accent-text); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); }
.form { display: grid; gap: var(--space-3); }
.hidden-submit { display: none; }
</style>
