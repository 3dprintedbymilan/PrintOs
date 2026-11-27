<script setup lang="ts">
import { parseApiError } from '~/utils/apiError'
import { formatDuration } from '~/utils/printJobs'
import type { Printer } from '~/utils/printerForm'
import { fromDateTimeLocal, groupByDay, horizonOptions, parsePartSize, priorityValues, toDateTimeLocal } from '~/utils/schedule'
import type { Schedule, ScheduleBlocked, ScheduleItem } from '~/utils/schedule'

definePageMeta({ titleKey: 'schedule.title' })

const { t, d } = useI18n()
const auth = useAuthStore()

const schedule = ref<Schedule | null>(null)
const printers = ref<Printer[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const days = ref('14')
const notice = ref('')
const actionError = ref('')
const replanning = ref(false)

const editing = ref<{ jobId: string, name: string } | null>(null)
const editOpen = ref(false)
const editPriority = ref('1')
const editPrinter = ref('')
const editStart = ref('')
const editX = ref('')
const editY = ref('')
const editZ = ref('')
const editErrors = ref<Record<string, string>>({})
const editSaving = ref(false)
const editError = ref('')

const hasJobs = computed(() => (schedule.value?.lanes.some(lane => lane.items.length > 0) ?? false) || (schedule.value?.blocked.length ?? 0) > 0)
const horizonChoices = computed(() => horizonOptions.map(value => ({ value: String(value), label: t('schedule.horizon', { days: value }) })))
const priorityChoices = computed(() => priorityValues.map(value => ({ value: String(value), label: t(`schedule.priority.${value}`) })))
const printerChoices = computed(() => [
  { value: '', label: t('schedule.auto') },
  ...printers.value.filter(printer => !printer.isArchived).map(printer => ({ value: printer.id, label: printer.name })),
])

async function load(silent = false) {
  if (!silent) status.value = 'loading'
  try {
    const [loaded, loadedPrinters] = await Promise.all([
      auth.authorizedFetch<Schedule>('/api/production/schedule', { query: { days: days.value } }),
      auth.authorizedFetch<Printer[]>('/api/printers'),
    ])
    schedule.value = loaded
    printers.value = loadedPrinters
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

async function replan() {
  replanning.value = true
  actionError.value = ''
  notice.value = ''
  try {
    const result = await auth.authorizedFetch<Schedule>('/api/production/schedule/replan', { method: 'POST', query: { days: days.value } })
    schedule.value = result
    notice.value = result.reassigned > 0 ? t('schedule.replanned', { count: result.reassigned }, result.reassigned) : t('schedule.replannedNone')
  }
  catch {
    actionError.value = t('schedule.errors.action')
  }
  finally {
    replanning.value = false
  }
}

const dayLabel = (date: Date) => d(date, { weekday: 'long', day: 'numeric', month: 'long' })
const timeLabel = (value: string) => d(new Date(value), { hour: '2-digit', minute: '2-digit' })
const dueDay = (value: string) => d(new Date(`${value}T00:00:00`), { dateStyle: 'medium' })

function openEdit(target: { jobId: string, name: string, priority: number, pinned: boolean, pinnedStartAt: string | null, printerId: string | null, part?: [number | null, number | null, number | null] }) {
  editing.value = { jobId: target.jobId, name: target.name }
  editPriority.value = String(target.priority)
  editPrinter.value = target.pinned ? (target.printerId ?? '') : ''
  editStart.value = toDateTimeLocal(target.pinnedStartAt)
  editX.value = target.part?.[0] ? String(target.part[0]) : ''
  editY.value = target.part?.[1] ? String(target.part[1]) : ''
  editZ.value = target.part?.[2] ? String(target.part[2]) : ''
  editErrors.value = {}
  editError.value = ''
  editOpen.value = true
}

function editItem(item: ScheduleItem, printerId: string) {
  openEdit({ jobId: item.jobId, name: item.name, priority: item.priority, pinned: item.pinned, pinnedStartAt: item.pinnedStartAt, printerId })
}

function editBlocked(entry: ScheduleBlocked) {
  openEdit({
    jobId: entry.jobId,
    name: entry.name,
    priority: entry.priority,
    pinned: false,
    pinnedStartAt: null,
    printerId: null,
    part: [entry.partXMillimetres, entry.partYMillimetres, entry.partZMillimetres],
  })
}

async function saveEdit() {
  if (!editing.value) return
  const errors: Record<string, string> = {}
  const size = parsePartSize({ x: editX.value, y: editY.value, z: editZ.value })
  if (size.kind === 'invalid') errors.partSize = t('schedule.errors.partSize')
  const startAt = fromDateTimeLocal(editStart.value)
  if (editStart.value.trim() && startAt === null) errors.start = t('schedule.errors.start')
  if (startAt && !editPrinter.value) errors.start = t('schedule.errors.startNeedsPrinter')
  editErrors.value = errors
  if (Object.keys(errors).length > 0) {
    nextTick(() => document.querySelector<HTMLElement>('dialog [aria-invalid="true"]')?.focus())
    return
  }
  editSaving.value = true
  editError.value = ''
  const unpin = !editPrinter.value
  try {
    schedule.value = await auth.authorizedFetch<Schedule>(`/api/production/schedule/jobs/${editing.value.jobId}`, {
      method: 'PUT',
      body: {
        priority: Number(editPriority.value),
        printerId: editPrinter.value || null,
        startAt: unpin ? null : startAt,
        unpin: unpin ? true : null,
        partXMillimetres: size.kind === 'ok' ? size.x : null,
        partYMillimetres: size.kind === 'ok' ? size.y : null,
        partZMillimetres: size.kind === 'ok' ? size.z : null,
        clearPartSize: size.kind === 'none' ? true : null,
      },
    })
    editOpen.value = false
    notice.value = t('schedule.saved', { name: editing.value.name })
  }
  catch (failure) {
    editError.value = parseApiError(failure).kind === 'validation' ? t('schedule.errors.rejected') : t('schedule.errors.action')
  }
  finally {
    editSaving.value = false
  }
}

watch(days, () => load(true))
onMounted(() => load())
</script>

<template>
  <div class="schedule">
    <NuxtLink
      to="/prints?section=queue"
      class="back"
    >
      {{ $t('schedule.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load()"
    />

    <template v-else-if="schedule">
      <div class="toolbar">
        <UiSelect
          v-model="days"
          :label="$t('schedule.horizonLabel')"
          :options="horizonChoices"
        />
        <UiButton
          variant="secondary"
          :loading="replanning"
          @click="replan"
        >
          {{ $t('schedule.replan') }}
        </UiButton>
      </div>
      <p class="hint">
        {{ $t('schedule.readOnlyHint') }}
      </p>
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
        v-if="!hasJobs && schedule.lanes.length > 0"
        :message="$t('schedule.empty')"
      />
      <StateEmpty
        v-if="schedule.lanes.length === 0"
        :message="$t('schedule.noPrinters')"
      />

      <section
        v-if="schedule.blocked.length > 0"
        class="block"
        aria-labelledby="schedule-blocked"
      >
        <h2
          id="schedule-blocked"
          class="heading"
        >
          {{ $t('schedule.blockedTitle') }}
        </h2>
        <p class="hint">
          {{ $t('schedule.blockedHint') }}
        </p>
        <ul class="cards">
          <li
            v-for="entry in schedule.blocked"
            :key="entry.jobId"
          >
            <UiCard class="card">
              <div class="card-head">
                <NuxtLink
                  :to="`/print-jobs/${entry.jobId}`"
                  class="name"
                >
                  {{ entry.name }}
                </NuxtLink>
                <UiStatusChip status="warning">
                  {{ $t(`schedule.reason.${entry.reason}`) }}
                </UiStatusChip>
              </div>
              <div class="card-actions">
                <UiButton
                  variant="secondary"
                  :aria-label="$t('schedule.editLabel', { name: entry.name })"
                  @click="editBlocked(entry)"
                >
                  {{ $t('schedule.edit') }}
                </UiButton>
              </div>
            </UiCard>
          </li>
        </ul>
      </section>

      <section
        v-for="lane in schedule.lanes"
        :key="lane.printerId"
        class="block"
        :aria-labelledby="`lane-${lane.printerId}`"
      >
        <h2
          :id="`lane-${lane.printerId}`"
          class="heading"
        >
          {{ lane.name }}
        </h2>
        <p
          v-if="lane.items.length === 0"
          class="hint"
        >
          {{ $t('schedule.laneFree') }}
        </p>
        <div
          v-for="group in groupByDay(lane.items)"
          :key="group.key"
          class="day"
        >
          <h3 class="day-title">
            {{ dayLabel(group.date) }}
          </h3>
          <ol class="cards">
            <li
              v-for="item in group.items"
              :key="item.jobId"
            >
              <UiCard class="card">
                <div class="card-head">
                  <span class="time">{{ timeLabel(item.start) }} – {{ timeLabel(item.end) }}</span>
                  <UiStatusChip
                    v-if="item.status === 'Printing'"
                    status="info"
                  >
                    {{ $t('printJobs.status.Printing') }}
                  </UiStatusChip>
                  <UiStatusChip
                    v-if="item.late"
                    status="danger"
                  >
                    {{ $t('schedule.late') }}
                  </UiStatusChip>
                  <UiStatusChip
                    v-if="item.pinned"
                    status="neutral"
                  >
                    {{ $t('schedule.pinned') }}
                  </UiStatusChip>
                  <UiStatusChip
                    v-if="item.priority !== 1"
                    :status="item.priority > 1 ? 'warning' : 'neutral'"
                  >
                    {{ $t(`schedule.priority.${item.priority}`) }}
                  </UiStatusChip>
                </div>
                <NuxtLink
                  :to="`/print-jobs/${item.jobId}`"
                  class="name"
                >
                  {{ item.name }}
                </NuxtLink>
                <p class="facts">
                  {{ formatDuration(item.durationMinutes) }}<template v-if="item.durationEstimated"> · {{ $t('schedule.estimated', { minutes: schedule.defaultDurationMinutes }) }}</template>
                  <template v-if="item.orderNumber !== null"> · #{{ item.orderNumber }}</template>
                  <template v-if="item.dueDate"> · {{ $t('queue.due', { date: dueDay(item.dueDate) }) }}</template>
                </p>
                <p
                  v-if="item.noFilament && item.status === 'Planned'"
                  class="hint"
                >
                  {{ $t('queue.filament.none') }}
                </p>
                <div
                  v-if="item.status === 'Planned'"
                  class="card-actions"
                >
                  <UiButton
                    variant="secondary"
                    :aria-label="$t('schedule.editLabel', { name: item.name })"
                    @click="editItem(item, lane.printerId)"
                  >
                    {{ $t('schedule.edit') }}
                  </UiButton>
                </div>
              </UiCard>
            </li>
          </ol>
        </div>
      </section>

      <p
        v-if="schedule.beyondHorizon > 0"
        class="hint"
      >
        {{ $t('schedule.beyond', { count: schedule.beyondHorizon }, schedule.beyondHorizon) }}
      </p>
    </template>

    <UiDialog
      v-model="editOpen"
      :title="$t('schedule.edit')"
    >
      <form
        class="form"
        novalidate
        @submit.prevent="saveEdit"
      >
        <UiSelect
          v-model="editPriority"
          :label="$t('schedule.fields.priority')"
          :options="priorityChoices"
        />
        <UiSelect
          v-model="editPrinter"
          :label="$t('schedule.fields.printer')"
          :options="printerChoices"
        />
        <UiInput
          v-model="editStart"
          type="datetime-local"
          :label="$t('schedule.fields.start')"
          :hint="$t('schedule.fields.startHint')"
          :error="editErrors.start"
        />
        <fieldset class="size">
          <legend>{{ $t('schedule.fields.partSize') }}</legend>
          <UiInput
            v-model="editX"
            :label="$t('schedule.fields.partX')"
            inputmode="numeric"
            :error="editErrors.partSize"
          />
          <UiInput
            v-model="editY"
            :label="$t('schedule.fields.partY')"
            inputmode="numeric"
          />
          <UiInput
            v-model="editZ"
            :label="$t('schedule.fields.partZ')"
            inputmode="numeric"
          />
        </fieldset>
        <p class="hint">
          {{ $t('schedule.fields.partSizeHint') }}
        </p>
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
.schedule { display: grid; gap: var(--space-4); }
.back { color: var(--accent-text); text-decoration: none; min-height: var(--tap-size); display: inline-flex; align-items: center; }
.toolbar { display: grid; grid-template-columns: 1fr auto; gap: var(--space-3); align-items: end; }
.block { display: grid; gap: var(--space-3); }
.heading { margin: 0; font-size: var(--font-size-lg); }
.day { display: grid; gap: var(--space-2); }
.day-title { margin: 0; font-size: var(--font-size-md); color: var(--muted); font-weight: 600; text-transform: capitalize; }
.cards { display: grid; gap: var(--space-3); list-style: none; margin: 0; padding: 0; }
.card { display: grid; gap: var(--space-2); }
.card-head { display: flex; align-items: center; gap: var(--space-2); flex-wrap: wrap; }
.time { font-weight: 700; font-variant-numeric: tabular-nums; margin-right: auto; }
.name { font-weight: 600; color: var(--text); text-decoration: none; overflow-wrap: anywhere; }
.name:hover { text-decoration: underline; }
.facts, .hint { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.card-actions { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.notice { margin: 0; min-height: 1.2em; color: var(--accent-text); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); }
.form { display: grid; gap: var(--space-3); }
.size { display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--space-2); border: 0; padding: 0; margin: 0; }
.size legend { grid-column: 1 / -1; font-size: var(--font-size-sm); color: var(--muted); padding: 0; margin-bottom: var(--space-1); }
.hidden-submit { display: none; }
</style>
