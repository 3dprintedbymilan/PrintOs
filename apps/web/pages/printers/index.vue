<script setup lang="ts">
import type { Printer } from '~/utils/printerForm'
import { isActivePrint } from '~/utils/bambu'

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

const { status: live } = useBambuStatus()

function liveFor(printerId: string) {
  return live.value?.printers.find(candidate => candidate.printerId === printerId)
}

const bambuSubtitle = computed(() => {
  if (!live.value?.connected) return t('bambu.rowSubtitle')
  return live.value.connectionStatus === 'tokenExpired' ? t('bambu.errors.expiredShort') : t('bambu.connected.chip')
})

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
      <div
        v-for="printer in printers"
        :key="printer.id"
        class="entry"
      >
        <UiListRow
          :to="`/printers/${printer.id}`"
          :title="printer.name"
          :subtitle="subtitle(printer)"
        >
          <template
            v-if="printer.isArchived || liveFor(printer.id) || busyPrinterIds.has(printer.id)"
            #trailing
          >
            <UiStatusChip
              v-if="printer.isArchived"
              status="neutral"
            >
              {{ $t('printers.archived') }}
            </UiStatusChip>
            <BambuStateChip
              v-else-if="liveFor(printer.id)"
              :state="liveFor(printer.id)!.state"
            />
            <UiStatusChip
              v-else
              status="info"
            >
              {{ $t('printJobs.status.Printing') }}
            </UiStatusChip>
          </template>
        </UiListRow>
        <div
          v-if="liveFor(printer.id) && isActivePrint(liveFor(printer.id)!)"
          class="live"
        >
          <p class="job">
            {{ liveFor(printer.id)!.jobName ?? $t('bambu.now.unnamed') }}
          </p>
          <BambuProgress :printer="liveFor(printer.id)!" />
        </div>
      </div>

      <UiListRow
        to="/printers/bambu"
        :title="$t('bambu.title')"
        :subtitle="bambuSubtitle"
      />
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
.entry { display: grid; gap: var(--space-2); }
.live { display: grid; gap: var(--space-1); padding: 0 var(--space-4); }
.job { margin: 0; font-size: var(--font-size-sm); font-weight: 600; overflow-wrap: anywhere; }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.toggle { display: flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); color: var(--muted); font-size: var(--font-size-sm); }
.toggle input { width: 20px; height: 20px; accent-color: var(--accent); }
</style>
