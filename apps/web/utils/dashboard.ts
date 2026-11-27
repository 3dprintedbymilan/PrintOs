export type DashboardPeriod = 'today' | '7d' | '30d' | 'month'

export const dashboardPeriods: DashboardPeriod[] = ['today', '7d', '30d', 'month']

export const defaultDashboardPeriod: DashboardPeriod = '7d'

export interface DashboardTrendPoint {
  date: string
  printHours: number
  prints: number
}

export interface DashboardSummary {
  period: DashboardPeriod
  from: string
  to: string
  currency: string
  production: {
    printsDone: number
    printsFailed: number
    successRatePercent: number | null
    printHours: number
    filamentKilograms: number
    filamentCost: number
    trend: DashboardTrendPoint[]
  }
  printers: { total: number, online: number | null, printing: number }
  stock: { spools: number, totalGrams: number, alertCount: number }
  licenses: { expiringSoon: number, expired: number }
  recentPrints: {
    id: string
    name: string
    printerName: string
    status: 'Planned' | 'Printing' | 'Done' | 'Failed' | 'Cancelled'
    at: string
    durationMinutes: number | null
    grams: number
  }[]
}

export function isDashboardPeriod(value: unknown): value is DashboardPeriod {
  return typeof value === 'string' && (dashboardPeriods as string[]).includes(value)
}

export function hasProductionActivity(summary: DashboardSummary): boolean {
  const { production } = summary
  return production.printsDone + production.printsFailed > 0 || production.filamentKilograms > 0
}

export function successTrend(successRatePercent: number | null): 'up' | 'down' {
  return (successRatePercent ?? 100) >= 80 ? 'up' : 'down'
}

export function barHeights(points: DashboardTrendPoint[]): number[] {
  const max = Math.max(0, ...points.map(point => point.printHours))
  if (max === 0) return points.map(() => 0)
  return points.map(point => (point.printHours === 0 ? 0 : Math.max(4, Math.round((point.printHours / max) * 100))))
}

export function trendDate(date: string): Date {
  return new Date(`${date}T12:00:00Z`)
}
