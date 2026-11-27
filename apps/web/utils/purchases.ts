import { parseDecimal } from './settingsForm'
import { newMaterialValue, newSupplierValue } from './inventory'

export type PurchaseStatus = 'Ordered' | 'PartiallyReceived' | 'Received' | 'Cancelled'

export interface PurchaseSummary {
  id: string
  supplierId: string
  supplierName: string
  purchasedOn: string
  invoiceReference: string | null
  status: PurchaseStatus
  total: number
  currency: string
  lineCount: number
  orderedQuantity: number
  receivedQuantity: number
}

export interface PurchaseLine {
  id: string
  materialId: string
  materialName: string
  colorName: string
  colorHex: string | null
  quantity: number
  receivedQuantity: number
  weightGramsPerSpool: number
  unitPrice: number
  landedUnitCost: number
  costPerKg: number
  lineTotal: number
}

export interface PurchaseDetail {
  id: string
  supplierId: string
  supplierName: string
  purchasedOn: string
  invoiceReference: string | null
  notes: string | null
  status: PurchaseStatus
  currency: string
  subtotal: number
  shippingCost: number
  discount: number
  total: number
  hasReceiptFile: boolean
  receiptFileName: string | null
  lines: PurchaseLine[]
  createdAt: string
}

export const purchaseStatuses: PurchaseStatus[] = ['Ordered', 'PartiallyReceived', 'Received', 'Cancelled']

export function purchaseStatusTone(status: PurchaseStatus): 'success' | 'warning' | 'danger' | 'info' | 'neutral' {
  if (status === 'Received') return 'success'
  if (status === 'PartiallyReceived') return 'warning'
  if (status === 'Cancelled') return 'neutral'
  return 'info'
}

export function remainingToReceive(purchase: Pick<PurchaseDetail, 'lines'>): number {
  return purchase.lines.reduce((sum, line) => sum + Math.max(0, line.quantity - line.receivedQuantity), 0)
}

export type PurchaseField = 'supplierName' | 'purchasedOn' | 'invoiceReference' | 'notes' | 'shippingCost' | 'discount' | 'lines'
export type LineField = 'materialName' | 'materialType' | 'colorName' | 'colorHex' | 'quantity' | 'weight' | 'unitPrice'
export type PurchaseErrorKind = 'required' | 'number' | 'range' | 'hex' | 'date' | 'integer' | 'negativeTotal' | 'tooLong'

export interface LineFormValues {
  key: number
  materialId: string
  materialName: string
  materialType: string
  colorName: string
  colorHex: string
  quantity: string
  weight: string
  unitPrice: string
}

export interface PurchaseFormValues {
  supplierId: string
  supplierName: string
  purchasedOn: string
  invoiceReference: string
  notes: string
  shippingCost: string
  discount: string
  receiveNow: boolean
  lines: LineFormValues[]
}

export function emptyLine(key: number, materialId = ''): LineFormValues {
  return { key, materialId, materialName: '', materialType: '', colorName: '', colorHex: '', quantity: '1', weight: '1000', unitPrice: '' }
}

export function todayIso(now = new Date()): string {
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const day = String(now.getDate()).padStart(2, '0')
  return `${now.getFullYear()}-${month}-${day}`
}

export interface ValidatedPurchase {
  newSupplier: { name: string } | null
  supplierId: string | null
  newMaterials: { key: number, name: string, type: string }[]
  request: {
    purchasedOn: string
    invoiceReference: string | null
    notes: string | null
    shippingCost: number
    discount: number
    receiveImmediately: boolean
    lines: { key: number, materialId: string | null, colorName: string, colorHex: string | null, quantity: number, weightGramsPerSpool: number, unitPrice: number }[]
  }
}

export type PurchaseErrors = Partial<Record<PurchaseField, PurchaseErrorKind>> & { lineErrors?: Record<number, Partial<Record<LineField, PurchaseErrorKind>>> }

