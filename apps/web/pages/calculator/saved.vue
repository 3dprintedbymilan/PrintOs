<script setup lang="ts">
import type { CostSnapshotSummary } from '~/utils/costCalculator'

definePageMeta({ titleKey: 'calculator.savedTitle' })

const { locale } = useI18n()
const auth = useAuthStore()

const snapshots = ref<CostSnapshotSummary[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')

async function load() {
  status.value = 'loading'
  try {
    snapshots.value = await auth.authorizedFetch<CostSnapshotSummary[]>('/api/costing/snapshots')
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function subtitle(snapshot: CostSnapshotSummary) {
  const date = new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium' }).format(new Date(snapshot.createdAt))
  const cost = formatCurrency(snapshot.totalCost, locale.value, snapshot.currency)
  return `${date} · ${snapshot.materialLabel} · ${cost}`
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/calculator"
      class="back"
    >
      {{ $t('calculator.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <template v-else>
      <StateEmpty
        v-if="snapshots.length === 0"
        :message="$t('calculator.savedEmpty')"
      />
      <UiListRow
        v-for="snapshot in snapshots"
        :key="snapshot.id"
        :to="`/calculator/${snapshot.id}`"
        :title="snapshot.name"
        :subtitle="subtitle(snapshot)"
      >
        <template #trailing>
          <strong class="price">{{ formatCurrency(snapshot.suggestedPrice, locale, snapshot.currency) }}</strong>
        </template>
      </UiListRow>
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.price { font-variant-numeric: tabular-nums; white-space: nowrap; }
</style>
