import { describe, expect, it } from 'vitest'
import { availabilityTone, filterFinishedStock, signedUnits, validateStockAdjustment, validateThreshold } from '../utils/finishedStock'
import type { FinishedStockItem } from '../utils/finishedStock'

const item = (overrides: Partial<FinishedStockItem> = {}): FinishedStockItem => ({
  variantId: 'v1',
  productId: 'p1',
  productName: 'Vase',
  variantName: 'Large',
  sku: 'VASE-L',
  productArchived: false,
  onHand: 5,
  reserved: 2,
  available: 3,
  lowStockThreshold: null,
  isLow: false,
  averageUnitCost: null,
  stockValue: null,
  updatedAt: null,
  ...overrides,
})

describe('availabilityTone', () => {
  it('flags a shortage as danger, an empty shelf as neutral and low stock as warning', () => {
    expect(availabilityTone(item({ available: -1 }))).toBe('danger')
    expect(availabilityTone(item({ onHand: 0, available: 0 }))).toBe('neutral')
    expect(availabilityTone(item({ isLow: true }))).toBe('warning')
    expect(availabilityTone(item())).toBe('success')
  })
})

describe('filterFinishedStock', () => {
  const items = [item(), item({ variantId: 'v2', productName: 'Lamp', variantName: 'Small', sku: null, isLow: true })]

  it('searches product, variant and sku', () => {
    expect(filterFinishedStock(items, 'vase-l', false).map(i => i.variantId)).toEqual(['v1'])
    expect(filterFinishedStock(items, 'SMALL', false).map(i => i.variantId)).toEqual(['v2'])
  })

  it('can show only low stock', () => {
    expect(filterFinishedStock(items, '', true).map(i => i.variantId)).toEqual(['v2'])
  })
})

describe('validateStockAdjustment', () => {
  it('requires whole units and a reason', () => {
    const result = validateStockAdjustment('add', '1,5', ' ')
    expect(result.adjustment).toBeNull()
    expect(result.errors).toEqual({ amount: 'amount', reason: 'reason' })
  })

  it('signs the delta by direction and keeps the override flag', () => {
    expect(validateStockAdjustment('remove', '3', ' Broken ', true).adjustment).toEqual({ delta: -3, reason: 'Broken', allowBelowReserved: true })
    expect(validateStockAdjustment('add', '4', 'Counted').adjustment).toEqual({ delta: 4, reason: 'Counted', allowBelowReserved: false })
  })
})

describe('validateThreshold', () => {
  it('accepts empty as no warning and rejects negatives and decimals', () => {
    expect(validateThreshold('')).toEqual({ threshold: null, valid: true })
    expect(validateThreshold('3')).toEqual({ threshold: 3, valid: true })
    expect(validateThreshold('-1').valid).toBe(false)
    expect(validateThreshold('1,5').valid).toBe(false)
  })
})

describe('signedUnits', () => {
  it('prefixes positive numbers', () => {
    expect(signedUnits(2)).toBe('+2')
    expect(signedUnits(-2)).toBe('-2')
    expect(signedUnits(0)).toBe('0')
  })
})
