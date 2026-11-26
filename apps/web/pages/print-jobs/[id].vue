<script setup lang="ts">
import { formatDuration, isFinished, statusTone } from '~/utils/printJobs'
import type { PrintJob } from '~/utils/printJobs'

definePageMeta({ titleKey: 'printJobs.detailTitle' })

const { t, n, d } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const id = String(route.params.id)

const job = ref<PrintJob | null>(null)
const loadStatus = ref<'loading' | 'ready' | 'failed'>('loading')
const mode = ref<'view' | 'edit' | 'finish'>('view')
const message = ref('')
const actionError = ref('')
const busy = ref(false)
const confirmDelete = ref(false)

async function load() {
  loadStatus.value = 'loading'
  try {
    job.value = await auth.authorizedFetch<PrintJob>(`/api/print-jobs/${id}`)
    loadStatus.value = 'ready'
  }
  catch {
    loadStatus.value = 'failed'
  }
}

function formatTime(value: string | null) {
  return value ? d(new Date(value), { dateStyle: 'medium', timeStyle: 'short' }) : '–'
}

function onSaved(saved: PrintJob) {
  const finishing = mode.value === 'finish'
  job.value = saved
  mode.value = 'view'
  message.value = finishing ? t('printJobs.finishedMessage') : t('printJobs.savedMessage')
}

async function start() {
  busy.value = true
  actionError.value = ''
  message.value = ''
  try {
    job.value = await auth.authorizedFetch<PrintJob>(`/api/print-jobs/${id}/start`, { method: 'POST' })
    message.value = t('printJobs.startedMessage')
  }
  catch {
    actionError.value = t('printJobs.errors.save')
  }
  finally {
    busy.value = false
  }
}

