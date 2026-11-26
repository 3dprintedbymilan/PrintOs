<script setup lang="ts">
import { filterPrintJobs, formatDuration, printJobStatuses, statusTone } from '~/utils/printJobs'
import type { PrintJob } from '~/utils/printJobs'
import type { Printer } from '~/utils/printerForm'

const { t, n, d } = useI18n()
const auth = useAuthStore()

const jobs = ref<PrintJob[]>([])
const printers = ref<Printer[]>([])
const loadStatus = ref<'loading' | 'ready' | 'failed'>('loading')
const filters = reactive({ status: '', printerId: '', search: '' })

const visible = computed(() => filterPrintJobs(jobs.value, filters))
const statusOptions = computed(() => [
  { value: '', label: t('printJobs.allStatuses') },
  ...printJobStatuses.map(value => ({ value, label: t(`printJobs.status.${value}`) })),
])
const printerOptions = computed(() => [
  { value: '', label: t('printJobs.allPrinters') },
  ...printers.value.map(printer => ({ value: printer.id, label: printer.name })),
])

async function load() {
  loadStatus.value = 'loading'
  try {
    const [loadedJobs, loadedPrinters] = await Promise.all([
      auth.authorizedFetch<PrintJob[]>('/api/print-jobs'),
      auth.authorizedFetch<Printer[]>('/api/printers', { query: { includeArchived: true } }),
    ])
    jobs.value = loadedJobs
    printers.value = loadedPrinters
    loadStatus.value = 'ready'
  }
  catch {
    loadStatus.value = 'failed'
  }
}

function subtitle(job: PrintJob) {
  const parts = [job.printerName, d(new Date(job.startedAt ?? job.createdAt), { dateStyle: 'medium', timeStyle: 'short' })]
  if (job.durationMinutes !== null) parts.push(formatDuration(job.durationMinutes))
  if (job.totalGrams > 0) parts.push(`${n(job.totalGrams)} g`)
  return parts.join(' · ')
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <StateLoading v-if="loadStatus === 'loading'" />
    <StateError
      v-else-if="loadStatus === 'failed'"
      @retry="load"
    />
    <template v-else>
      <UiButton
        block
        @click="navigateTo('/print-jobs/new')"
      >
        {{ $t('printJobs.add') }}
      </UiButton>

      <template v-if="jobs.length > 0">
        <div class="filters">
          <UiInput
            v-model="filters.search"
            :label="$t('printJobs.search')"
            type="search"
          />
          <div class="pair">
            <UiSelect
              v-model="filters.status"
              :label="$t('printJobs.fields.status')"
              :options="statusOptions"
            />
            <UiSelect
              v-model="filters.printerId"
              :label="$t('printJobs.fields.printer')"
              :options="printerOptions"
            />
          </div>
        </div>

        <StateEmpty
          v-if="visible.length === 0"
          :message="$t('printJobs.noMatches')"
        />
        <UiListRow
          v-for="job in visible"
          :key="job.id"
          :to="`/print-jobs/${job.id}`"
          :title="job.name"
          :subtitle="subtitle(job)"
        >
          <template #trailing>
            <UiStatusChip :status="statusTone(job.status)">
              {{ $t(`printJobs.status.${job.status}`) }}
            </UiStatusChip>
          </template>
        </UiListRow>
      </template>
      <StateEmpty
        v-else
        :message="$t('printJobs.empty')"
      />
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.filters { display: grid; gap: var(--space-2); }
.pair { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-2); }
</style>
