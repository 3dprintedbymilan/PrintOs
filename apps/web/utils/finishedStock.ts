import { parseDecimal } from './settingsForm'

export interface FinishedStockItem {
  variantId: string
  productId: string
  productName: string
  variantName: string
  sku: string | null
  productArchived: boolean
  onHand: number
  reserved: number
  available: number
  lowStockThreshold: number | null
  isLow: boolean
  averageUnitCost: number | null
  stockValue: number | null
  updatedAt: string | null
}

export interface FinishedStockSummary {
  skuCount: number
  skusInStock: number
  totalUnits: number
  totalReserved: number
  totalAvailable: number
  lowStockCount: number
  stockValue: number
}

export interface FinishedStockList {
  summary: FinishedStockSummary
  items: FinishedStockItem[]
}

export type FinishedMovementKind = 'Produced' | 'Sold' | 'Reserved' | 'ReservationReleased' | 'Adjustment' | 'Returned'

export interface FinishedStockMovement {
  id: string
  variantId: string
  kind: FinishedMovementKind
  delta: number
  reservedDelta: number
  balanceAfter: number
  reservedAfter: number
  reason: string
  unitCost: number | null
  printJobId: string | null
  printJobName: string | null
  orderId: string | null
  orderNumber: number | null
  orderLineId: string | null
  marketSaleId: string | null
  createdAt: string
}

export interface VariantReservation {
  orderId: string
  orderNumber: number
  orderLineId: string
  quantity: number
  status: string
  createdAt: string
}

export interface FinishedStockDetail {
  item: FinishedStockItem
  reservations: VariantReservation[]
}

export interface OrderReservationLine {
  orderLineId: string
  variantId: string
  description: string
  quantity: number
  reserved: number
  fulfilledQuantity: number
  status: 'None' | 'Active' | 'Released' | 'Fulfilled'
  onHand: number
  available: number
}

export interface OrderReservations {
  orderId: string
  canReserve: boolean
  lines: OrderReservationLine[]
}

export type StockTone = 'success' | 'warning' | 'danger' | 'neutral'

export function availabilityTone(item: Pick<FinishedStockItem, 'onHand' | 'available' | 'isLow'>): StockTone {
  if (item.available < 0) return 'danger'
  if (item.onHand === 0) return 'neutral'
  return item.isLow ? 'warning' : 'success'
}

export function filterFinishedStock(items: FinishedStockItem[], search: string, onlyLow: boolean): FinishedStockItem[] {
  const needle = search.trim().toLowerCase()
  return items.filter((item) => {
    if (onlyLow && !item.isLow) return false
    if (!needle) return true
    return [item.productName, item.variantName, item.sku ?? ''].some(text => text.toLowerCase().includes(needle))
  })
}

export type StockAdjustmentErrorKind = 'amount' | 'reason'

export function validateStockAdjustment(direction: 'add' | 'remove', amount: string, reason: string, allowBelowReserved = false): {
  adjustment: { delta: number, reason: string, allowBelowReserved: boolean } | null
  errors: Partial<Record<'amount' | 'reason', StockAdjustmentErrorKind>>
} {
  const errors: Partial<Record<'amount' | 'reason', StockAdjustmentErrorKind>> = {}
  const units = parseDecimal(amount)
  if (units === null || !Number.isInteger(units) || units < 1 || units > 100_000) errors.amount = 'amount'
  if (!reason.trim()) errors.reason = 'reason'
  if (Object.keys(errors).length > 0) return { adjustment: null, errors }
  return { adjustment: { delta: direction === 'remove' ? -units! : units!, reason: reason.trim(), allowBelowReserved }, errors }
}

export function validateThreshold(text: string): { threshold: number | null, valid: boolean } {
  if (!text.trim()) return { threshold: null, valid: true }
  const value = parseDecimal(text)
  if (value === null || !Number.isInteger(value) || value < 0 || value > 100_000) return { threshold: null, valid: false }
  return { threshold: value, valid: true }
}

export function signedUnits(value: number): string {
  return value > 0 ? `+${value}` : String(value)
}
