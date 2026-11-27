import type { MarketSummary } from './markets'
import { parseDecimal } from './settingsForm'

export interface MarketClosing {
  id: string
  marketId: string
  marketName: string
  date: string
  currency: string
  saleCount: number
  voidedCount: number
  itemCount: number
  subtotal: number
  discounts: number
  revenue: number
  cost: number
  grossProfit: number
  cashTotal: number
  cardTotal: number
  otherTotal: number
  openingFloat: number
  expectedCash: number
  countedCash: number
  difference: number
  standCost: number
  notes: string | null
  closedAt: string
  items: MarketSummary['items']
}

export interface ClosingDay {
  date: string
  saleCount: number
  revenue: number
  closed: boolean
}

export type ClosingField = 'openingFloat' | 'countedCash' | 'notes'
export type ClosingErrorKind = 'required' | 'number' | 'length'

export function closingDays(days: MarketSummary['days'], closings: Pick<MarketClosing, 'date' | 'saleCount' | 'revenue'>[], today: string): ClosingDay[] {
  const byDate = new Map<string, ClosingDay>()
  for (const day of days) byDate.set(day.date, { date: day.date, saleCount: day.saleCount, revenue: day.revenue, closed: false })
  for (const closing of closings) byDate.set(closing.date, { date: closing.date, saleCount: closing.saleCount, revenue: closing.revenue, closed: true })
  if (!byDate.has(today)) byDate.set(today, { date: today, saleCount: 0, revenue: 0, closed: false })
  return [...byDate.values()].sort((left, right) => right.date.localeCompare(left.date))
}

export function expectedCash(openingFloat: number, cashSales: number): number {
  return Math.round((openingFloat + cashSales) * 100) / 100
}

export function cashDifference(counted: number, expected: number): number {
  return Math.round((counted - expected) * 100) / 100
}

export function validateClosingForm(form: { openingFloat: string, countedCash: string, notes: string }): {
  values: { openingFloat: number, countedCash: number, notes: string | null } | null
  errors: Partial<Record<ClosingField, ClosingErrorKind>>
} {
  const errors: Partial<Record<ClosingField, ClosingErrorKind>> = {}
  let openingFloat = 0
  if (form.openingFloat.trim()) {
    const parsed = parseDecimal(form.openingFloat)
    if (parsed === null || parsed > 1_000_000) errors.openingFloat = 'number'
    else openingFloat = parsed
  }

  let countedCash = 0
  if (!form.countedCash.trim()) errors.countedCash = 'required'
  else {
    const parsed = parseDecimal(form.countedCash)
    if (parsed === null || parsed > 1_000_000) errors.countedCash = 'number'
    else countedCash = parsed
  }

  if (form.notes.trim().length > 2000) errors.notes = 'length'
  if (Object.keys(errors).length > 0) return { values: null, errors }
  return { values: { openingFloat, countedCash, notes: form.notes.trim() || null }, errors }
}
