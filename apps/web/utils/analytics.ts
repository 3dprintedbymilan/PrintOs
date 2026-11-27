import { reportQuery } from './reports'
import type { ReportFilter, ReportPeriodInfo, ReportChannel, RankedItem } from './reports'

export type Granularity = 'day' | 'week' | 'month'

export const hoursPerDayOptions = [8, 12, 24]
export const deadDayOptions = [30, 60, 90, 180]

export interface ProductionTotals {
  jobs: number
  done: number
  failed: number
  cancelled: number
  successRatePercent: number | null
  printHours: number
  utilisationPercent: number | null
  filamentGrams: number
  wasteGrams: number
  wastePercent: number | null
  filamentCost: number
  runningCost: number
  costPerPrintHour: number | null
  jobsWithEstimate: number
  averageActualMinutes: number | null
  averageEstimatedMinutes: number | null
  durationVariancePercent: number | null
}

export interface PrinterProductionRow {
  printerId: string
  name: string
  done: number
  failed: number
  successRatePercent: number | null
  printHours: number
  utilisationPercent: number | null
  filamentGrams: number
  wasteGrams: number
  wastePercent: number | null
  costPerPrintHour: number | null
}

export interface MaterialProductionRow {
  material: string
  jobs: number
  failed: number
  failureRatePercent: number | null
  grams: number
  wasteGrams: number
  wastePercent: number | null
  failedGrams: number
}

export interface ProductionAnalytics {
  period: ReportPeriodInfo
  currency: string
  hoursPerDay: number
  current: ProductionTotals
  previous: ProductionTotals
  granularity: Granularity
  trend: { start: string, jobs: number, failed: number, printHours: number, filamentGrams: number }[]
  printers: PrinterProductionRow[]
  materials: MaterialProductionRow[]
}

export interface SalesTotals {
  documents: number
  revenue: number
  averageOrderValue: number | null
  discount: number
  discountPercent: number | null
  discountedDocuments: number
  customers: number
  repeatCustomers: number
  repeatRatePercent: number | null
}

export interface SalesAnalytics {
  period: ReportPeriodInfo
  currency: string
  channel: ReportChannel
  current: SalesTotals
  previous: SalesTotals
  granularity: Granularity
  trend: { start: string, documents: number, revenue: number }[]
  channels: { key: string, documents: number, revenue: number, averageOrderValue: number | null, revenueSharePercent: number | null, discount: number }[]
  customers: { customerId: string | null, label: string, documents: number, revenue: number, averageOrderValue: number | null, repeat: boolean, revenueSharePercent: number | null }[]
  products: RankedItem[]
  leadTime: { orders: number, averageDays: number | null, medianDays: number | null, longestDays: number | null, buckets: { key: string, orders: number }[] }
  marketTiming: {
    sales: number
    revenue: number
    weekdays: { weekday: number, sales: number, revenue: number }[]
    hours: { hour: number, sales: number, revenue: number }[]
    cells: { weekday: number, hour: number, sales: number, revenue: number }[]
  }
}

export interface InventoryAnalytics {
  period: ReportPeriodInfo
  currency: string
  deadDays: number
  summary: {
    filamentValue: number
    filamentGrams: number
    finishedValue: number
    finishedUnits: number
    totalValue: number
    filamentTurnover: number | null
    finishedTurnover: number | null
    filamentDaysOfCover: number | null
    finishedDaysOfCover: number | null
    deadStockValue: number
  }
  materials: {
    material: string
    onHandGrams: number
    value: number
    usedGrams: number
    usedCost: number
    usageGramsPerDay: number
    daysOfCover: number | null
    turnover: number | null
    pricePerKgCurrent: number | null
    pricePerKgPrevious: number | null
    priceChangePercent: number | null
    priceImpact: number | null
  }[]
  finishedGoods: { productId: string, label: string, onHand: number, value: number, unitsSold: number, daysOfCover: number | null, turnover: number | null }[]
  deadFilament: { spoolId: string, material: string, color: string, remainingGrams: number, value: number, daysSinceLastUse: number | null }[]
  deadFinished: { productId: string, variantId: string, label: string, onHand: number, stockValue: number, daysSinceLastSale: number | null }[]
}

export interface ProfitFigures {
  units: number
  revenue: number
  cost: number
  fees: number
  contribution: number
  contributionMarginPercent: number | null
  costComplete: boolean
}

export interface ProfitTotals {
  revenue: number
  cost: number
  fees: number
  contribution: number
  contributionMarginPercent: number | null
  standCosts: number
  netProfit: number
  netMarginPercent: number | null
  costComplete: boolean
}

export interface ProfitRow {
  key: string
  id: string | null
  label: string
  figures: ProfitFigures
}

export interface ProfitabilityAnalytics {
  period: ReportPeriodInfo
  currency: string
  channel: ReportChannel
  current: ProfitTotals
  previous: ProfitTotals
  components: { kind: string, amount: number, sharePercent: number | null }[]
  granularity: Granularity
  trend: { start: string, revenue: number, cost: number, contribution: number, net: number }[]
  channels: ProfitRow[]
  products: ProfitRow[]
  customers: ProfitRow[]
}

export function analyticsQuery(filter: ReportFilter, extra: Record<string, string | number | null | undefined> = {}): string {
  const params = new URLSearchParams(reportQuery(filter))
  for (const [key, value] of Object.entries(extra)) {
    if (value !== null && value !== undefined && value !== '') params.set(key, String(value))
  }
  return params.toString()
}

export interface BarLayout {
  x: number
  width: number
}

export interface BarScale {
  bars: BarLayout[]
  zero: number
}

export function barScale(values: number[], width = 100): BarScale {
  const high = Math.max(0, ...values)
  const low = Math.min(0, ...values)
  const span = high - low
  if (span === 0) return { bars: values.map(() => ({ x: 0, width: 0 })), zero: 0 }
  const zero = ((0 - low) / span) * width
  const bars = values.map((value) => {
    const size = (Math.abs(value) / span) * width
    const minimum = value === 0 ? 0 : Math.max(size, 1.5)
    return { x: value >= 0 ? zero : zero - minimum, width: minimum }
  })
  return { bars, zero }
}

export function heatLevel(value: number, peak: number): number {
  if (peak <= 0 || value <= 0) return 0
  return Math.min(1, Math.max(0.15, value / peak))
}

export function hourLabel(hour: number): string {
  return `${String(hour).padStart(2, '0')}:00`
}

export const weekdayNumbers = [1, 2, 3, 4, 5, 6, 7]

export const costComponentKinds = ['material', 'electricity', 'machine', 'labour', 'packaging', 'extra', 'unallocated']

export function componentColor(kind: string): string {
  const index = costComponentKinds.indexOf(kind)
  return index === 6 || index < 0 ? 'var(--chart-muted)' : `var(--chart-${index + 1})`
}

export function weekdayName(weekday: number, locale: string): string {
  const monday = new Date(Date.UTC(2024, 0, 1))
  monday.setUTCDate(monday.getUTCDate() + weekday - 1)
  return new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' }).format(monday)
}

export function changePercent(current: number, previous: number): number | null {
  if (previous === 0) return null
  return Math.round(((current - previous) * 1000) / Math.abs(previous)) / 10
}
