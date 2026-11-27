import { parseDecimal } from './settingsForm'
import type { RightsStatus } from './products'

export type OrderStatus = 'Draft' | 'Confirmed' | 'InProduction' | 'Ready' | 'Delivered' | 'Cancelled'
export type OrderPaymentStatus = 'Unpaid' | 'Partial' | 'Paid'
export type OrderChannel = 'direct' | 'market' | 'online' | 'other'
export type PaymentMethod = 'cash' | 'transfer' | 'card' | 'other'
export type Tone = 'success' | 'warning' | 'danger' | 'info' | 'neutral'

export const orderStatuses: OrderStatus[] = ['Draft', 'Confirmed', 'InProduction', 'Ready', 'Delivered', 'Cancelled']
export const progressSteps: OrderStatus[] = ['Draft', 'Confirmed', 'InProduction', 'Ready', 'Delivered']
export const orderChannels: OrderChannel[] = ['direct', 'market', 'online', 'other']
export const paymentMethods: PaymentMethod[] = ['cash', 'transfer', 'card', 'other']

export interface OrderSummary {
  id: string
  number: number
  customerId: string | null
  customerName: string | null
  status: OrderStatus
  paymentStatus: OrderPaymentStatus
  channel: OrderChannel
  currency: string
  orderedAt: string
  dueDate: string | null
  isOverdue: boolean
  totalAmount: number
  amountPaid: number
  lineCount: number
  itemCount: number
}

export interface OrderLine {
  id: string
  variantId: string | null
  description: string
  sku: string | null
  quantity: number
  listUnitPrice: number | null
  unitPrice: number
  discountPercent: number
  lineTotal: number
  unitCost: number | null
  commercialRightsStatus: RightsStatus | null
  commercialRightsBlockers: string[]
  riskAcknowledged: boolean
}

export interface OrderPayment {
  id: string
  amount: number
  paidAt: string
  method: PaymentMethod | null
  note: string | null
}

export interface Order extends OrderSummary {
  discountAmount: number
  subtotal: number
  totalCost: number
  costComplete: boolean
  profit: number
  marginPercent: number | null
  amountDue: number
  notes: string | null
  confirmedAt: string | null
  deliveredAt: string | null
  cancelledAt: string | null
  createdAt: string
  linesEditable: boolean
  headerEditable: boolean
  paymentsEditable: boolean
  hasRightsWarning: boolean
  allowedTransitions: OrderStatus[]
  lines: OrderLine[]
  payments: OrderPayment[]
}

export interface OrderLineRequest {
  id: string | null
  variantId: string | null
  description: string | null
  quantity: number
  unitPrice: number | null
  unitCost: number | null
  discountPercent: number | null
}

export interface OrderRequest {
  customerId: string | null
  channel: OrderChannel
  dueDate: string | null
  discountAmount: number | null
  notes: string | null
  lines: OrderLineRequest[] | null
  acknowledgeRisk: boolean
}

export const orderStatusTone: Record<OrderStatus, Tone> = {
  Draft: 'neutral',
  Confirmed: 'info',
  InProduction: 'warning',
  Ready: 'success',
  Delivered: 'success',
  Cancelled: 'danger',
}

export const orderPaymentTone: Record<OrderPaymentStatus, Tone> = {
  Unpaid: 'warning',
  Partial: 'info',
  Paid: 'success',
}

export function orderLabel(number: number): string {
  return `#${number}`
}

const transitionKeys = {
  forward: {
    Draft: 'orders.transitions.reopen',
    Confirmed: 'orders.transitions.Confirmed',
    InProduction: 'orders.transitions.InProduction',
    Ready: 'orders.transitions.Ready',
    Delivered: 'orders.transitions.Delivered',
    Cancelled: 'orders.transitions.Cancelled',
  },
  back: {
    Draft: 'orders.transitions.reopen',
    Confirmed: 'orders.transitions.backConfirmed',
    InProduction: 'orders.transitions.backInProduction',
    Ready: 'orders.transitions.Ready',
    Delivered: 'orders.transitions.Delivered',
    Cancelled: 'orders.transitions.Cancelled',
  },
} as const

