import { describe, expect, it } from 'vitest'
import {
  emptyLineForm,
  emptyOrderForm,
  orderedTransitions,
  orderErrorMessages,
  previewTotals,
  progressIndex,
  serverOrderErrors,
  transitionKind,
  transitionLabelKey,
  validateOrderForm,
} from '../utils/orders'

function variantLine(overrides: Partial<ReturnType<typeof emptyLineForm>> = {}) {
  return { ...emptyLineForm(), variantId: 'v1', quantity: '2', ...overrides }
}

describe('validateOrderForm', () => {
  it('builds a request with a decimal comma and optional fields', () => {
    const form = {
      ...emptyOrderForm(),
      customerId: 'c1',
      dueDate: '2027-01-05',
      discountAmount: '2,5',
      lines: [variantLine({ unitPrice: '18,5', discountPercent: '10' })],
    }

    const { request, errors } = validateOrderForm(form)

    expect(errors).toEqual({})
    expect(request).toMatchObject({
      customerId: 'c1',
      channel: 'direct',
      dueDate: '2027-01-05',
      discountAmount: 2.5,
      notes: null,
      acknowledgeRisk: false,
    })
    expect(request?.lines?.[0]).toEqual({ id: null, variantId: 'v1', description: null, quantity: 2, unitPrice: 18.5, unitCost: null, discountPercent: 10 })
  })

  it('lets a variant line use the list price when the price is empty', () => {
    const { request } = validateOrderForm({ ...emptyOrderForm(), lines: [variantLine()] })

    expect(request?.lines?.[0]?.unitPrice).toBeNull()
  })

  it('requires description and price for free-text lines', () => {
    const { request, errors } = validateOrderForm({ ...emptyOrderForm(), lines: [{ ...emptyLineForm(), quantity: '1' }] })

    expect(request).toBeNull()
    expect(errors).toEqual({ 'lines.0.description': 'required', 'lines.0.unitPrice': 'required' })
  })

  it('flags bad quantities, percentages and discounts', () => {
    const { errors } = validateOrderForm({
      ...emptyOrderForm(),
      discountAmount: 'abc',
      lines: [variantLine({ quantity: '0', discountPercent: '120' }), variantLine({ quantity: '1,5' })],
    })

    expect(errors).toEqual({
      'lines.0.quantity': 'range',
      'lines.0.discountPercent': 'range',
      'lines.1.quantity': 'number',
      discountAmount: 'number',
    })
  })

  it('requires acknowledgement for uncleared rights and reports it in the request once ticked', () => {
    const blocked = validateOrderForm({ ...emptyOrderForm(), lines: [variantLine({ rights: 'NotCleared' })] })
    const accepted = validateOrderForm({ ...emptyOrderForm(), lines: [variantLine({ rights: 'NotCleared', acknowledged: true })] })

    expect(blocked.errors).toEqual({ 'lines.0.risk': 'risk' })
    expect(accepted.request?.acknowledgeRisk).toBe(true)
  })
})

describe('previewTotals', () => {
  it('uses list prices, line discounts and the order discount', () => {
    const form = {
      ...emptyOrderForm(),
      discountAmount: '2',
      lines: [variantLine({ quantity: '3', discountPercent: '10' }), { ...emptyLineForm(), quantity: '1', unitPrice: '9,99' }],
    }

    expect(previewTotals(form, new Map([['v1', 10]]))).toEqual({ subtotal: 36.99, discount: 2, total: 34.99 })
  })

  it('never lets the discount exceed the subtotal', () => {
    const form = { ...emptyOrderForm(), discountAmount: '500', lines: [variantLine()] }

    expect(previewTotals(form, new Map([['v1', 5]]))).toEqual({ subtotal: 10, discount: 10, total: 0 })
  })
})

describe('status helpers', () => {
  it('classifies transitions and labels them', () => {
    expect(transitionKind('Draft', 'Confirmed')).toBe('forward')
    expect(transitionKind('Ready', 'InProduction')).toBe('back')
    expect(transitionKind('Ready', 'Cancelled')).toBe('cancel')
    expect(transitionKind('Cancelled', 'Draft')).toBe('reopen')
    expect(transitionLabelKey('Ready', 'InProduction')).toBe('orders.transitions.backInProduction')
    expect(transitionLabelKey('Confirmed', 'InProduction')).toBe('orders.transitions.InProduction')
  })

  it('puts forward actions before back and cancel', () => {
    expect(orderedTransitions('Ready', ['Cancelled', 'InProduction', 'Delivered'])).toEqual(['Delivered', 'InProduction', 'Cancelled'])
  })

  it('indexes the progress steps', () => {
    expect(progressIndex('InProduction')).toBe(2)
    expect(progressIndex('Cancelled')).toBe(-1)
  })
})

describe('server errors', () => {
  it('maps commercial rights problems to the line risk error and others to rejected', () => {
    const kinds = serverOrderErrors({ 'lines[1].commercialRights': ['x'], 'lines[0].quantity': ['y'], customerId: ['z'] })

    expect(kinds).toEqual({ 'lines.1.risk': 'risk', 'lines.0.quantity': 'server', customerId: 'server' })
    const messages = orderErrorMessages(kinds, key => key)
    expect(messages).toEqual({ 'lines.1.risk': 'risk', 'lines.0.quantity': 'server', customerId: 'orders.errors.rejected' })
  })
})
