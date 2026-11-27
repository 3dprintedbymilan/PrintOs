<script setup lang="ts">
import { defaultDashboardPeriod, hasProductionActivity, isDashboardPeriod, successTrend, dashboardPeriods } from '~/utils/dashboard'
import type { DashboardPeriod, DashboardSummary } from '~/utils/dashboard'
import { formatDuration, statusTone } from '~/utils/printJobs'
import { formatCurrency } from '~/utils/format'

definePageMeta({ titleKey: 'home.title' })

const { t, n, d, locale } = useI18n()
const auth = useAuthStore()
const route = useRoute()

const initial = isDashboardPeriod(route.query.period) ? route.query.period : defaultDashboardPeriod
const period = ref<DashboardPeriod>(initial)
const summary = ref<DashboardSummary | null>(null)
const state = ref<'loading' | 'ready' | 'failed'>('loading')
let requestId = 0

const periodOptions = computed(() => dashboardPeriods.map(value => ({ value, label: t(`home.period.${value}`) })))

async function load() {
  const current = ++requestId
  if (!summary.value) state.value = 'loading'
  try {
    const result = await auth.authorizedFetch<DashboardSummary>('/api/dashboard/summary', { query: { period: period.value } })
    if (current !== requestId) return
    summary.value = result
    state.value = 'ready'
  }
  catch {
    if (current !== requestId) return
    summary.value = null
    state.value = 'failed'
  }
}

watch(period, (value) => {
  history.replaceState(history.state, '', `${route.path}?period=${value}`)
  load()
})

onMounted(load)

const money = (amount: number) => formatCurrency(amount, locale.value, summary.value?.currency)
const compact = { maximumFractionDigits: 1 }

const empty = computed(() => (summary.value ? !hasProductionActivity(summary.value) : false))

const tiles = computed(() => {
  const data = summary.value
  if (!data) return []
  const { production, printers, stock } = data
  const finished = production.printsDone + production.printsFailed
  const printersValue = printers.total === 0
    ? '0'
    : t('home.kpi.printersValue', { printing: printers.printing, total: printers.total })
  return [
    {
      key: 'prints',
      label: t('home.kpi.prints'),
      value: n(production.printsDone),
      delta: finished > 0 && production.successRatePercent !== null
        ? t('home.kpi.success', { percent: n(production.successRatePercent, compact) })
        : undefined,
      trend: successTrend(production.successRatePercent),
    },
    { key: 'hours', label: t('home.kpi.hours'), value: t('home.kpi.hoursValue', { hours: n(production.printHours, compact) }) },
    {
      key: 'filament',
      label: t('home.kpi.filament'),
      value: t('home.kpi.kgValue', { kg: n(production.filamentKilograms, { maximumFractionDigits: 2 }) }),
    },
    { key: 'cost', label: t('home.kpi.cost'), value: money(production.filamentCost) },
    {
      key: 'printers',
      label: t('home.kpi.printers'),
      value: printersValue,
      delta: printers.online !== null ? t('home.kpi.online', { online: printers.online }) : undefined,
      trend: printers.online === 0 ? 'down' as const : 'up' as const,
    },
    {
      key: 'stock',
      label: t('home.kpi.stock'),
      value: t('home.kpi.kgValue', { kg: n(stock.totalGrams / 1000, { maximumFractionDigits: 2 }) }),
      delta: stock.alertCount > 0 ? t('home.kpi.alerts', { count: stock.alertCount }, stock.alertCount) : undefined,
      trend: 'down' as const,
    },
  ]
})

function recentMeta(print: DashboardSummary['recentPrints'][number]) {
  const parts = [print.printerName, d(new Date(print.at), { dateStyle: 'medium', timeStyle: 'short' })]
  if (print.durationMinutes !== null) parts.push(formatDuration(print.durationMinutes))
  if (print.grams > 0) parts.push(`${n(print.grams)} g`)
  return parts.join(' · ')
}
</script>

