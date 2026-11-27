export type ReportPeriodKind = 'week' | 'month' | 'quarter' | 'year' | 'custom'
export type ReportChannel = 'all' | 'orders' | 'pos'

export const reportPeriods: ReportPeriodKind[] = ['week', 'month', 'quarter', 'year', 'custom']
export const reportChannels: ReportChannel[] = ['all', 'orders', 'pos']

export interface ReportFilter {
  period: ReportPeriodKind
  channel: ReportChannel
  from: string
  to: string
  productId?: string | null
}

export interface ReportPeriodInfo {
  kind: ReportPeriodKind
  from: string
  to: string
  previousFrom: string
  previousTo: string
  days: number
}

export interface MarginFigures {
  units: number
  revenue: number
  cost: number
  grossProfit: number
  marginPercent: number | null
  costComplete: boolean
  standCosts: number
  netProfit: number
  netMarginPercent: number | null
}

export interface MarginDelta {
  revenueChange: number
  revenueChangePercent: number | null
  grossProfitChange: number
  grossProfitChangePercent: number | null
  marginPointsChange: number | null
}

export interface MarginRow {
  key: string
  productId: string | null
  variantId: string | null
  label: string
  sku: string | null
  figures: MarginFigures
}

export interface MarginTrendPoint {
  start: string
  revenue: number
  cost: number
  grossProfit: number
  marginPercent: number | null
}

export interface MarginReport {
  period: ReportPeriodInfo
  currency: string
  channel: ReportChannel
  current: MarginFigures
  previous: MarginFigures
  delta: MarginDelta
  granularity: 'day' | 'week' | 'month'
  trend: MarginTrendPoint[]
  channels: MarginRow[]
  markets: MarginRow[]
  products: MarginRow[]
  variants: MarginRow[]
}

export function isReportPeriod(value: unknown): value is ReportPeriodKind {
  return typeof value === 'string' && (reportPeriods as string[]).includes(value)
}

export function isReportChannel(value: unknown): value is ReportChannel {
  return typeof value === 'string' && (reportChannels as string[]).includes(value)
}

export function isIsoDate(value: unknown): value is string {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const parsed = new Date(`${value}T00:00:00Z`)
  return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value
}

export function reportQuery(filter: ReportFilter): string {
  const params = new URLSearchParams({ period: filter.period })
  if (filter.channel !== 'all') params.set('channel', filter.channel)
  if (filter.period === 'custom') {
    params.set('from', filter.from)
    params.set('to', filter.to)
  }
  if (filter.productId) params.set('productId', filter.productId)
  return params.toString()
}

export function validateRange(filter: ReportFilter): 'from' | 'to' | null {
  if (filter.period !== 'custom') return null
  if (!isIsoDate(filter.from)) return 'from'
  if (!isIsoDate(filter.to) || filter.to < filter.from) return 'to'
  return null
}

export function deltaTrend(change: number): 'up' | 'down' {
  return change >= 0 ? 'up' : 'down'
}

export function reportDate(date: string): Date {
  return new Date(`${date}T12:00:00Z`)
}

export function reportToday(now = new Date()): string {
  return now.toISOString().slice(0, 10)
}

export function addDaysIso(date: string, days: number): string {
  const value = reportDate(date)
  value.setUTCDate(value.getUTCDate() + days)
  return value.toISOString().slice(0, 10)
}

export interface ChartBar {
  x: number
  y: number
  width: number
  height: number
}

export interface ChartGeometry {
  bars: ChartBar[]
  line: { x: number, y: number }[]
  max: number
  ticks: { value: number, y: number }[]
  hasData: boolean
}

export function niceMax(value: number): number {
  if (value <= 0) return 1
  const magnitude = 10 ** Math.floor(Math.log10(value))
  const normalised = value / magnitude
  const step = normalised <= 1 ? 1 : normalised <= 2 ? 2 : normalised <= 5 ? 5 : 10
  return step * magnitude
}

export function chartGeometry(points: MarginTrendPoint[], width: number, height: number): ChartGeometry {
  const peak = Math.max(0, ...points.map(point => Math.max(point.revenue, point.grossProfit)))
  const floor = Math.min(0, ...points.map(point => point.grossProfit))
  const top = niceMax(peak)
  const bottom = floor < 0 ? -niceMax(-floor) : 0
  const span = top - bottom
  const scale = (value: number) => height - ((value - bottom) / span) * height
  const slot = points.length === 0 ? width : width / points.length
  const barWidth = Math.max(2, Math.min(36, slot * 0.7))
  const zero = scale(0)
  const bars = points.map((point, index) => {
    const y = scale(Math.max(point.revenue, 0))
    return { x: index * slot + (slot - barWidth) / 2, y, width: barWidth, height: Math.max(point.revenue > 0 ? 2 : 0, zero - y) }
  })
  const line = points.map((point, index) => ({ x: index * slot + slot / 2, y: scale(point.grossProfit) }))
  const ticks = [top, top / 2, 0].map(value => ({ value, y: scale(value) }))
  return { bars, line, max: top, ticks, hasData: peak > 0 }
}
