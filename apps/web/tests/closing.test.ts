import { describe, expect, it } from 'vitest'
import { cashDifference, closingDays, expectedCash, validateClosingForm } from '../utils/marketClosing'

describe('closing helpers', () => {
  it('computes expected cash and the difference like the API', () => {
    expect(expectedCash(50, 40)).toBe(90)
    expect(cashDifference(85, 90)).toBe(-5)
    expect(cashDifference(92.5, 90)).toBe(2.5)
    expect(expectedCash(0.1, 0.2)).toBe(0.3)
  })

  it('validates the closing form with a decimal comma', () => {
    expect(validateClosingForm({ openingFloat: '', countedCash: '85,50', notes: ' ok ' })).toEqual({ values: { openingFloat: 0, countedCash: 85.5, notes: 'ok' }, errors: {} })
    expect(validateClosingForm({ openingFloat: 'x', countedCash: '', notes: '' }).errors).toEqual({ openingFloat: 'number', countedCash: 'required' })
  })

  it('lists days with sales, closed days and today, newest first', () => {
    const days = closingDays(
      [{ date: '2026-12-05', saleCount: 3, revenue: 60 }, { date: '2026-12-06', saleCount: 1, revenue: 10 }],
      [{ date: '2026-12-05', saleCount: 3, revenue: 60 }],
      '2026-12-07',
    )

    expect(days.map(day => [day.date, day.closed])).toEqual([['2026-12-07', false], ['2026-12-06', false], ['2026-12-05', true]])
  })
})
