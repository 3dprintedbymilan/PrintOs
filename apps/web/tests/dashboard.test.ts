import { describe, expect, it } from 'vitest'
import { barHeights, hasProductionActivity, isDashboardPeriod, successTrend } from '../utils/dashboard'
import type { DashboardSummary } from '../utils/dashboard'

const point = (printHours: number) => ({ date: '2026-11-27', printHours, prints: printHours > 0 ? 1 : 0 })

function summary(done: number, failed: number, kilograms: number): DashboardSummary {
  return {
    period: '7d',
    from: '',
    to: '',
    currency: 'EUR',
    production: { printsDone: done, printsFailed: failed, successRatePercent: null, printHours: 0, filamentKilograms: kilograms, filamentCost: 0, trend: [] },
    printers: { total: 0, online: null, printing: 0 },
    stock: { spools: 0, totalGrams: 0, alertCount: 0 },
    licenses: { expiringSoon: 0, expired: 0 },
    recentPrints: [],
  }
}

describe('dashboard helpers', () => {
  it('validates the period', () => {
    expect(isDashboardPeriod('30d')).toBe(true)
    expect(isDashboardPeriod('year')).toBe(false)
    expect(isDashboardPeriod(undefined)).toBe(false)
  })

  it('scales bars to the busiest day and keeps tiny values visible', () => {
    expect(barHeights([point(0), point(2), point(8), point(0.1)])).toEqual([0, 25, 100, 4])
  })

  it('returns flat bars when nothing was printed', () => {
    expect(barHeights([point(0), point(0)])).toEqual([0, 0])
    expect(barHeights([])).toEqual([])
  })

  it('detects an empty period', () => {
    expect(hasProductionActivity(summary(0, 0, 0))).toBe(false)
    expect(hasProductionActivity(summary(0, 1, 0))).toBe(true)
    expect(hasProductionActivity(summary(0, 0, 0.2))).toBe(true)
  })

  it('flags a low success rate', () => {
    expect(successTrend(95)).toBe('up')
    expect(successTrend(60)).toBe('down')
    expect(successTrend(null)).toBe('up')
  })
})
