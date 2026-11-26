import { describe, expect, it } from 'vitest'
import { filterSpools, newMaterialValue, remainingPercent, summarizeStock, validateAdjustment, validateSpoolForm, validateThresholds } from '../utils/inventory'
import type { Spool } from '../utils/inventory'

function spool(overrides: Partial<Spool>): Spool {
  return {
    id: 'a',
    materialId: 'pla',
    materialName: 'PLA Basic',
    supplierId: null,
    supplierName: null,
    colorName: 'Jade White',
    colorHex: null,
    cost: 20,
    costPerGram: 0.02,
    initialWeightGrams: 1000,
    remainingWeightGrams: 500,
    isArchived: false,
    ...overrides,
  }
}

describe('summarizeStock', () => {
  it('totals kilograms, materials and spools and ignores archived spools', () => {
    const summary = summarizeStock([
      spool({ id: '1', remainingWeightGrams: 750 }),
      spool({ id: '2', materialId: 'petg', remainingWeightGrams: 1000 }),
      spool({ id: '3', remainingWeightGrams: 900, isArchived: true }),
    ])
    expect(summary).toEqual({ totalKilograms: 1.75, materialCount: 2, spoolCount: 2 })
  })
})

describe('filterSpools', () => {
  const spools = [spool({ id: '1' }), spool({ id: '2', materialId: 'petg', materialName: 'PETG HF', colorName: 'Black', supplierName: 'Local Shop' })]

  it('filters by material', () => {
    expect(filterSpools(spools, 'petg', '').map(item => item.id)).toEqual(['2'])
  })

  it('searches colour, material and supplier, ignoring case', () => {
    expect(filterSpools(spools, '', 'jade').map(item => item.id)).toEqual(['1'])
    expect(filterSpools(spools, '', 'LOCAL').map(item => item.id)).toEqual(['2'])
  })
})

describe('remainingPercent', () => {
  it('rounds the share left', () => {
    expect(remainingPercent(spool({ remainingWeightGrams: 333 }))).toBe(33)
  })
})

describe('validateSpoolForm', () => {
  const base = { materialId: 'pla', materialName: '', materialType: '', supplierId: '', supplierName: '', colorName: ' Jade White ', colorHex: '', cost: '19,99', weight: '1000' }

  it('accepts a valid spool with an existing material and no supplier', () => {
    const { spool: result } = validateSpoolForm(base)
    expect(result).toMatchObject({ materialId: 'pla', supplierId: null, colorName: 'Jade White', colorHex: null, cost: 19.99, initialWeightGrams: 1000 })
  })

  it('requires the new material fields only when creating a material', () => {
    const { errors } = validateSpoolForm({ ...base, materialId: newMaterialValue })
    expect(errors).toEqual({ materialName: 'required', materialType: 'required' })
  })

  it('reports colour, cost and weight problems', () => {
    const { errors } = validateSpoolForm({ ...base, colorName: '', colorHex: 'red', cost: 'x', weight: '0' })
    expect(errors).toEqual({ colorName: 'required', colorHex: 'hex', cost: 'number', weight: 'range' })
  })
})

describe('validateAdjustment', () => {
  it('turns a removal into a negative delta', () => {
    expect(validateAdjustment('remove', '150', ' Failed print ').adjustment).toEqual({ deltaGrams: -150, reason: 'Failed print' })
  })

  it('requires a whole positive amount and a reason', () => {
    expect(validateAdjustment('add', '1.5', '').errors).toEqual({ amount: 'amount', reason: 'reason' })
  })
})

describe('validateThresholds', () => {
  it('treats empty fields as switched off', () => {
    expect(validateThresholds('', ' ').thresholds).toEqual({ lowStockGrams: null, reorderGrams: null })
  })

  it('parses whole grams', () => {
    expect(validateThresholds('200', '500').thresholds).toEqual({ lowStockGrams: 200, reorderGrams: 500 })
  })

  it('rejects non-numbers and a reorder level below the low-stock level', () => {
    expect(validateThresholds('abc', '').errors).toEqual({ low: 'number' })
    expect(validateThresholds('500', '200').errors).toEqual({ reorder: 'order' })
  })
})
