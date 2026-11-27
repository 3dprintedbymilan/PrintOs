import { analyticsQuery, changePercent } from '~/utils/analytics'
import { reportDate } from '~/utils/reports'
import type { ReportPeriodInfo } from '~/utils/reports'

export function useAnalyticsReport<T extends { period: ReportPeriodInfo, currency: string }>(
  endpoint: string,
  extra: () => Record<string, string | number | null | undefined> = () => ({}),
) {
  const { t, n, d } = useI18n()
  const auth = useAuthStore()
  const route = useRoute()
  const filters = useReportFilter()
  const { period, channel, from, to, productId, filter, validate, syncUrl } = filters

  const report = ref<T | null>(null) as Ref<T | null>
  const state = ref<'loading' | 'ready' | 'failed'>('loading')
  let requestId = 0

  const query = computed(() => analyticsQuery(filter.value, extra()))

  async function load() {
    if (!(await validate())) return
    const current = ++requestId
    if (!report.value) state.value = 'loading'
    try {
      const result = await auth.authorizedFetch<T>(`${endpoint}?${query.value}`)
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

  watch([period, channel, from, to, () => JSON.stringify(extra())], () => {
    syncUrl()
    load()
  })
  watch(() => route.query.productId, (value) => {
    productId.value = typeof value === 'string' ? value : null
    load()
  })
  onMounted(load)

  const currency = computed(() => report.value?.currency ?? 'EUR')
  const money = (value: number | null) => (value === null ? '-' : n(value, { style: 'currency', currency: currency.value }))
  const percent = (value: number | null) => (value === null ? '-' : `${n(value, { maximumFractionDigits: 1 })}%`)
  const number = (value: number | null, digits = 1) => (value === null ? '-' : n(value, { maximumFractionDigits: digits }))
  const hours = (value: number | null) => (value === null ? '-' : t('analytics.hoursValue', { hours: n(value, { maximumFractionDigits: 1 }) }))
  const dateText = (value: string) => d(reportDate(value), { dateStyle: 'medium', timeZone: 'UTC' })
  const rangeText = computed(() => (report.value ? t('reports.range', { from: dateText(report.value.period.from), to: dateText(report.value.period.to) }) : ''))
  const stamp = computed(() => (report.value ? `${report.value.period.from}_${report.value.period.to}` : ''))

  function delta(current: number, previous: number, hasPrevious: boolean, lowerIsBetter = false) {
    if (!hasPrevious) return { delta: undefined, trend: 'up' as const }
    const change = changePercent(current, previous)
    const text = change === null ? undefined : t('reports.deltaPercent', { percent: `${change > 0 ? '+' : ''}${n(change, { maximumFractionDigits: 1 })}%` })
    const good = lowerIsBetter ? current <= previous : current >= previous
    return { delta: text, trend: good ? ('up' as const) : ('down' as const) }
  }

  return { ...filters, report, state, load, query, money, percent, number, hours, dateText, rangeText, stamp, delta }
}
