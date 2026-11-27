import { describe, expect, it } from 'vitest'
import type { PendingSale, PosItem } from '../utils/pos'
import {
  addToCart,
  buildCheckout,
  cartCount,
  cartSubtotal,
  changeFor,
  discountAmountFor,
  filterItems,
  isRetryableStatus,
  loadQueue,
  parseQuantity,
  pendingQuantities,
  queueStorageKey,
  quickTenders,
  saleTotal,
  saveQueue,
  setQuantity,
  tenderedFor,
} from '../utils/pos'

function item(overrides: Partial<PosItem> = {}): PosItem {
  return { variantId: 'v1', productId: 'p1', productName: 'Vase', variantName: 'Large', sku: 'VASE-L', price: 12.5, available: 3, commercialRightsStatus: 'Cleared', ...overrides }
}

function memoryStorage() {
  const data = new Map<string, string>()
  return { getItem: (key: string) => data.get(key) ?? null, setItem: (key: string, value: string) => void data.set(key, value) }
}

describe('cart', () => {
  it('adds a product once and raises its quantity up to the available stock', () => {
    let cart = addToCart([], item())
    cart = addToCart(cart, item())
    cart = addToCart(cart, item())
    cart = addToCart(cart, item())

    expect(cart).toHaveLength(1)
    expect(cart[0]?.quantity).toBe(3)
    expect(cartCount(cart)).toBe(3)
  })

  it('ignores products without stock and flags products with unverified rights', () => {
    expect(addToCart([], item({ available: 0 }))).toEqual([])
    expect(addToCart([], item({ commercialRightsStatus: 'NotCleared' }))[0]?.notCleared).toBe(true)
  })

  it('sets and removes quantities within the available range', () => {
    const cart = addToCart([], item())

    expect(setQuantity(cart, 'v1', 2)[0]?.quantity).toBe(2)
    expect(setQuantity(cart, 'v1', 99)[0]?.quantity).toBe(3)
    expect(setQuantity(cart, 'v1', 0)).toEqual([])
    expect(setQuantity(cart, 'unknown', 1)).toEqual(cart)
  })

  it('sums the subtotal in cents without floating point drift', () => {
    const cart = addToCart(addToCart([], item({ price: 0.1 }), 3), item({ variantId: 'v2', price: 0.2 }), 1)

    expect(cartSubtotal(cart)).toBe(0.5)
  })

  it('parses a typed quantity', () => {
    expect(parseQuantity('4')).toBe(4)
    expect(parseQuantity('2,5')).toBeNull()
    expect(parseQuantity('abc')).toBeNull()
  })
})

describe('discount and totals', () => {
  it('supports an amount and a percentage and rejects values above the subtotal', () => {
    expect(discountAmountFor('amount', '2,5', 20)).toEqual({ amount: 2.5, valid: true })
    expect(discountAmountFor('percent', '10', 25)).toEqual({ amount: 2.5, valid: true })
    expect(discountAmountFor('amount', '30', 20).valid).toBe(false)
    expect(discountAmountFor('percent', '120', 20).valid).toBe(false)
    expect(discountAmountFor('amount', 'abc', 20).valid).toBe(false)
    expect(discountAmountFor('amount', '', 20)).toEqual({ amount: 0, valid: true })
  })

  it('never goes below zero', () => {
    expect(saleTotal(20, 2.5)).toBe(17.5)
    expect(saleTotal(5, 9)).toBe(0)
  })
})

describe('cash handling', () => {
  it('computes change and treats an empty field as the exact amount', () => {
    expect(changeFor(17.5, 20)).toBe(2.5)
    expect(changeFor(17.5, 17.5)).toBe(0)
    expect(changeFor(17.5, null)).toBe(0)
    expect(tenderedFor('', 17.5)).toEqual({ value: 17.5, state: 'exact' })
    expect(tenderedFor('20', 17.5)).toEqual({ value: 20, state: 'ok' })
    expect(tenderedFor('10,50', 17.5)).toEqual({ value: 10.5, state: 'short' })
    expect(tenderedFor('x', 17.5)).toEqual({ value: null, state: 'invalid' })
  })

  it('suggests the exact amount and the next banknotes', () => {
    expect(quickTenders(17.5)).toEqual([17.5, 20, 50])
    expect(quickTenders(20)).toEqual([20, 50, 100])
    expect(quickTenders(0)).toEqual([])
  })
})

describe('checkout request', () => {
  const cart = addToCart([], item(), 2)

  it('lets the server price the lines and carries the idempotency key and acknowledgement', () => {
    const request = buildCheckout({ key: 'key-1', cart, discountAmount: 1, method: 'cash', tendered: 30, acknowledgeRisk: true, now: new Date('2026-12-05T10:00:00Z'), businessDate: '2026-12-05' })

    expect(request).toEqual({
      idempotencyKey: 'key-1',
      lines: [{ variantId: 'v1', quantity: 2, unitPrice: null, discountPercent: null }],
      discountAmount: 1,
      paymentMethod: 'cash',
      amountTendered: 30,
      customerId: null,
      notes: null,
      acknowledgeRisk: true,
      soldAt: '2026-12-05T10:00:00.000Z',
      businessDate: '2026-12-05',
    })
  })

  it('sends no tendered amount for card payments', () => {
    const request = buildCheckout({ key: 'key-2', cart, discountAmount: 0, method: 'card', tendered: 30, acknowledgeRisk: false, now: new Date(), businessDate: '2026-12-05' })

    expect(request.amountTendered).toBeNull()
  })
})

describe('search', () => {
  it('matches product, variant and sku', () => {
    const items = [item(), item({ variantId: 'v2', productName: 'Lamp', variantName: 'Small', sku: null })]

    expect(filterItems(items, '')).toHaveLength(2)
    expect(filterItems(items, 'lamp')).toHaveLength(1)
    expect(filterItems(items, 'vase-l')).toHaveLength(1)
  })
})

describe('offline queue', () => {
  const pending = (key: string, marketId = 'm1', state: PendingSale['state'] = 'pending'): PendingSale => ({
    key,
    marketId,
    total: 10,
    queuedAt: '2026-12-05T10:00:00Z',
    state,
    message: null,
    request: buildCheckout({ key, cart: addToCart([], item(), 2), discountAmount: 0, method: 'card', tendered: null, acknowledgeRisk: false, now: new Date(), businessDate: '2026-12-05' }),
  })

  it('survives a reload through storage and ignores corrupt data', () => {
    const storage = memoryStorage()
    saveQueue(storage, [pending('a'), pending('b')])

    expect(loadQueue(storage).map(entry => entry.key)).toEqual(['a', 'b'])

    storage.setItem(queueStorageKey, '{not json')
    expect(loadQueue(storage)).toEqual([])
    expect(loadQueue(null)).toEqual([])
  })

  it('counts only pending sales of the market against the available stock', () => {
    const queue = [pending('a'), pending('b'), pending('c', 'other'), pending('d', 'm1', 'rejected')]

    expect(pendingQuantities(queue, 'm1').get('v1')).toBe(4)
  })

  it('retries on network and server problems but not on validation errors', () => {
    expect([0, 401, 408, 429, 500, 503].every(isRetryableStatus)).toBe(true)
    expect([400, 404, 409].some(isRetryableStatus)).toBe(false)
  })
})