async function remove() {
  busy.value = true
  actionError.value = ''
  try {
    await auth.authorizedFetch(`/api/print-jobs/${id}`, { method: 'DELETE' })
    confirmDelete.value = false
    await navigateTo('/prints')
  }
  catch {
    confirmDelete.value = false
    actionError.value = t('printJobs.errors.save')
  }
  finally {
    busy.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/prints"
      class="back"
    >
      {{ $t('printJobs.back') }}
    </NuxtLink>

    <StateLoading v-if="loadStatus === 'loading'" />
    <StateError
      v-else-if="loadStatus === 'failed'"
      @retry="load"
    />

    <template v-else-if="job">
      <PrintJobForm
        v-if="mode !== 'view'"
        :key="mode"
        :mode="mode"
        :job="job"
        @saved="onSaved"
        @cancel="mode = 'view'"
      />

      <template v-else>
        <UiCard class="group">
          <div class="head">
            <h2 class="heading">
              {{ job.name }}
            </h2>
            <UiStatusChip :status="statusTone(job.status)">
              {{ $t(`printJobs.status.${job.status}`) }}
            </UiStatusChip>
          </div>
          <dl class="facts">
            <div>
              <dt>{{ $t('printJobs.fields.printer') }}</dt>
              <dd>{{ job.printerName }}</dd>
            </div>
            <div>
              <dt>{{ $t('printJobs.fields.startedAt') }}</dt>
              <dd>{{ formatTime(job.startedAt) }}</dd>
            </div>
            <div v-if="isFinished(job.status)">
              <dt>{{ $t('printJobs.fields.finishedAt') }}</dt>
              <dd>{{ formatTime(job.finishedAt) }}</dd>
            </div>
            <div v-if="job.durationMinutes !== null">
              <dt>{{ $t('printJobs.duration') }}</dt>
              <dd>{{ formatDuration(job.durationMinutes) }}</dd>
            </div>
            <div v-if="job.finishedUnits">
              <dt>{{ $t('printJobs.fields.finishedUnits') }}</dt>
              <dd>{{ n(job.finishedUnits) }}</dd>
            </div>
            <div v-if="job.externalSource">
              <dt>{{ $t('printJobs.source') }}</dt>
              <dd>{{ job.externalSource }}</dd>
            </div>
          </dl>
          <p
            class="status"
            role="status"
          >
            {{ message }}
          </p>
          <p
            v-if="actionError"
            class="error"
            role="alert"
          >
            {{ actionError }}
          </p>
        </UiCard>

        <section aria-labelledby="filament-heading">
          <h2
            id="filament-heading"
            class="heading"
          >
            {{ $t('printJobs.filament') }}
          </h2>
          <p
            v-if="job.filaments.length === 0"
            class="muted"
          >
            {{ $t('printJobs.noFilament') }}
          </p>
          <ul
            v-else
            class="lines"
          >
            <li
              v-for="line in job.filaments"
              :key="line.spoolId"
            >
              <span
                class="swatch"
                :style="{ background: line.colorHex ?? 'var(--surface-2)' }"
                aria-hidden="true"
              />
              <span class="what">
                <strong>{{ line.materialName }} · {{ line.colorName }}</strong>
                <span
                  v-if="line.wasteGrams"
                  class="muted"
                >{{ $t('printJobs.wasteOf', { grams: n(line.wasteGrams) }) }}</span>
              </span>
              <strong>{{ n(line.grams) }} g</strong>
            </li>
          </ul>
          <p
            v-if="job.filaments.length > 0 && !isFinished(job.status)"
            class="muted"
          >
            {{ $t('printJobs.hints.planned') }}
          </p>
        </section>

        <UiCard
          v-if="job.notes"
          class="group"
        >
          <h2 class="heading">
            {{ $t('printJobs.fields.notes') }}
          </h2>
          <p class="notes">
            {{ job.notes }}
          </p>
        </UiCard>

        <div class="actions">
          <UiButton
            v-if="job.status === 'Planned'"
            block
            :loading="busy"
            @click="start"
          >
            {{ $t('printJobs.start') }}
          </UiButton>
          <UiButton
            v-if="job.status === 'Planned' || job.status === 'Printing'"
            :variant="job.status === 'Printing' ? 'primary' : 'secondary'"
            block
            @click="mode = 'finish'; message = ''"
          >
            {{ $t('printJobs.finish') }}
          </UiButton>
          <UiButton
            variant="secondary"
            block
            @click="mode = 'edit'; message = ''"
          >
            {{ $t('printJobs.edit') }}
          </UiButton>
          <UiButton
            variant="danger"
            block
            @click="confirmDelete = true"
          >
            {{ $t('printJobs.delete') }}
          </UiButton>
        </div>
      </template>
    </template>

    <UiDialog
      v-model="confirmDelete"
      :title="$t('printJobs.deleteTitle')"
    >
      <p>{{ $t('printJobs.deleteBody') }}</p>
      <template #actions>
        <UiButton
          variant="ghost"
          @click="confirmDelete = false"
        >
          {{ $t('printJobs.cancel') }}
        </UiButton>
        <UiButton
          variant="danger"
          :loading="busy"
          @click="remove"
        >
          {{ $t('printJobs.delete') }}
        </UiButton>
      </template>
    </UiDialog>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.group { display: grid; gap: var(--space-3); }
.head { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-3); }
.heading { margin: 0; font-size: var(--font-size-md); overflow-wrap: anywhere; }
.facts { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-3); margin: 0; }
.facts dt { color: var(--muted); font-size: var(--font-size-sm); }
.facts dd { margin: 0; font-weight: 600; }
.status { margin: 0; min-height: 1.25em; color: var(--accent); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
.muted { color: var(--muted); font-size: var(--font-size-sm); }
.notes { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
.lines { display: grid; gap: var(--space-2); margin: var(--space-2) 0 0; padding: 0; list-style: none; }
.lines li { display: flex; align-items: center; gap: var(--space-3); padding: var(--space-3) var(--space-4); background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
.what { display: grid; flex: 1; }
.swatch { display: block; width: 28px; height: 28px; border-radius: var(--radius-pill); border: 1px solid var(--border); }
.actions { display: grid; gap: var(--space-2); }
</style>
