import { describe, expect, it } from 'vitest'
import {
  emptyBomLineForm,
  emptyProductForm,
  emptyVariantForm,
  marginTone,
  priceRange,
  validateBomLineForm,
  validateProductForm,
  validateVariantForm,
} from '../utils/products'

describe('validateProductForm', () => {
  it('requires a name and trims the optional fields', () => {
    expect(validateProductForm(emptyProductForm()).errors).toEqual({ name: 'required' })
    const { request } = validateProductForm({ name: ' Vase ', sku: ' V-1 ', description: '' })
    expect(request).toEqual({ name: 'Vase', sku: 'V-1', description: null })
  })

  it('rejects values that are too long', () => {
    const { errors } = validateProductForm({ name: 'x'.repeat(101), sku: 'y'.repeat(41), description: 'z'.repeat(2001) })
    expect(errors).toEqual({ name: 'length', sku: 'length', description: 'length' })
  })
})

describe('validateVariantForm', () => {
  it('accepts a decimal comma and defaults', () => {
    const { request } = validateVariantForm({ ...emptyVariantForm(), name: 'Large', price: '12,50' })
    expect(request).toEqual({ name: 'Large', sku: null, price: 12.5, targetMarginPercent: 40, feePercent: 0 })
  })

  it('flags price, margin and fee problems', () => {
    const { errors } = validateVariantForm({ name: '', sku: '', price: 'abc', targetMargin: '100', fee: '-1' })
    expect(errors).toEqual({ name: 'required', price: 'number', targetMargin: 'range', fee: 'number' })
  })

  it('keeps margin plus fee below 100 percent', () => {
    const { errors } = validateVariantForm({ name: 'A', sku: '', price: '1', targetMargin: '60', fee: '40' })
    expect(errors.targetMargin).toBe('range')
  })
})

describe('validateBomLineForm', () => {
  it('validates a filament line by spool or by material', () => {
    const empty = validateBomLineForm(emptyBomLineForm('filament'))
    expect(empty.errors).toEqual({ spoolId: 'required', grams: 'number' })

    const bySpool = validateBomLineForm({ ...emptyBomLineForm('filament'), spoolId: 's1', grams: '12,5' })
    expect(bySpool.request).toMatchObject({ kind: 'filament', spoolId: 's1', materialId: null, grams: 12.5 })

    const byMaterial = validateBomLineForm({ ...emptyBomLineForm('filament'), source: 'material', materialId: 'm1', spoolId: 'ignored', grams: '5' })
    expect(byMaterial.request).toMatchObject({ materialId: 'm1', spoolId: null })
    expect(validateBomLineForm({ ...emptyBomLineForm('filament'), source: 'material', grams: '0' }).errors).toEqual({ materialId: 'required', grams: 'range' })
  })

  it('validates a print line with an optional model', () => {
    expect(validateBomLineForm(emptyBomLineForm('print')).errors).toEqual({ printerId: 'required', minutes: 'number' })
    expect(validateBomLineForm({ ...emptyBomLineForm('print'), printerId: 'p1', minutes: '1,5' }).errors).toEqual({ minutes: 'integer' })
    const { request } = validateBomLineForm({ ...emptyBomLineForm('print'), printerId: 'p1', minutes: '90', modelId: 'model-1' })
    expect(request).toMatchObject({ printerId: 'p1', minutes: 90, modelId: 'model-1' })
  })

  it('validates labour minutes, packaging and extras', () => {
    expect(validateBomLineForm({ ...emptyBomLineForm('labour'), minutes: '15' }).request).toMatchObject({ kind: 'labour', minutes: 15 })
    expect(validateBomLineForm({ ...emptyBomLineForm('packaging'), amount: '0,5' }).request).toMatchObject({ amount: 0.5, label: null })
    expect(validateBomLineForm({ ...emptyBomLineForm('extra'), amount: '1' }).errors).toEqual({ label: 'required' })
    expect(validateBomLineForm({ ...emptyBomLineForm('extra'), label: 'Magnet', amount: '0,25' }).request).toMatchObject({ label: 'Magnet', amount: 0.25 })
  })
})

describe('marginTone', () => {
  it('maps margins to a tone', () => {
    expect(marginTone(null)).toBe('neutral')
    expect(marginTone(-5)).toBe('danger')
    expect(marginTone(10)).toBe('warning')
    expect(marginTone(35)).toBe('success')
  })
})

describe('priceRange', () => {
  it('is empty without variants', () => {
    expect(priceRange({ lowestPrice: null, highestPrice: null })).toBeNull()
    expect(priceRange({ lowestPrice: 5, highestPrice: 9 })).toEqual({ low: 5, high: 9 })
  })
})
