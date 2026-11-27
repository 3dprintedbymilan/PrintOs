import { describe, expect, it } from 'vitest'
import { newMaterialValue, newSupplierValue } from '../utils/inventory'
import { changeTone, emptyLine, groupBySupplier, purchaseTotals, remainingToReceive, sparkline, purchaseStatusTone, validatePurchaseForm } from '../utils/purchases'
import type { MaterialPriceHistory, PurchaseFormValues } from '../utils/purchases'

function form(overrides: Partial<PurchaseFormValues> = {}): PurchaseFormValues {
  return {
    supplierId: 's1',
    supplierName: '',
    purchasedOn: '2026-11-01',
    invoiceReference: ' INV-9 ',
    notes: '',
    shippingCost: '4,50',
    discount: '',
    receiveNow: true,
    lines: [{ ...emptyLine(1, 'm1'), colorName: ' Jade ', colorHex: '#F4F4F0', quantity: '2', weight: '1000', unitPrice: '19,99' }],
    ...overrides,
  }
}

describe('validatePurchaseForm', () => {
  it('accepts decimal commas and trims values', () => {
    const { purchase, errors } = validatePurchaseForm(form(), '2026-11-27')

    expect(errors).toEqual({})
    expect(purchase?.request).toMatchObject({ invoiceReference: 'INV-9', shippingCost: 4.5, discount: 0, receiveImmediately: true })
    expect(purchase?.request.lines[0]).toMatchObject({ materialId: 'm1', colorName: 'Jade', quantity: 2, unitPrice: 19.99 })
  })

  it('reports each invalid field', () => {
    const lines = [{ ...emptyLine(1, newMaterialValue), quantity: '0', weight: '1.5', unitPrice: 'abc', colorHex: 'red' }]
    const { purchase, errors } = validatePurchaseForm(form({ supplierId: newSupplierValue, purchasedOn: '2999-01-01', shippingCost: 'x', lines }), '2026-11-27')

    expect(purchase).toBeNull()
    expect(errors).toMatchObject({ supplierName: 'required', purchasedOn: 'date', shippingCost: 'number' })
    expect(errors.lineErrors?.[1]).toEqual({
      materialName: 'required', materialType: 'required', colorName: 'required', colorHex: 'hex', quantity: 'range', weight: 'integer', unitPrice: 'number',
    })
  })

  it('requires at least one line and rejects a discount above the total', () => {
    expect(validatePurchaseForm(form({ lines: [] }), '2026-11-27').errors.lines).toBe('required')
    expect(validatePurchaseForm(form({ discount: '500' }), '2026-11-27').errors.discount).toBe('negativeTotal')
  })

  it('collects new materials for creation first', () => {
    const lines = [{ ...emptyLine(7, newMaterialValue), materialName: 'PETG', materialType: 'PETG', colorName: 'Black', unitPrice: '15' }]
    const { purchase } = validatePurchaseForm(form({ lines }), '2026-11-27')

    expect(purchase?.newMaterials).toEqual([{ key: 7, name: 'PETG', type: 'PETG' }])
    expect(purchase?.request.lines[0]?.materialId).toBeNull()
  })
})

describe('purchaseTotals', () => {
  it('adds shipping and subtracts discount', () => {
    expect(purchaseTotals(form({ discount: '1' }))).toEqual({ subtotal: 39.98, total: 43.48 })
  })
})

describe('helpers', () => {
  it('maps statuses to tones', () => {
    expect(purchaseStatusTone('Received')).toBe('success')
    expect(purchaseStatusTone('PartiallyReceived')).toBe('warning')
    expect(purchaseStatusTone('Cancelled')).toBe('neutral')
    expect(purchaseStatusTone('Ordered')).toBe('info')
  })

  it('counts spools still to receive', () => {
    expect(remainingToReceive({ lines: [{ quantity: 5, receivedQuantity: 2 }, { quantity: 1, receivedQuantity: 1 }] as never })).toBe(3)
  })

  it('classifies price changes', () => {
    expect(changeTone(null)).toBe('flat')
    expect(changeTone(0)).toBe('flat')
    expect(changeTone(3)).toBe('up')
    expect(changeTone(-3)).toBe('down')
  })

  it('draws a sparkline from low to high', () => {
    const { path, last } = sparkline([{ costPerKg: 10 }, { costPerKg: 20 }], 100, 50, 0)

    expect(path).toBe('M0 50 L100 0')
    expect(last).toEqual({ x: 100, y: 0 })
    expect(sparkline([], 100, 50)).toEqual({ path: '', last: null })
    expect(sparkline([{ costPerKg: 5 }], 100, 50).last).toEqual({ x: 50, y: 25 })
  })

  it('groups price rows per supplier', () => {
    const price = (supplierId: string, supplierName: string) => ({ supplierId, supplierName, lastCostPerKg: 20, lastPurchasedOn: '2026-11-01', previousCostPerKg: null, changePercent: null, purchaseCount: 1, isCheapest: true })
    const histories = [
      { materialId: 'm2', materialName: 'PETG', currency: 'EUR', suppliers: [price('b', 'Bravo')] },
      { materialId: 'm1', materialName: 'PLA', currency: 'EUR', suppliers: [price('b', 'Bravo'), price('a', 'Alpha')] },
    ] as unknown as MaterialPriceHistory[]

    const groups = groupBySupplier(histories)

    expect(groups.map(group => group.supplierName)).toEqual(['Alpha', 'Bravo'])
    expect(groups[1]?.rows.map(row => row.materialName)).toEqual(['PETG', 'PLA'])
  })
})