export type TransitionKind = 'forward' | 'back' | 'cancel' | 'reopen'

export function transitionKind(from: OrderStatus, to: OrderStatus): TransitionKind {
  if (to === 'Cancelled') return 'cancel'
  if (from === 'Cancelled') return 'reopen'
  return progressSteps.indexOf(to) < progressSteps.indexOf(from) ? 'back' : 'forward'
}

export function transitionLabelKey(from: OrderStatus, to: OrderStatus): string {
  return transitionKeys[transitionKind(from, to) === 'back' ? 'back' : 'forward'][to]
}

export function orderedTransitions(from: OrderStatus, allowed: OrderStatus[]): OrderStatus[] {
  const rank: Record<TransitionKind, number> = { forward: 0, back: 1, reopen: 0, cancel: 2 }
  return [...allowed].sort((left, right) => rank[transitionKind(from, left)] - rank[transitionKind(from, right)])
}

export function progressIndex(status: OrderStatus): number {
  return progressSteps.indexOf(status)
}

export type OrderServerErrorKind = 'required' | 'number' | 'range' | 'risk' | 'server'

export function serverOrderErrors(fieldErrors: Record<string, string[]>): Record<string, OrderServerErrorKind> {
  const result: Record<string, OrderServerErrorKind> = {}
  for (const key of Object.keys(fieldErrors)) {
    const normalized = key.replace(/\[(\d+)\]/g, '.$1')
    const lineRights = /^lines\.(\d+)\.commercialRights$/.exec(normalized)
    if (lineRights) result[`lines.${lineRights[1]}.risk`] = 'risk'
    else result[normalized] = 'server'
  }
  return result
}

type Translate = (key: string, params?: Record<string, unknown>) => string

export function orderErrorMessages(errors: Record<string, OrderServerErrorKind>, translate: Translate): Record<string, string> {
  const messages: Record<string, string> = {}
  for (const [key, kind] of Object.entries(errors)) {
    if (key.startsWith('lines.') && key.split('.').length > 2) messages[key] = kind
    else if (kind === 'server') messages[key] = translate('orders.errors.rejected')
    else messages[key] = translate(`orders.errors.${kind}`, { field: translate(`orders.fields.${key}`) })
  }
  return messages
}

export interface OrderLineForm {
  key: string
  id: string | null
  variantId: string
  description: string
  quantity: string
  unitPrice: string
  unitCost: string
  discountPercent: string
  rights: RightsStatus | null
  acknowledged: boolean
}

export interface OrderFormValues {
  customerId: string
  channel: string
  dueDate: string
  notes: string
  discountAmount: string
  lines: OrderLineForm[]
}

export type OrderErrorKind = 'required' | 'number' | 'range' | 'risk'

let lineCounter = 0

export function emptyLineForm(): OrderLineForm {
  lineCounter += 1
  return { key: `line-${lineCounter}`, id: null, variantId: '', description: '', quantity: '1', unitPrice: '', unitCost: '', discountPercent: '', rights: null, acknowledged: false }
}

export function emptyOrderForm(): OrderFormValues {
  return { customerId: '', channel: 'direct', dueDate: '', notes: '', discountAmount: '', lines: [] }
}

function decimalText(value: number | null): string {
  return value === null ? '' : String(value).replace('.', ',')
}

export function orderToForm(order: Order): OrderFormValues {
  return {
    customerId: order.customerId ?? '',
    channel: order.channel,
    dueDate: order.dueDate ?? '',
    notes: order.notes ?? '',
    discountAmount: order.discountAmount > 0 ? decimalText(order.discountAmount) : '',
    lines: order.lines.map((line) => {
      const form = emptyLineForm()
      return {
        ...form,
        id: line.id,
        variantId: line.variantId ?? '',
        description: line.description,
        quantity: String(line.quantity),
        unitPrice: decimalText(line.unitPrice),
        unitCost: decimalText(line.unitCost),
        discountPercent: line.discountPercent > 0 ? decimalText(line.discountPercent) : '',
        rights: line.commercialRightsStatus,
        acknowledged: line.riskAcknowledged,
      }
    }),
  }
}

