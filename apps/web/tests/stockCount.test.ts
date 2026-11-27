import { describe, expect, it } from 'vitest'
import { changedRows, countDelta, filterReservations, remainingFromGross, rowState, signedNumber, validateWeigh } from '../utils/stockCount'
import type { ReservationOverviewItem } from '../utils/stockCount'

describe('weighing', () => {
  it('subtracts the empty spool', () => {
    expect(remainingFromGross(950, 250)).toBe(700)
    expect(countDelta(1000, 700)).toBe(-300)
  })

  it('accepts a decimal comma only for whole grams', () => {
    expect(validateWeigh({ gross: '950', empty: '250', reason: ' ' }, null).payload).toEqual({ grossWeightGrams: 950, emptySpoolGrams: 250 })
    expect(validateWeigh({ gross: '950,5', empty: '250', reason: '' }, null).errors.gross).toBe('gross')
  })

  it('falls back to the remembered empty weight', () => {
    expect(validateWeigh({ gross: '900', empty: '', reason: 'Month end' }, 240).payload).toEqual({ grossWeightGrams: 900, emptySpoolGrams: 240, reason: 'Month end' })
    expect(validateWeigh({ gross: '900', empty: '', reason: '' }, null).errors.empty).toBe('empty')
  })

  it('rejects a weight below the empty spool and out-of-range values', () => {
    expect(validateWeigh({ gross: '100', empty: '250', reason: '' }, null).errors.gross).toBe('lighter')
    expect(validateWeigh({ gross: '0', empty: '250', reason: '' }, null).errors.gross).toBe('gross')
    expect(validateWeigh({ gross: '500', empty: '3000', reason: '' }, null).errors.empty).toBe('empty')
  })
})

describe('stock count rows', () => {
  it('classifies rows', () => {
    expect(rowState({ id: 'a', current: 5, input: '' }, 100)).toEqual({ kind: 'skip' })
    expect(rowState({ id: 'a', current: 5, input: '5' }, 100)).toEqual({ kind: 'same' })
    expect(rowState({ id: 'a', current: 5, input: '3' }, 100)).toEqual({ kind: 'change', counted: 3, delta: -2 })
    expect(rowState({ id: 'a', current: 5, input: '-1' }, 100)).toEqual({ kind: 'invalid' })
    expect(rowState({ id: 'a', current: 5, input: '101' }, 100)).toEqual({ kind: 'invalid' })
    expect(rowState({ id: 'a', current: 5, input: 'abc' }, 100)).toEqual({ kind: 'invalid' })
  })

  it('collects only changed rows and reports invalid ones', () => {
    const result = changedRows([
      { id: 'a', current: 5, input: '7' },
      { id: 'b', current: 5, input: '5' },
      { id: 'c', current: 5, input: '' },
      { id: 'd', current: 5, input: 'x' },
    ], 100)
    expect(result.lines).toEqual([{ id: 'a', counted: 7 }])
    expect(result.invalidIds).toEqual(['d'])
  })

  it('signs numbers', () => {
    expect(signedNumber(3)).toBe('+3')
    expect(signedNumber(-3)).toBe('-3')
    expect(signedNumber(0)).toBe('0')
  })
})

describe('reservation filter', () => {
  const item = (overrides: Partial<ReservationOverviewItem>): ReservationOverviewItem => ({
    orderId: 'o1', orderNumber: 12, orderStatus: 'Confirmed', customerName: 'Anna', dueDate: null, orderLineId: 'l1', variantId: 'v1',
    productName: 'Vase', variantName: 'Large', sku: 'VASE-L', quantity: 2, onHand: 5, reserved: 2, available: 3, isShort: false, ...overrides,
  })

  it('searches by product, sku, customer and order number and filters shortages', () => {
    const items = [item({}), item({ orderLineId: 'l2', orderNumber: 13, customerName: 'Bram', productName: 'Lamp', sku: null, isShort: true })]
    expect(filterReservations(items, 'anna', false)).toHaveLength(1)
    expect(filterReservations(items, '#13', false)).toHaveLength(1)
    expect(filterReservations(items, 'vase-l', false)).toHaveLength(1)
    expect(filterReservations(items, '', true).map(row => row.orderNumber)).toEqual([13])
  })
})
