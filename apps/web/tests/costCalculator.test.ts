import { describe, expect, it } from 'vitest'
import { buildCostRequest, defaultSnapshotName, emptyCostForm, lineShare, profitMarginPercent } from '../utils/costCalculator'
import type { CostBreakdown, CostFormValues } from '../utils/costCalculator'

function valid(overrides: Partial<CostFormValues> = {}): CostFormValues {
  return { ...emptyCostForm(), printerId: 'p1', spoolId: 's1', grams: '100', printHours: '2', ...overrides }
}

describe('buildCostRequest', () => {
  it('turns a valid form into an API request', () => {
    const { request, errors } = buildCostRequest(valid({ printMinutes: '30', labourMinutes: '15', packagingCost: '0,50', feePercent: '10' }))

    expect(errors).toEqual({})
    expect(request).toEqual({
      printerId: 'p1',
      spoolId: 's1',
      grams: 100,
      printMinutes: 150,
      labourMinutes: 15,
      packagingCost: 0.5,
      extraCosts: [],
      marginPercent: 40,
      feePercent: 10,
    })
  })

  it('accepts decimal commas for grams', () => {
    expect(buildCostRequest(valid({ grams: '12,5' })).request?.grams).toBe(12.5)
  })

  it('requires printer, spool and grams', () => {
    const { request, errors } = buildCostRequest(emptyCostForm())

    expect(request).toBeNull()
    expect(errors.printerId).toBe('required')
    expect(errors.spoolId).toBe('required')
    expect(errors.grams).toBe('required')
    expect(errors.printTime).toBe('positive')
  })

  it('rejects zero grams and non numeric input', () => {
    expect(buildCostRequest(valid({ grams: '0' })).errors.grams).toBe('positive')
    expect(buildCostRequest(valid({ grams: 'abc' })).errors.grams).toBe('number')
    expect(buildCostRequest(valid({ grams: '999999' })).errors.grams).toBe('range')
  })

  it('requires whole numbers for print time and labour minutes', () => {
    expect(buildCostRequest(valid({ printMinutes: '1,5' })).errors.printTime).toBe('number')
    expect(buildCostRequest(valid({ labourMinutes: '2,5' })).errors.labourMinutes).toBe('number')
  })

  it('rejects margins of 100 percent or more', () => {
    expect(buildCostRequest(valid({ marginPercent: '100' })).errors.marginPercent).toBe('margin')
    expect(buildCostRequest(valid({ marginPercent: '99,9' })).errors.marginPercent).toBeUndefined()
  })

  it('rejects margin plus fees of 100 percent or more', () => {
    expect(buildCostRequest(valid({ marginPercent: '60', feePercent: '40' })).errors.marginPercent).toBe('marginPlusFee')
    expect(buildCostRequest(valid({ feePercent: '100' })).errors.feePercent).toBe('margin')
  })

  it('validates each extra cost row', () => {
    const { request, errors } = buildCostRequest(valid({ extras: [{ label: '', amount: 'x' }, { label: 'Magnet', amount: '0,25' }] }))

    expect(request).toBeNull()
    expect(errors.extraLabel0).toBe('required')
    expect(errors.extraAmount0).toBe('number')
    expect(errors.extraLabel1).toBeUndefined()
  })

  it('keeps valid extras in order', () => {
    const { request } = buildCostRequest(valid({ extras: [{ label: ' Magnet ', amount: '0,25' }] }))

    expect(request?.extraCosts).toEqual([{ label: 'Magnet', amount: 0.25 }])
  })
})

describe('breakdown helpers', () => {
  const breakdown: CostBreakdown = {
    currency: 'EUR',
    printerName: 'A1',
    materialLabel: 'PLA',
    lines: [
      { kind: 'material', label: 'material', quantity: 105, unit: 'g', unitCost: 0.02, amount: 2.1 },
      { kind: 'labour', label: 'labour', quantity: 0.25, unit: 'h', unitCost: 20, amount: 5 },
    ],
    totalCost: 7.1,
    marginPercent: 40,
    feePercent: 0,
    suggestedPrice: 11.83,
    feeAmount: 0,
    profit: 4.73,
  }

  it('computes the realised profit margin', () => {
    expect(profitMarginPercent(breakdown)).toBe(40)
    expect(profitMarginPercent({ ...breakdown, suggestedPrice: 0 })).toBe(0)
  })

  it('computes each line share of the total cost', () => {
    expect(lineShare(breakdown.lines[0]!, breakdown)).toBe(30)
    expect(lineShare(breakdown.lines[0]!, { ...breakdown, totalCost: 0 })).toBe(0)
  })

  it('builds a readable default snapshot name', () => {
    expect(defaultSnapshotName('A1', 100, new Date('2026-11-26T10:00:00Z'))).toBe('A1 100 g 2026-11-26')
  })
})
