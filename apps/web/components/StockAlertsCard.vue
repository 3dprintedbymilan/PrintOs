<script setup lang="ts">
import type { StockAlert } from '~/utils/inventory'

const auth = useAuthStore()
const { n } = useI18n()

const alerts = ref<StockAlert[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')

async function load() {
  status.value = 'loading'
  try {
    alerts.value = await auth.authorizedFetch<StockAlert[]>('/api/inventory/alerts')
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

onMounted(load)
</script>

<template>
  <UiCard class="alerts">
    <h2>{{ $t('alerts.title') }}</h2>
    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <p
      v-else-if="alerts.length === 0"
      class="calm"
    >
      {{ $t('alerts.none') }}
    </p>
    <ul v-else>
      <li
        v-for="alert in alerts"
        :key="alert.materialId"
      >
        <NuxtLink :to="`/inventory?material=${alert.materialId}`">
          <span class="text">
            <strong>{{ alert.materialName }}</strong>
            <span class="detail">{{ $t('alerts.detail', { total: n(alert.totalGrams), threshold: n(alert.thresholdGrams) }) }}</span>
          </span>
          <UiStatusChip :status="alert.severity === 'LowStock' ? 'danger' : 'warning'">
            {{ $t(`alerts.severity.${alert.severity}`) }}
          </UiStatusChip>
        </NuxtLink>
      </li>
    </ul>
  </UiCard>
</template>

<style scoped>
.alerts { display: grid; gap: var(--space-3); }
h2 { margin: 0; font-size: var(--font-size-md); }
.calm { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
ul { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
a { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); min-height: var(--tap-size); color: var(--text); text-decoration: none; }
.text { display: grid; }
.detail { color: var(--muted); font-size: var(--font-size-sm); }
</style>
