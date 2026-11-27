import { describe, expect, it } from 'vitest'
import { addDaysIso, chartGeometry, deltaTrend, isIsoDate, isRankingSort, niceMax, rankingShare, reportQuery, shareBarWidth, validateRange } from '../utils/reports'
import type { MarginTrendPoint, ReportFilter } from '../utils/reports'

const filter = (overrides: Partial<ReportFilter> = {}): ReportFilter => ({ period: 'month', channel: 'all', from: '2026-11-01', to: '2026-11-30', ...overrides })

const point = (start: string, revenue: number, grossProfit: number): MarginTrendPoint => ({ start, revenue, cost: revenue - grossProfit, grossProfit, marginPercent: null })

describe('reportQuery', () => {
  it('only sends the range for custom periods and omits the default channel', () => {
    expect(reportQuery(filter())).toBe('period=month')
    expect(reportQuery(filter({ period: 'custom', channel: 'pos' }))).toBe('period=custom&channel=pos&from=2026-11-01&to=2026-11-30')
  })

  it('adds the product for the drill-down', () => {
    expect(reportQuery(filter({ productId: 'abc' }))).toBe('period=month&productId=abc')
  })
})

describe('validateRange', () => {
  it('accepts presets without looking at the dates', () => {
    expect(validateRange(filter({ from: '' }))).toBeNull()
  })

  it('flags a missing start and an end before the start', () => {
    expect(validateRange(filter({ period: 'custom', from: '' }))).toBe('from')
    expect(validateRange(filter({ period: 'custom', from: '2026-11-10', to: '2026-11-01' }))).toBe('to')
    expect(validateRange(filter({ period: 'custom', from: '2026-11-10', to: '2026-11-10' }))).toBeNull()
  })
})

describe('dates and trend', () => {
  it('validates ISO dates and shifts them', () => {
    expect(isIsoDate('2026-02-30')).toBe(false)
    expect(isIsoDate('2026-11-27')).toBe(true)
    expect(addDaysIso('2026-12-31', 1)).toBe('2027-01-01')
    expect(addDaysIso('2026-11-27', -29)).toBe('2026-10-29')
  })

  it('treats zero as up', () => {
    expect(deltaTrend(0)).toBe('up')
    expect(deltaTrend(-0.5)).toBe('down')
  })
})

describe('chartGeometry', () => {
  it('rounds the axis up to a nice number', () => {
    expect(niceMax(0)).toBe(1)
    expect(niceMax(73)).toBe(100)
    expect(niceMax(180)).toBe(200)
    expect(niceMax(4)).toBe(5)
  })

  it('anchors bars at the baseline and scales to the maximum', () => {
    const geometry = chartGeometry([point('2026-11-01', 50, 20), point('2026-11-02', 100, 60)], 200, 100)

    expect(geometry.hasData).toBe(true)
    expect(geometry.max).toBe(100)
    expect(geometry.bars[1]!.y).toBeCloseTo(0)
    expect(geometry.bars[1]!.height).toBeCloseTo(100)
    expect(geometry.bars[0]!.height).toBeCloseTo(50)
    expect(geometry.line[1]!.y).toBeCloseTo(40)
  })

  it('reports no data for an empty period and keeps a negative profit on the chart', () => {
    expect(chartGeometry([point('2026-11-01', 0, 0)], 200, 100).hasData).toBe(false)
    const negative = chartGeometry([point('2026-11-01', 100, -20)], 200, 100)
    expect(negative.line[0]!.y).toBeGreaterThan(negative.bars[0]!.y + negative.bars[0]!.height)
  })
})

describe('ranking helpers', () => {
  const item = { rank: 1, key: 'a', productId: null, variantId: null, label: 'A', sku: null, units: 3, revenue: 30, profit: 20, marginPercent: 66.7, unitsSharePercent: 60, revenueSharePercent: 12.4, profitSharePercent: null }

  it('picks the share that belongs to the sort order', () => {
    expect(rankingShare(item, 'units')).toBe(60)
    expect(rankingShare(item, 'revenue')).toBe(12.4)
    expect(rankingShare(item, 'profit')).toBeNull()
  })

  it('keeps a visible sliver for small shares and caps at the full width', () => {
    expect(shareBarWidth(null)).toBe(0)
    expect(shareBarWidth(0.2)).toBe(2)
    expect(shareBarWidth(55.6)).toBe(56)
    expect(shareBarWidth(140)).toBe(100)
  })

  it('validates the sort option', () => {
    expect(isRankingSort('profit')).toBe(true)
    expect(isRankingSort('name')).toBe(false)
  })
})
