<script setup lang="ts">
import { rankingItems, rankingLevels, rankingShare, rankingSorts, reportDate, reportQuery, shareBarWidth, slowMoverDayOptions } from '~/utils/reports'
import type { BestsellerReport, RankedItem, RankingLevel, RankingSort, SlowMoverReport } from '~/utils/reports'

definePageMeta({ titleKey: 'reports.bestsellers.title' })

const { t, n, d } = useI18n()
const auth = useAuthStore()
const { period, channel, from, to, rangeError, filter, validate, syncUrl } = useReportFilter()

const sort = ref<RankingSort>('units')
const level = ref<RankingLevel>('product')
const days = ref('30')

const report = ref<BestsellerReport | null>(null)
const state = ref<'loading' | 'ready' | 'failed'>('loading')
const slow = ref<SlowMoverReport | null>(null)
const slowState = ref<'loading' | 'ready' | 'failed'>('loading')
let requestId = 0
let slowRequestId = 0

const sortOptions = computed(() => rankingSorts.map(value => ({ value, label: t(`reports.sort.${value}`) })))
const levelOptions = computed(() => rankingLevels.map(value => ({ value, label: t(`reports.level.${value}`) })))
const dayOptions = computed(() => slowMoverDayOptions.map(value => ({ value: String(value), label: t('reports.slow.daysOption', { days: value }) })))

async function load() {
  if (!(await validate())) return
  const current = ++requestId
  if (!report.value) state.value = 'loading'
  try {
    const result = await auth.authorizedFetch<BestsellerReport>(`/api/reports/bestsellers?${reportQuery(filter.value)}&level=${level.value}&limit=20`)
    if (current !== requestId) return
    report.value = result
    state.value = 'ready'
  }
  catch {
    if (current !== requestId) return
    report.value = null
    state.value = 'failed'
  }
}

async function loadSlow() {
  const current = ++slowRequestId
  if (!slow.value) slowState.value = 'loading'
  try {
    const result = await auth.authorizedFetch<SlowMoverReport>(`/api/reports/slow-movers?days=${days.value}`)
    if (current !== slowRequestId) return
    slow.value = result
    slowState.value = 'ready'
  }
  catch {
    if (current !== slowRequestId) return
    slow.value = null
    slowState.value = 'failed'
  }
}

watch([period, channel, from, to, level], () => {
  syncUrl()
  load()
})
watch(days, loadSlow)

onMounted(() => {
  load()
  loadSlow()
})

const money = (value: number, currency?: string) => n(value, { style: 'currency', currency: currency ?? report.value?.currency ?? 'EUR' })
const percent = (value: number | null) => (value === null ? '-' : `${n(value, { maximumFractionDigits: 1 })}%`)
const dateText = (value: string) => d(reportDate(value), { dateStyle: 'medium', timeZone: 'UTC' })
const rangeText = computed(() => (report.value ? t('reports.range', { from: dateText(report.value.period.from), to: dateText(report.value.period.to) }) : ''))
const items = computed(() => (report.value ? rankingItems(report.value, sort.value) : []))

function primary(item: RankedItem) {
  return sort.value === 'units' ? t('reports.unitsValue', { count: item.units }, item.units) : sort.value === 'revenue' ? money(item.revenue) : money(item.profit)
}

function secondary(item: RankedItem) {
  return t('reports.rankMeta', { revenue: money(item.revenue), profit: money(item.profit), margin: percent(item.marginPercent) }, item.units)
}

function shareText(item: RankedItem) {
  const share = rankingShare(item, sort.value)
  return share === null ? '' : t('reports.share', { percent: percent(share) })
}

function lastSold(days: number | null) {
  return days === null ? t('reports.slow.never') : t('reports.slow.daysAgo', { days }, days)
}
</script>

