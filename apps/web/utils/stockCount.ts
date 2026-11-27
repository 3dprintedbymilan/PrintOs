import { parseDecimal } from './settingsForm'

export interface ReservationOverviewItem {
  orderId: string
  orderNumber: number
  orderStatus: string
  customerName: string | null
  dueDate: string | null
  orderLineId: string
  variantId: string
  productName: string
  variantName: string
  sku: string | null
  quantity: number
  onHand: number
  reserved: number
  available: number
  isShort: boolean
}

export interface ReservationOverview {
  summary: { lines: number, orders: number, reservedUnits: number, shortLines: number, shortUnits: number }
  items: ReservationOverviewItem[]
}

export interface BulkResponse {
  changed: number
  unchanged: number
}

export interface WeighForm {
  gross: string
  empty: string
  reason: string
}

export function parseWholeNumber(input: string): number | null {
  const parsed = parseDecimal(input)
  return parsed !== null && Number.isInteger(parsed) ? parsed : null
}

export function remainingFromGross(gross: number, empty: number): number {
  return gross - empty
}

export function countDelta(current: number, counted: number): number {
  return counted - current
}

export function signedNumber(value: number): string {
  return value > 0 ? `+${value}` : String(value)
}

export type WeighErrorKind = 'gross' | 'empty' | 'lighter'

export function validateWeigh(form: WeighForm, knownEmpty: number | null): {
  payload: { grossWeightGrams: number, emptySpoolGrams: number, reason?: string } | null
  errors: Partial<Record<'gross' | 'empty', WeighErrorKind>>
} {
  const errors: Partial<Record<'gross' | 'empty', WeighErrorKind>> = {}
  const gross = parseWholeNumber(form.gross)
  if (gross === null || gross < 1 || gross > 100_000) errors.gross = 'gross'
  const emptyText = form.empty.trim()
  const empty = emptyText === '' ? knownEmpty : parseWholeNumber(emptyText)
  if (empty === null || empty < 0 || empty > 2000) errors.empty = 'empty'
  if (!errors.gross && !errors.empty && gross! < empty!) errors.gross = 'lighter'
  if (errors.gross || errors.empty) return { payload: null, errors }
  const reason = form.reason.trim()
  return {
    payload: { grossWeightGrams: gross!, emptySpoolGrams: empty!, ...(reason ? { reason } : {}) },
    errors,
  }
}

export interface CountRow {
  id: string
  current: number
  input: string
}

export type CountRowState = { kind: 'skip' } | { kind: 'invalid' } | { kind: 'same' } | { kind: 'change', counted: number, delta: number }

export function rowState(row: CountRow, max: number): CountRowState {
  if (row.input.trim() === '') return { kind: 'skip' }
  const counted = parseWholeNumber(row.input)
  if (counted === null || counted < 0 || counted > max) return { kind: 'invalid' }
  const delta = countDelta(row.current, counted)
  return delta === 0 ? { kind: 'same' } : { kind: 'change', counted, delta }
}

export function changedRows(rows: CountRow[], max: number): { lines: { id: string, counted: number }[], invalidIds: string[] } {
  const lines: { id: string, counted: number }[] = []
  const invalidIds: string[] = []
  for (const row of rows) {
    const state = rowState(row, max)
    if (state.kind === 'invalid') invalidIds.push(row.id)
    else if (state.kind === 'change') lines.push({ id: row.id, counted: state.counted })
  }
  return { lines, invalidIds }
}

export function filterReservations(items: ReservationOverviewItem[], search: string, shortOnly: boolean): ReservationOverviewItem[] {
  const needle = search.trim().toLowerCase()
  return items.filter((item) => {
    if (shortOnly && !item.isShort) return false
    if (!needle) return true
    return [item.productName, item.variantName, item.sku ?? '', item.customerName ?? '', `#${item.orderNumber}`, String(item.orderNumber)]
      .some(value => value.toLowerCase().includes(needle))
  })
}
