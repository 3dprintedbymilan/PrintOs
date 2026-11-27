import { addDaysIso, isIsoDate, isReportChannel, isReportPeriod, reportQuery, reportToday, validateRange } from '~/utils/reports'
import type { ReportChannel, ReportFilter, ReportPeriodKind } from '~/utils/reports'

export function useReportFilter() {
  const route = useRoute()
  const today = reportToday()
  const period = ref<ReportPeriodKind>(isReportPeriod(route.query.period) ? route.query.period : 'month')
  const channel = ref<ReportChannel>(isReportChannel(route.query.channel) ? route.query.channel : 'all')
  const from = ref(isIsoDate(route.query.from) ? route.query.from : addDaysIso(today, -29))
  const to = ref(isIsoDate(route.query.to) ? route.query.to : today)
  const productId = ref<string | null>(typeof route.query.productId === 'string' ? route.query.productId : null)
  const rangeError = ref<'from' | 'to' | null>(null)

  const filter = computed<ReportFilter>(() => ({ period: period.value, channel: channel.value, from: from.value, to: to.value, productId: productId.value }))
  const baseQuery = computed(() => reportQuery({ ...filter.value, productId: null }))

  async function validate() {
    rangeError.value = validateRange(filter.value)
    if (!rangeError.value) return true
    await nextTick()
    document.querySelector<HTMLElement>(`[data-range="${rangeError.value}"] input`)?.focus()
    return false
  }

  function syncUrl() {
    history.replaceState(history.state, '', `${route.path}?${reportQuery(filter.value)}`)
  }

  return { period, channel, from, to, productId, rangeError, filter, baseQuery, validate, syncUrl }
}
