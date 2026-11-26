import { parseDecimal } from './settingsForm'

export interface CostLine {
  kind: 'material' | 'electricity' | 'machine' | 'labour' | 'packaging' | 'extra'
  label: string
  quantity: number
  unit: string
  unitCost: number
  amount: number
}

export interface CostBreakdown {
  currency: string
  printerName: string
  materialLabel: string
  lines: CostLine[]
  totalCost: number
  marginPercent: number
  feePercent: number
  suggestedPrice: number
  feeAmount: number
  profit: number
}

export interface CostRequest {
  printerId: string
  spoolId: string
  grams: number
  printMinutes: number
  labourMinutes: number
  packagingCost: number
  extraCosts: { label: string, amount: number }[]
  marginPercent: number
  feePercent: number
}

export interface CostSnapshotSummary {
  id: string
  name: string
  printerName: string
  materialLabel: string
  currency: string
  totalCost: number
  suggestedPrice: number
  profit: number
  createdAt: string
}

export interface CostSnapshot {
  id: string
  name: string
  createdAt: string
  calculation: CostRequest
  breakdown: CostBreakdown
}

export interface ExtraFormValues {
  label: string
  amount: string
}

export interface CostFormValues {
  printerId: string
  spoolId: string
  grams: string
  printHours: string
  printMinutes: string
  labourMinutes: string
  packagingCost: string
  extras: ExtraFormValues[]
  marginPercent: string
  feePercent: string
}

export type CostField
  = | 'printerId' | 'spoolId' | 'grams' | 'printTime' | 'labourMinutes' | 'packagingCost'
    | 'marginPercent' | 'feePercent' | `extraLabel${number}` | `extraAmount${number}`
export type CostErrorKind = 'required' | 'number' | 'range' | 'positive' | 'margin' | 'marginPlusFee'

export const defaultMarginPercent = 40

export function emptyCostForm(): CostFormValues {
  return {
    printerId: '',
    spoolId: '',
    grams: '',
    printHours: '0',
    printMinutes: '0',
    labourMinutes: '0',
    packagingCost: '0',
    extras: [],
    marginPercent: String(defaultMarginPercent),
    feePercent: '0',
  }
}

const limits = { grams: 100_000, minutes: 100_000, money: 1_000_000 }

function wholeNumber(text: string): number | null {
  const value = parseDecimal(text)
  return value !== null && Number.isInteger(value) ? value : null
}

export function buildCostRequest(values: CostFormValues): { request: CostRequest | null, errors: Partial<Record<CostField, CostErrorKind>> } {
  const errors: Partial<Record<CostField, CostErrorKind>> = {}

  if (!values.printerId) errors.printerId = 'required'
  if (!values.spoolId) errors.spoolId = 'required'

  const grams = parseDecimal(values.grams)
  if (grams === null) errors.grams = values.grams.trim() ? 'number' : 'required'
  else if (grams <= 0) errors.grams = 'positive'
  else if (grams > limits.grams) errors.grams = 'range'

  const hours = wholeNumber(values.printHours)
  const minutes = wholeNumber(values.printMinutes)
  let printMinutes = 0
  if (hours === null || minutes === null) errors.printTime = 'number'
  else {
    printMinutes = hours * 60 + minutes
    if (printMinutes <= 0) errors.printTime = 'positive'
    else if (printMinutes > limits.minutes) errors.printTime = 'range'
  }

  const labourMinutes = wholeNumber(values.labourMinutes)
  if (labourMinutes === null) errors.labourMinutes = 'number'
  else if (labourMinutes > limits.minutes) errors.labourMinutes = 'range'

  const packagingCost = parseDecimal(values.packagingCost)
  if (packagingCost === null) errors.packagingCost = 'number'
  else if (packagingCost > limits.money) errors.packagingCost = 'range'

  const extraCosts: CostRequest['extraCosts'] = []
  values.extras.forEach((extra, index) => {
    if (!extra.label.trim() && !extra.amount.trim()) return
    const label = extra.label.trim()
    const amount = parseDecimal(extra.amount)
    if (!label) errors[`extraLabel${index}`] = 'required'
    if (amount === null) errors[`extraAmount${index}`] = 'number'
    else if (amount > limits.money) errors[`extraAmount${index}`] = 'range'
    if (label && amount !== null) extraCosts.push({ label, amount })
  })

  const margin = parseDecimal(values.marginPercent)
  if (margin === null) errors.marginPercent = 'number'
  else if (margin >= 100) errors.marginPercent = 'margin'

  const fee = parseDecimal(values.feePercent)
  if (fee === null) errors.feePercent = 'number'
  else if (fee >= 100) errors.feePercent = 'margin'

  if (margin !== null && fee !== null && !errors.marginPercent && !errors.feePercent && margin + fee >= 100) {
    errors.marginPercent = 'marginPlusFee'
  }

  if (Object.keys(errors).length > 0) return { request: null, errors }

  return {
    request: {
      printerId: values.printerId,
      spoolId: values.spoolId,
      grams: grams!,
      printMinutes,
      labourMinutes: labourMinutes!,
      packagingCost: packagingCost!,
      extraCosts,
      marginPercent: margin!,
      feePercent: fee!,
    },
    errors,
  }
}

export function profitMarginPercent(breakdown: CostBreakdown): number {
  return breakdown.suggestedPrice > 0 ? Math.round((breakdown.profit / breakdown.suggestedPrice) * 1000) / 10 : 0
}

export function lineShare(line: CostLine, breakdown: CostBreakdown): number {
  return breakdown.totalCost > 0 ? Math.round((line.amount / breakdown.totalCost) * 100) : 0
}

export function defaultSnapshotName(printerName: string, grams: number, date = new Date()): string {
  return `${printerName} ${grams} g ${date.toISOString().slice(0, 10)}`
}