<template>
  <div class="stack">
    <p class="subtitle">
      {{ $t('home.subtitle') }}
    </p>

    <UiSegmentedControl
      v-model="period"
      :label="$t('home.period.label')"
      :options="periodOptions"
    />

    <nav
      class="actions"
      :aria-label="$t('home.actions.label')"
    >
      <NuxtLink to="/print-jobs/new">
        {{ $t('home.actions.print') }}
      </NuxtLink>
      <NuxtLink to="/inventory/new">
        {{ $t('home.actions.spool') }}
      </NuxtLink>
      <NuxtLink to="/calculator">
        {{ $t('home.actions.calculator') }}
      </NuxtLink>
    </nav>

    <StateLoading v-if="state === 'loading'" />
    <StateError
      v-else-if="state === 'failed'"
      @retry="load"
    />
    <template v-else-if="summary">
      <div
        class="kpis"
        role="group"
        :aria-label="$t('home.kpi.label')"
      >
        <UiKpiTile
          v-for="tile in tiles"
          :key="tile.key"
          :label="tile.label"
          :value="tile.value"
          :delta="tile.delta"
          :trend="tile.trend"
        />
      </div>

      <UiCard class="panel">
        <h2>{{ $t('home.trend.title') }}</h2>
        <p
          v-if="empty"
          class="calm"
        >
          {{ $t('home.empty') }}
        </p>
        <DashboardTrend
          v-else
          :points="summary.production.trend"
        />
      </UiCard>
    </template>

    <BambuNowCard />
    <StockAlertsCard />

    <UiCard
      v-if="state === 'ready' && summary"
      class="panel"
    >
      <h2>{{ $t('home.recent.title') }}</h2>
      <p
        v-if="summary.recentPrints.length === 0"
        class="calm"
      >
        {{ $t('home.recent.empty') }}
      </p>
      <ul v-else>
        <li
          v-for="print in summary.recentPrints"
          :key="print.id"
        >
          <NuxtLink :to="`/print-jobs/${print.id}`">
            <span class="text">
              <strong>{{ print.name }}</strong>
              <span class="detail">{{ recentMeta(print) }}</span>
            </span>
            <UiStatusChip :status="statusTone(print.status)">
              {{ $t(`printJobs.status.${print.status}`) }}
            </UiStatusChip>
          </NuxtLink>
        </li>
      </ul>
      <NuxtLink
        v-if="summary.recentPrints.length > 0"
        to="/prints"
        class="more"
      >
        {{ $t('home.recent.all') }}
      </NuxtLink>
    </UiCard>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-4); }
.subtitle { margin: 0; color: var(--muted); }
.stack :deep(.segment span) { padding-inline: 4px; font-size: var(--font-size-sm); white-space: nowrap; }
.actions { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.actions a {
  display: inline-flex;
  align-items: center;
  min-height: var(--tap-size);
  padding: 0 var(--space-4);
  border: 1px solid var(--border);
  border-radius: var(--radius-pill);
  background: var(--accent-soft);
  color: var(--accent-text);
  font-weight: 600;
  text-decoration: none;
}
.actions a:focus-visible { outline: none; box-shadow: var(--focus-ring); }
.kpis { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-3); }
.panel { display: grid; gap: var(--space-3); }
h2 { margin: 0; font-size: var(--font-size-md); }
.calm { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
ul { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
li a { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); min-height: var(--tap-size); color: var(--text); text-decoration: none; }
.text { display: grid; min-width: 0; }
.text strong { overflow-wrap: anywhere; }
.detail { color: var(--muted); font-size: var(--font-size-sm); }
.more { display: inline-flex; align-items: center; min-height: var(--tap-size); color: var(--accent-text); font-weight: 600; }
@media (min-width: 900px) {
  .kpis { grid-template-columns: repeat(3, minmax(0, 1fr)); }
}
</style>
