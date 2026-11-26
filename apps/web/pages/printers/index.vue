<script setup lang="ts">
import type { Printer } from '~/utils/printerForm'

definePageMeta({ titleKey: 'printers.title' })

const { t, n } = useI18n()
const auth = useAuthStore()

const printers = ref<Printer[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const showArchived = ref(false)
const busyPrinterIds = ref(new Set<string>())

async function load() {
  status.value = 'loading'
  try {
    printers.value = await auth.authorizedFetch<Printer[]>('/api/printers', { query: { includeArchived: showArchived.value } })
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
    return
  }
  try {
    const running = await auth.authorizedFetch<{ printerId: string }[]>('/api/print-jobs', { query: { status: 'Printing' } })
    busyPrinterIds.value = new Set(running.map(job => job.printerId))
  }
  catch {
    busyPrinterIds.value = new Set()
  }
}

function subtitle(printer: Printer) {
  const volume = `${printer.buildVolumeXMillimetres}×${printer.buildVolumeYMillimetres}×${printer.buildVolumeZMillimetres} mm`
  const rate = t('printers.perHour', { amount: n(printer.depreciationPerHour, { minimumFractionDigits: 2, maximumFractionDigits: 4 }) })
  return `${printer.model} · ${volume} · ${rate}`
}

watch(showArchived, load)
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
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <template v-else>
      <StateEmpty
        v-if="printers.length === 0"
        :message="$t('printers.empty')"
      />
      <UiListRow
        v-for="printer in printers"
        :key="printer.id"
        :to="`/printers/${printer.id}`"
        :title="printer.name"
        :subtitle="subtitle(printer)"
      >
        <template
          v-if="printer.isArchived || busyPrinterIds.has(printer.id)"
          #trailing
        >
          <UiStatusChip
            v-if="printer.isArchived"
            status="neutral"
          >
            {{ $t('printers.archived') }}
          </UiStatusChip>
          <UiStatusChip
            v-else
            status="info"
          >
            {{ $t('printJobs.status.Printing') }}
          </UiStatusChip>
        </template>
      </UiListRow>
    </template>

    <label class="toggle">
      <input
        v-model="showArchived"
        type="checkbox"
      >
      {{ $t('printers.showArchived') }}
    </label>

    <UiButton
      block
      @click="navigateTo('/printers/new')"
    >
      {{ $t('printers.add') }}
    </UiButton>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.toggle { display: flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); color: var(--muted); font-size: var(--font-size-sm); }
.toggle input { width: 20px; height: 20px; accent-color: var(--accent); }
</style>
