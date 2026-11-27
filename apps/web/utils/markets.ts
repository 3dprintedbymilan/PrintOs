import { parseDecimal } from './settingsForm'

export type MarketStatus = 'Open' | 'Closed'
export type SaleStatus = 'Completed' | 'Voided'
export type MarketPaymentMethod = 'cash' | 'card' | 'other'

export const marketPaymentMethods: MarketPaymentMethod[] = ['cash', 'card', 'other']

export interface Market {
  id: string
  name: string
  location: string | null
  startDate: string
  endDate: string | null
  standCost: number
  notes: string | null
  status: MarketStatus
  closedAt: string | null
  currency: string
  createdAt: string
  saleCount: number
  voidedCount: number
  revenue: number
  cost: number
  canDelete: boolean
}

export interface MarketSaleLine {
  id: string
  variantId: string | null
  description: string
  sku: string | null
  quantity: number
  listUnitPrice: number
  unitPrice: number
  discountPercent: number
  lineTotal: number
  unitCost: number | null
  riskAcknowledged: boolean
}

export interface MarketSale {
  id: string
  marketId: string
  receiptNumber: number
  status: SaleStatus
  soldAt: string
  businessDate: string
  customerId: string | null
  customerName: string | null
  paymentMethod: MarketPaymentMethod
  currency: string
  subtotal: number
  discountAmount: number
  totalAmount: number
  totalCost: number
  costComplete: boolean
  profit: number
  amountTendered: number | null
  changeGiven: number
  notes: string | null
  voidedAt: string | null
  voidReason: string | null
  replayed: boolean
  lines: MarketSaleLine[]
}

export interface MarketSummary {
  marketId: string
  date: string | null
  currency: string
  saleCount: number
  voidedCount: number
  voidedAmount: number
  itemCount: number
  subtotal: number
  discounts: number
  revenue: number
  cost: number
  costComplete: boolean
  grossProfit: number
  standCost: number
  byPayment: { method: MarketPaymentMethod, saleCount: number, amount: number }[]
  items: { variantId: string | null, description: string, quantity: number, revenue: number, cost: number }[]
  days: { date: string, saleCount: number, revenue: number }[]
}

export interface MarketRequest {
  name: string
  location: string | null
  startDate: string
  endDate: string | null
  standCost: number
  notes: string | null
}

export interface MarketFormValues {
  name: string
  location: string
  startDate: string
  endDate: string
  standCost: string
  notes: string
}

export type MarketField = 'name' | 'location' | 'startDate' | 'endDate' | 'standCost' | 'notes'
export type MarketErrorKind = 'required' | 'order' | 'number' | 'length'

export function emptyMarketForm(today = new Date()): MarketFormValues {
  return { name: '', location: '', startDate: localDateKey(today), endDate: '', standCost: '', notes: '' }
}

export function marketToForm(market: Market): MarketFormValues {
  return {
    name: market.name,
    location: market.location ?? '',
    startDate: market.startDate,
    endDate: market.endDate ?? '',
    standCost: market.standCost ? String(market.standCost).replace('.', ',') : '',
    notes: market.notes ?? '',
  }
}

export function localDateKey(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}

export function validateMarketForm(form: MarketFormValues): { request: MarketRequest | null, errors: Partial<Record<MarketField, MarketErrorKind>> } {
  const errors: Partial<Record<MarketField, MarketErrorKind>> = {}
  const name = form.name.trim()
  if (!name) errors.name = 'required'
  else if (name.length > 120) errors.name = 'length'
  if (form.location.trim().length > 200) errors.location = 'length'
  if (form.notes.trim().length > 2000) errors.notes = 'length'
  if (!form.startDate) errors.startDate = 'required'
  if (form.startDate && form.endDate && form.endDate < form.startDate) errors.endDate = 'order'

  let standCost = 0
  if (form.standCost.trim()) {
    const parsed = parseDecimal(form.standCost)
    if (parsed === null || parsed > 1_000_000) errors.standCost = 'number'
    else standCost = parsed
  }

  if (Object.keys(errors).length > 0) return { request: null, errors }
  return {
    request: {
      name,
      location: form.location.trim() || null,
      startDate: form.startDate,
      endDate: form.endDate || null,
      standCost,
      notes: form.notes.trim() || null,
    },
    errors,
  }
}

export function serverMarketErrors(fieldErrors: Record<string, string[]>): Partial<Record<MarketField, MarketErrorKind>> {
  const errors: Partial<Record<MarketField, MarketErrorKind>> = {}
  if (fieldErrors.name) errors.name = 'required'
  if (fieldErrors.location) errors.location = 'length'
  if (fieldErrors.notes) errors.notes = 'length'
  if (fieldErrors.startDate) errors.startDate = 'required'
  if (fieldErrors.endDate) errors.endDate = 'order'
  if (fieldErrors.standCost) errors.standCost = 'number'
  return errors
}

export function marketTone(status: MarketStatus): 'success' | 'neutral' {
  return status === 'Open' ? 'success' : 'neutral'
}

export function formatDateRange(start: string, end: string | null, format: (date: string) => string): string {
  return end && end !== start ? `${format(start)} - ${format(end)}` : format(start)
}

export function averageSale(revenue: number, count: number): number {
  return count > 0 ? Math.round((revenue / count) * 100) / 100 : 0
}
