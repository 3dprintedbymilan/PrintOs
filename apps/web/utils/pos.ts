import type { MarketPaymentMethod } from './markets'
import { parseDecimal } from './settingsForm'

export interface PosItem {
  variantId: string
  productId: string
  productName: string
  variantName: string
  sku: string | null
  price: number
  available: number
  commercialRightsStatus: 'NoModel' | 'Cleared' | 'NotCleared'
}

export interface CartLine {
  variantId: string
  name: string
  price: number
  available: number
  quantity: number
  notCleared: boolean
}

export type DiscountMode = 'amount' | 'percent'

export interface CheckoutRequest {
  idempotencyKey: string
  lines: { variantId: string, quantity: number, unitPrice: null, discountPercent: null }[]
  discountAmount: number
  paymentMethod: MarketPaymentMethod
  amountTendered: number | null
  customerId: string | null
  notes: string | null
  acknowledgeRisk: boolean
  soldAt: string
  businessDate: string
}

export const roundMoney = (value: number) => Math.round((value + Number.EPSILON) * 100) / 100

export function itemName(item: Pick<PosItem, 'productName' | 'variantName'>): string {
  return `${item.productName} · ${item.variantName}`
}

export function addToCart(cart: CartLine[], item: PosItem, quantity = 1): CartLine[] {
  const existing = cart.find(line => line.variantId === item.variantId)
  if (!existing) {
    if (item.available < 1) return cart
    return [...cart, {
      variantId: item.variantId,
      name: itemName(item),
      price: item.price,
      available: item.available,
      quantity: Math.min(quantity, item.available),
      notCleared: item.commercialRightsStatus === 'NotCleared',
    }]
  }
  return setQuantity(cart, item.variantId, existing.quantity + quantity)
}

export function setQuantity(cart: CartLine[], variantId: string, quantity: number): CartLine[] {
  return cart.flatMap((line) => {
    if (line.variantId !== variantId) return [line]
    const next = Math.min(Math.max(0, Math.floor(quantity)), line.available)
    return next < 1 ? [] : [{ ...line, quantity: next }]
  })
}

export function parseQuantity(text: string): number | null {
  const value = parseDecimal(text)
  return value !== null && Number.isInteger(value) && value >= 0 && value <= 100_000 ? value : null
}

export function cartCount(cart: CartLine[]): number {
  return cart.reduce((sum, line) => sum + line.quantity, 0)
}

export function cartSubtotal(cart: CartLine[]): number {
  return roundMoney(cart.reduce((sum, line) => sum + (line.quantity * line.price), 0))
}

export function discountAmountFor(mode: DiscountMode, text: string, subtotal: number): { amount: number, valid: boolean } {
  if (!text.trim()) return { amount: 0, valid: true }
  const value = parseDecimal(text)
  if (value === null) return { amount: 0, valid: false }
  if (mode === 'percent') {
    return value > 100 ? { amount: 0, valid: false } : { amount: roundMoney(subtotal * value / 100), valid: true }
  }
  return value > subtotal ? { amount: 0, valid: false } : { amount: roundMoney(value), valid: true }
}

export function saleTotal(subtotal: number, discount: number): number {
  return Math.max(0, roundMoney(subtotal - discount))
}

export function changeFor(total: number, tendered: number | null): number {
  return tendered === null ? 0 : Math.max(0, roundMoney(tendered - total))
}

export function quickTenders(total: number): number[] {
  if (total <= 0) return []
  const suggestions = new Set<number>([roundMoney(total)])
  for (const note of [5, 10, 20, 50, 100]) {
    const rounded = Math.ceil(total / note) * note
    if (rounded > total) suggestions.add(rounded)
  }
  return [...suggestions].sort((left, right) => left - right).slice(0, 3)
}

export function tenderedFor(text: string, total: number): { value: number | null, state: 'exact' | 'ok' | 'short' | 'invalid' } {
  if (!text.trim()) return { value: total, state: 'exact' }
  const value = parseDecimal(text)
  if (value === null) return { value: null, state: 'invalid' }
  return { value: roundMoney(value), state: value < total ? 'short' : 'ok' }
}

export function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`
}

export function buildCheckout(input: {
  key: string
  cart: CartLine[]
  discountAmount: number
  method: MarketPaymentMethod
  tendered: number | null
  acknowledgeRisk: boolean
  now: Date
  businessDate: string
}): CheckoutRequest {
  return {
    idempotencyKey: input.key,
    lines: input.cart.map(line => ({ variantId: line.variantId, quantity: line.quantity, unitPrice: null, discountPercent: null })),
    discountAmount: input.discountAmount,
    paymentMethod: input.method,
    amountTendered: input.method === 'cash' ? input.tendered : null,
    customerId: null,
    notes: null,
    acknowledgeRisk: input.acknowledgeRisk,
    soldAt: input.now.toISOString(),
    businessDate: input.businessDate,
  }
}

export function filterItems(items: PosItem[], search: string): PosItem[] {
  const needle = search.trim().toLowerCase()
  if (!needle) return items
  return items.filter(item => [item.productName, item.variantName, item.sku ?? ''].some(text => text.toLowerCase().includes(needle)))
}

export function hasUnclearedLines(cart: CartLine[]): boolean {
  return cart.some(line => line.notCleared)
}

export interface PendingSale {
  key: string
  marketId: string
  request: CheckoutRequest
  total: number
  queuedAt: string
  state: 'pending' | 'rejected'
  message: string | null
}

export const queueStorageKey = 'printos_pos_queue'

export interface KeyValueStorage {
  getItem: (key: string) => string | null
  setItem: (key: string, value: string) => void
}

export function loadQueue(storage: KeyValueStorage | null): PendingSale[] {
  try {
    const raw = storage?.getItem(queueStorageKey)
    const parsed: unknown = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed) ? parsed.filter((entry): entry is PendingSale => typeof entry?.key === 'string' && typeof entry?.marketId === 'string') : []
  }
  catch {
    return []
  }
}

export function saveQueue(storage: KeyValueStorage | null, queue: PendingSale[]): void {
  try {
    storage?.setItem(queueStorageKey, JSON.stringify(queue))
  }
  catch {
    // the sale stays in memory for this session
  }
}

export function pendingQuantities(queue: PendingSale[], marketId: string): Map<string, number> {
  const totals = new Map<string, number>()
  for (const sale of queue) {
    if (sale.marketId !== marketId || sale.state !== 'pending') continue
    for (const line of sale.request.lines) totals.set(line.variantId, (totals.get(line.variantId) ?? 0) + line.quantity)
  }
  return totals
}

export function isRetryableStatus(status: number): boolean {
  return status === 0 || status === 401 || status === 408 || status === 429 || status >= 500
}
