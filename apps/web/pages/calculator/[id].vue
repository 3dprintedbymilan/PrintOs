<script setup lang="ts">
import type { CostSnapshot } from '~/utils/costCalculator'

definePageMeta({ titleKey: 'calculator.snapshotTitle' })

const { locale } = useI18n()
const route = useRoute()
const auth = useAuthStore()

const snapshot = ref<CostSnapshot | null>(null)
const status = ref<'loading' | 'ready' | 'failed'>('loading')

async function load() {
  status.value = 'loading'
  try {
    snapshot.value = await auth.authorizedFetch<CostSnapshot>(`/api/costing/snapshots/${route.params.id}`)
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

const savedAt = computed(() => snapshot.value
  ? new Intl.DateTimeFormat(locale.value, { dateStyle: 'long', timeStyle: 'short' }).format(new Date(snapshot.value.createdAt))
  : '')

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/calculator/saved"
      class="back"
    >
      {{ $t('calculator.backToSaved') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed' || !snapshot"
      @retry="load"
    />
    <UiCard
      v-else
      class="group"
    >
      <h2>{{ snapshot.name }}</h2>
      <p class="intro">
        {{ $t('calculator.snapshotMeta', { date: savedAt }) }}
      </p>
      <p class="intro">
        {{ snapshot.breakdown.printerName }} · {{ snapshot.breakdown.materialLabel }}
      </p>
      <CostBreakdownList :breakdown="snapshot.breakdown" />
      <p class="intro">
        {{ $t('calculator.immutable') }}
      </p>
    </UiCard>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.group { display: grid; gap: var(--space-3); }
.group h2 { margin: 0; font-size: var(--font-size-lg); overflow-wrap: anywhere; }
.intro { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
</style>
