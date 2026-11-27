import { describe, expect, it } from 'vitest'
import { analyticsQuery, barScale, changePercent, componentColor, heatLevel, hourLabel, weekdayName } from '../utils/analytics'

describe('analyticsQuery', () => {
  const filter = { period: 'month' as const, channel: 'pos' as const, from: '2026-01-01', to: '2026-01-31', productId: null }

  it('adds the report filter and the extra parameters', () => {
    const params = new URLSearchParams(analyticsQuery(filter, { hoursPerDay: 12, deadDays: 90 }))

    expect(params.get('period')).toBe('month')
    expect(params.get('channel')).toBe('pos')
    expect(params.get('hoursPerDay')).toBe('12')
    expect(params.get('deadDays')).toBe('90')
  })

  it('leaves out empty extras and sends custom ranges', () => {
    const params = new URLSearchParams(analyticsQuery({ ...filter, period: 'custom', channel: 'all' }, { deadDays: null, x: '' }))

    expect(params.get('from')).toBe('2026-01-01')
    expect(params.has('deadDays')).toBe(false)
    expect(params.has('x')).toBe(false)
    expect(params.has('channel')).toBe(false)
  })
})

describe('barScale', () => {
  it('scales positive values against the largest one', () => {
    const { bars } = barScale([50, 100, 0])

    expect(bars[0]).toEqual({ x: 0, width: 50 })
    expect(bars[1]?.width).toBe(100)
    expect(bars[2]?.width).toBe(0)
  })

  it('draws negative values left of the zero line', () => {
    const { bars, zero } = barScale([60, -20])

    expect(zero).toBe(25)
    expect(bars[0]).toEqual({ x: 25, width: 75 })
    expect(bars[1]?.x).toBe(0)
    expect(bars[1]?.width).toBe(25)
  })

  it('keeps tiny values visible and handles all zero', () => {
    expect(barScale([1000, 1]).bars[1]?.width).toBe(1.5)
    expect(barScale([0, 0]).bars.every(bar => bar.width === 0)).toBe(true)
    expect(barScale([]).bars).toEqual([])
  })
})

describe('chart helpers', () => {
  it('maps heat intensity with a visible floor', () => {
    expect(heatLevel(0, 100)).toBe(0)
    expect(heatLevel(1, 100)).toBe(0.15)
    expect(heatLevel(100, 100)).toBe(1)
    expect(heatLevel(5, 0)).toBe(0)
  })

  it('formats hours and weekdays', () => {
    expect(hourLabel(7)).toBe('07:00')
    expect(weekdayName(1, 'en')).toBe('Mon')
    expect(weekdayName(7, 'en')).toBe('Sun')
  })

  it('computes percentage change and avoids dividing by zero', () => {
    expect(changePercent(150, 100)).toBe(50)
    expect(changePercent(50, 100)).toBe(-50)
    expect(changePercent(10, 0)).toBeNull()
  })

  it('colours cost components in a fixed order and mutes the unitemised remainder', () => {
    expect(componentColor('material')).toBe('var(--chart-1)')
    expect(componentColor('extra')).toBe('var(--chart-6)')
    expect(componentColor('unallocated')).toBe('var(--chart-muted)')
    expect(componentColor('unknown')).toBe('var(--chart-muted)')
  })
})
