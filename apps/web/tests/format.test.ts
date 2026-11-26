import { describe, expect, it } from 'vitest'
import { formatCurrency } from '../utils/format'

describe('formatCurrency', () => {
  it('formats euro amounts for the Belgian Dutch locale', () => {
    expect(formatCurrency(12840, 'nl-BE')).toContain('12.840,00')
  })

  it('formats euro amounts for the English locale', () => {
    expect(formatCurrency(12840, 'en-GB')).toContain('12,840.00')
  })
})
