import { describe, expect, it } from 'vitest'
import { averageSale, emptyMarketForm, formatDateRange, localDateKey, marketToForm, serverMarketErrors, validateMarketForm } from '../utils/markets'
import type { Market } from '../utils/markets'

describe('validateMarketForm', () => {
  it('builds a request with a decimal comma for the stand cost', () => {
    const { request, errors } = validateMarketForm({ ...emptyMarketForm(new Date(2026, 11, 5)), name: ' Kerstmarkt ', location: 'Gent', standCost: '45,5', endDate: '2026-12-06' })

    expect(errors).toEqual({})
    expect(request).toEqual({ name: 'Kerstmarkt', location: 'Gent', startDate: '2026-12-05', endDate: '2026-12-06', standCost: 45.5, notes: null })
  })

  it('requires a name and a start date and checks the date order and the amount', () => {
    const { request, errors } = validateMarketForm({ name: ' ', location: '', startDate: '', endDate: '', standCost: 'abc', notes: '' })

    expect(request).toBeNull()
    expect(errors).toEqual({ name: 'required', startDate: 'required', standCost: 'number' })
    expect(validateMarketForm({ ...emptyMarketForm(), name: 'x', startDate: '2026-05-02', endDate: '2026-05-01' }).errors).toEqual({ endDate: 'order' })
  })

  it('maps server field errors', () => {
    expect(serverMarketErrors({ name: ['x'], endDate: ['y'], standCost: ['z'] })).toEqual({ name: 'required', endDate: 'order', standCost: 'number' })
  })
})

describe('helpers', () => {
  it('uses the local calendar day as key', () => {
    expect(localDateKey(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05')
  })

  it('formats a single day or a range', () => {
    const format = (value: string) => `<${value}>`

    expect(formatDateRange('2026-12-05', null, format)).toBe('<2026-12-05>')
    expect(formatDateRange('2026-12-05', '2026-12-05', format)).toBe('<2026-12-05>')
    expect(formatDateRange('2026-12-05', '2026-12-06', format)).toBe('<2026-12-05> - <2026-12-06>')
  })

  it('averages a sale without dividing by zero', () => {
    expect(averageSale(100, 3)).toBe(33.33)
    expect(averageSale(0, 0)).toBe(0)
  })

  it('round-trips a market into the form', () => {
    const market = { name: 'M', location: null, startDate: '2026-12-05', endDate: null, standCost: 45.5, notes: null } as Market

    expect(marketToForm(market)).toEqual({ name: 'M', location: '', startDate: '2026-12-05', endDate: '', standCost: '45,5', notes: '' })
  })
})