export function validateOrderForm(values: OrderFormValues): {
  request: OrderRequest | null
  errors: Record<string, OrderErrorKind>
} {
  const errors: Record<string, OrderErrorKind> = {}
  const lines: OrderLineRequest[] = []
  const acknowledgeRisk = values.lines.some(line => line.variantId !== '' && line.rights === 'NotCleared' && line.acknowledged)

  values.lines.forEach((line, index) => {
    const key = `lines.${index}`
    const quantity = parseDecimal(line.quantity)
    if (quantity === null || !Number.isInteger(quantity)) errors[`${key}.quantity`] = 'number'
    else if (quantity < 1 || quantity > 100_000) errors[`${key}.quantity`] = 'range'

    const price = line.unitPrice.trim() === '' ? null : parseDecimal(line.unitPrice)
    if (line.unitPrice.trim() !== '' && price === null) errors[`${key}.unitPrice`] = 'number'
    else if (price !== null && price > 1_000_000) errors[`${key}.unitPrice`] = 'range'
    else if (line.variantId === '' && price === null) errors[`${key}.unitPrice`] = 'required'

    const discount = line.discountPercent.trim() === '' ? 0 : parseDecimal(line.discountPercent)
    if (discount === null) errors[`${key}.discountPercent`] = 'number'
    else if (discount > 100) errors[`${key}.discountPercent`] = 'range'

    const cost = line.unitCost.trim() === '' ? null : parseDecimal(line.unitCost)
    if (line.variantId === '' && line.unitCost.trim() !== '' && cost === null) errors[`${key}.unitCost`] = 'number'

    if (line.variantId === '' && !line.description.trim()) errors[`${key}.description`] = 'required'
    if (line.variantId !== '' && line.rights === 'NotCleared' && !line.acknowledged) errors[`${key}.risk`] = 'risk'
    
    lines.push({
      id: line.id,
      variantId: line.variantId || null,
      description: line.variantId ? null : line.description.trim(),
      quantity: quantity ?? 0,
      unitPrice: price,
      unitCost: line.variantId ? null : cost,
      discountPercent: discount ?? 0,
    })
  })

  const discountAmount = values.discountAmount.trim() === '' ? 0 : parseDecimal(values.discountAmount)
  if (discountAmount === null) errors.discountAmount = 'number'
  else if (discountAmount > 1_000_000) errors.discountAmount = 'range'

  if (values.dueDate && !/^\d{4}-\d{2}-\d{2}$/.test(values.dueDate)) errors.dueDate = 'number'

  if (Object.keys(errors).length > 0) return { request: null, errors }

  return {
    request: {
      customerId: values.customerId || null,
      channel: values.channel as OrderChannel,
      dueDate: values.dueDate || null,
      discountAmount,
      notes: values.notes.trim() || null,
      lines,
      acknowledgeRisk,
    },
    errors,
  }
}

function round2(value: number): number {
  return Math.round((value + Number.EPSILON) * 100) / 100
}

export function lineTotal(quantity: number, unitPrice: number, discountPercent: number): number {
  return round2(quantity * unitPrice * (1 - discountPercent / 100))
}

export function previewTotals(values: OrderFormValues, listPrices: Map<string, number>): { subtotal: number, total: number, discount: number } {
  let subtotal = 0
  for (const line of values.lines) {
    const quantity = parseDecimal(line.quantity)
    const price = line.unitPrice.trim() === '' ? (listPrices.get(line.variantId) ?? null) : parseDecimal(line.unitPrice)
    const discount = line.discountPercent.trim() === '' ? 0 : parseDecimal(line.discountPercent)
    if (quantity === null || price === null || discount === null) continue
    subtotal += lineTotal(quantity, price, Math.min(discount, 100))
  }
  const discount = Math.min(parseDecimal(values.discountAmount) ?? 0, subtotal)
  return { subtotal: round2(subtotal), discount: round2(discount), total: round2(Math.max(0, subtotal - discount)) }
}