function integerInRange(text: string, min: number, max: number): { value: number | null, kind?: PurchaseErrorKind } {
  const value = parseDecimal(text)
  if (value === null) return { value: null, kind: 'number' }
  if (!Number.isInteger(value)) return { value: null, kind: 'integer' }
  if (value < min || value > max) return { value: null, kind: 'range' }
  return { value }
}

export function purchaseTotals(values: Pick<PurchaseFormValues, 'lines' | 'shippingCost' | 'discount'>): { subtotal: number, total: number } {
  const subtotal = values.lines.reduce((sum, line) => {
    const quantity = parseDecimal(line.quantity)
    const price = parseDecimal(line.unitPrice)
    return quantity !== null && price !== null ? sum + quantity * price : sum
  }, 0)
  const shipping = parseDecimal(values.shippingCost) ?? 0
  const discount = parseDecimal(values.discount) ?? 0
  return { subtotal: Math.round(subtotal * 100) / 100, total: Math.round((subtotal + shipping - discount) * 100) / 100 }
}

export function validatePurchaseForm(values: PurchaseFormValues, today = todayIso()): { purchase: ValidatedPurchase | null, errors: PurchaseErrors } {
  const errors: PurchaseErrors = {}
  const lineErrors: Record<number, Partial<Record<LineField, PurchaseErrorKind>>> = {}

  if (values.supplierId === newSupplierValue && !values.supplierName.trim()) errors.supplierName = 'required'
  if (!/^\d{4}-\d{2}-\d{2}$/.test(values.purchasedOn) || Number.isNaN(Date.parse(values.purchasedOn))) errors.purchasedOn = 'date'
  else if (values.purchasedOn > today || values.purchasedOn < '2000-01-01') errors.purchasedOn = 'date'
  if (values.invoiceReference.trim().length > 100) errors.invoiceReference = 'tooLong'
  if (values.notes.trim().length > 1000) errors.notes = 'tooLong'

  const shipping = values.shippingCost.trim() ? parseDecimal(values.shippingCost) : 0
  if (shipping === null) errors.shippingCost = 'number'
  else if (shipping > 100_000) errors.shippingCost = 'range'
  const discount = values.discount.trim() ? parseDecimal(values.discount) : 0
  if (discount === null) errors.discount = 'number'
  else if (discount > 100_000) errors.discount = 'range'

  if (values.lines.length === 0) errors.lines = 'required'

  const lines: ValidatedPurchase['request']['lines'] = []
  const newMaterials: ValidatedPurchase['newMaterials'] = []
  for (const line of values.lines) {
    const problems: Partial<Record<LineField, PurchaseErrorKind>> = {}
    const creating = line.materialId === newMaterialValue
    if (creating) {
      if (!line.materialName.trim()) problems.materialName = 'required'
      if (!line.materialType.trim()) problems.materialType = 'required'
    }
    if (!line.colorName.trim()) problems.colorName = 'required'
    else if (line.colorName.trim().length > 60) problems.colorName = 'tooLong'
    const hex = line.colorHex.trim()
    if (hex && !/^#[0-9A-Fa-f]{6}$/.test(hex)) problems.colorHex = 'hex'
    const quantity = integerInRange(line.quantity, 1, 1000)
    if (quantity.kind) problems.quantity = quantity.kind
    const weight = integerInRange(line.weight, 1, 100_000)
    if (weight.kind) problems.weight = weight.kind
    const price = parseDecimal(line.unitPrice)
    if (price === null) problems.unitPrice = 'number'
    else if (price > 100_000) problems.unitPrice = 'range'

    if (Object.keys(problems).length > 0) {
      lineErrors[line.key] = problems
      continue
    }
    if (creating) newMaterials.push({ key: line.key, name: line.materialName.trim(), type: line.materialType.trim() })
    lines.push({
      key: line.key,
      materialId: creating ? null : line.materialId,
      colorName: line.colorName.trim(),
      colorHex: hex || null,
      quantity: quantity.value!,
      weightGramsPerSpool: weight.value!,
      unitPrice: price!,
    })
  }

  if (Object.keys(lineErrors).length > 0) errors.lineErrors = lineErrors
  if (!errors.discount && !errors.shippingCost && !errors.lineErrors && !errors.lines && purchaseTotals(values).total < 0) errors.discount = 'negativeTotal'

  const hasLineErrors = errors.lineErrors !== undefined
  const hasFieldErrors = Object.keys(errors).some(key => key !== 'lineErrors')
  if (hasLineErrors || hasFieldErrors) return { purchase: null, errors }

  return {
    purchase: {
      newSupplier: values.supplierId === newSupplierValue ? { name: values.supplierName.trim() } : null,
      supplierId: values.supplierId === newSupplierValue ? null : values.supplierId,
      newMaterials,
      request: {
        purchasedOn: values.purchasedOn,
        invoiceReference: values.invoiceReference.trim() || null,
        notes: values.notes.trim() || null,
        shippingCost: shipping ?? 0,
        discount: discount ?? 0,
        receiveImmediately: values.receiveNow,
        lines,
      },
    },
    errors,
  }
}

export interface PricePoint {
  purchaseId: string
  supplierId: string
  supplierName: string
  purchasedOn: string
  costPerKg: number
}

export interface SupplierPrice {
  supplierId: string
  supplierName: string
  lastCostPerKg: number
  lastPurchasedOn: string
  previousCostPerKg: number | null
  changePercent: number | null
  purchaseCount: number
  isCheapest: boolean
}

export interface MaterialPriceHistory {
  materialId: string
  materialName: string
  currency: string
  points: PricePoint[]
  latest: PricePoint | null
  previousCostPerKg: number | null
  changePercent: number | null
  cheapest: SupplierPrice | null
  suppliers: SupplierPrice[]
}

export interface SupplierPriceGroup {
  supplierId: string
  supplierName: string
  rows: { materialId: string, materialName: string, price: SupplierPrice, currency: string }[]
}

export function groupBySupplier(histories: MaterialPriceHistory[]): SupplierPriceGroup[] {
  const groups = new Map<string, SupplierPriceGroup>()
  for (const history of histories) {
    for (const price of history.suppliers) {
      const group = groups.get(price.supplierId) ?? { supplierId: price.supplierId, supplierName: price.supplierName, rows: [] }
      group.rows.push({ materialId: history.materialId, materialName: history.materialName, price, currency: history.currency })
      groups.set(price.supplierId, group)
    }
  }
  return [...groups.values()]
    .map(group => ({ ...group, rows: group.rows.sort((a, b) => a.materialName.localeCompare(b.materialName)) }))
    .sort((a, b) => a.supplierName.localeCompare(b.supplierName))
}

export function sparkline(points: Pick<PricePoint, 'costPerKg'>[], width: number, height: number, padding = 4): { path: string, last: { x: number, y: number } | null } {
  if (points.length === 0) return { path: '', last: null }
  const values = points.map(point => point.costPerKg)
  const min = Math.min(...values)
  const max = Math.max(...values)
  const span = max - min
  const coordinates = values.map((value, index) => ({
    x: points.length === 1 ? width / 2 : padding + (index / (points.length - 1)) * (width - padding * 2),
    y: span === 0 ? height / 2 : height - padding - ((value - min) / span) * (height - padding * 2),
  }))
  const round = (value: number) => Math.round(value * 10) / 10
  return {
    path: coordinates.map((point, index) => `${index === 0 ? 'M' : 'L'}${round(point.x)} ${round(point.y)}`).join(' '),
    last: coordinates[coordinates.length - 1] ?? null,
  }
}

export function changeTone(percent: number | null): 'up' | 'down' | 'flat' {
  if (percent === null || percent === 0) return 'flat'
  return percent > 0 ? 'up' : 'down'
}