<template>
  <div class="stack">
    <ReportFilters
      v-model:period="period"
      v-model:channel="channel"
      v-model:from="from"
      v-model:to="to"
      :range-error="rangeError"
    />

    <UiCard class="options">
      <UiSegmentedControl
        v-model="sort"
        :label="$t('reports.sort.label')"
        :options="sortOptions"
      />
      <UiSegmentedControl
        v-model="level"
        :label="$t('reports.level.label')"
        :options="levelOptions"
      />
    </UiCard>

    <StateLoading v-if="state === 'loading'" />
    <StateError
      v-else-if="state === 'failed'"
      @retry="load"
    />
    <template v-else-if="report">
      <p class="range-text">
        {{ rangeText }}
      </p>
      <StateEmpty
        v-if="items.length === 0"
        :message="$t('reports.bestsellers.empty')"
      />
      <UiCard v-else>
        <h2>{{ $t(`reports.sort.${sort}`) }}</h2>
        <ol class="ranking">
          <li
            v-for="item in items"
            :key="item.key"
            class="item"
          >
            <span
              class="rank"
              aria-hidden="true"
            >{{ item.rank }}</span>
            <div class="body">
              <div class="head">
                <strong class="name">{{ item.label }}</strong>
                <span class="value">{{ primary(item) }}</span>
              </div>
              <div
                class="track"
                aria-hidden="true"
              >
                <span
                  class="fill"
                  :style="{ width: `${shareBarWidth(rankingShare(item, sort))}%` }"
                />
              </div>
              <p class="meta">
                {{ shareText(item) }}<template v-if="shareText(item)">
                  ·
                </template>{{ secondary(item) }}
              </p>
            </div>
          </li>
        </ol>
      </UiCard>
      <ReportExportButton
        v-if="items.length > 0"
        :path="`/api/reports/bestsellers/export?${reportQuery(filter)}&level=${level}&sort=${sort}`"
        :file-name="`bestsellers-${report.period.from}_${report.period.to}.csv`"
        :label="$t('reports.export.bestsellers')"
      />
    </template>

    <UiCard>
      <h2>{{ $t('reports.slow.title') }}</h2>
      <p class="hint">
        {{ $t('reports.slow.hint') }}
      </p>
      <UiSelect
        v-model="days"
        :label="$t('reports.slow.days')"
        :options="dayOptions"
      />
      <StateLoading v-if="slowState === 'loading'" />
      <StateError
        v-else-if="slowState === 'failed'"
        @retry="loadSlow"
      />
      <template v-else-if="slow">
        <p
          v-if="slow.items.length === 0"
          class="hint"
        >
          {{ $t('reports.slow.empty', { days: slow.days }) }}
        </p>
        <template v-else>
          <ReportExportButton
            :path="`/api/reports/slow-movers/export?days=${days}`"
            :file-name="`slow-movers-${days}d.csv`"
            :label="$t('reports.export.slow')"
          />
          <p class="summary">
            {{ $t('reports.slow.summary', { value: money(slow.stockValue, slow.currency) }, slow.count) }}
          </p>
          <ul class="slow">
            <li
              v-for="row in slow.items"
              :key="row.variantId"
              class="slow-row"
            >
              <div class="head">
                <strong class="name">{{ row.label }}</strong>
                <span class="value">{{ money(row.stockValue, slow.currency) }}</span>
              </div>
              <p class="meta">
                {{ $t('reports.slow.onHand', { count: row.onHand }, row.onHand) }} · {{ lastSold(row.daysSinceLastSale) }}
              </p>
            </li>
          </ul>
        </template>
      </template>
    </UiCard>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.options { display: grid; gap: var(--space-3); }
h2 { margin: 0 0 var(--space-2); font-size: var(--font-size-md); }
.range-text { margin: 0; color: var(--text); font-size: var(--font-size-sm); font-weight: 600; }
.ranking, .slow { display: grid; margin: 0; padding: 0; list-style: none; }
.item { display: grid; grid-template-columns: 28px minmax(0, 1fr); gap: var(--space-3); padding: var(--space-3) 0; border-bottom: 1px solid var(--border); }
.item:last-child, .slow-row:last-child { border-bottom: 0; }
.rank { display: grid; place-items: center; width: 28px; height: 28px; color: var(--accent-text); font-size: var(--font-size-sm); font-weight: 700; background: var(--accent-soft); border-radius: var(--radius-pill); }
.body { display: grid; gap: var(--space-1); min-width: 0; }
.head { display: flex; align-items: baseline; justify-content: space-between; gap: var(--space-3); }
.name { min-width: 0; overflow-wrap: anywhere; }
.value { font-weight: 700; font-variant-numeric: tabular-nums; white-space: nowrap; }
.track { height: 6px; background: var(--surface-2); border-radius: 3px; overflow: hidden; }
.fill { display: block; height: 100%; background: var(--accent); border-radius: 3px; }
.meta { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.hint { margin: 0 0 var(--space-3); color: var(--muted); font-size: var(--font-size-sm); }
.summary { margin: var(--space-3) 0 0; font-weight: 600; }
.slow-row { display: grid; gap: 2px; padding: var(--space-3) 0; border-bottom: 1px solid var(--border); }
</style>
