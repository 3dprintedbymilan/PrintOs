import { formatDecimal, parseDecimal } from './settingsForm'

export interface ProductSummary {
  id: string
  name: string
  description: string | null
  sku: string | null
  isArchived: boolean
  currency: string
  variantCount: number
  lowestPrice: number | null
  highestPrice: number | null
  createdAt: string
  commercialRightsStatus?: RightsStatus
  notClearedVariantCount?: number
}

export type RightsStatus = 'NoModel' | 'Cleared' | 'NotCleared'

export interface RightsModel {
  modelId: string
  modelName: string
  licenseName: string | null
  licenseStatus: string
  allowsCommercialSale: boolean
  attributionRequired: boolean
  daysUntilExpiry: number | null
}

export interface RightsAttribution {
  modelId: string
  modelName: string
  licenseName: string | null
  text: string | null
}

export interface CommercialRights {
  status: RightsStatus
  isCleared: boolean
  expiringSoon: boolean
  blockingModelCount: number
  models: RightsModel[]
  attributions: RightsAttribution[]
}

export type BomKind = 'filament' | 'print' | 'labour' | 'packaging' | 'extra'

export const bomKinds: BomKind[] = ['filament', 'print', 'labour', 'packaging', 'extra']

export interface BomLine {
  id: string
  kind: BomKind
  label: string | null
  materialId: string | null
  materialName: string | null
  spoolId: string | null
  spoolLabel: string | null
  grams: number | null
  printerId: string | null
  printerName: string | null
  modelId: string | null
  modelName: string | null
  minutes: number | null
  amount: number | null
}

export interface VariantCostLine {
  kind: 'material' | 'electricity' | 'machine' | 'labour' | 'packaging' | 'extra'
  label: string
  quantity: number
  unit: string
  unitCost: number
  amount: number
}

export interface VariantCost {
  currency: string
  lines: VariantCostLine[]
  totalCost: number
  price: number
  targetMarginPercent: number
  feePercent: number
  suggestedPrice: number
  feeAmount: number
  profit: number
  marginPercent: number | null
  unpricedMaterials: string[]
}

export interface Variant {
  id: string
  productId: string
  name: string
  sku: string | null
  price: number
  targetMarginPercent: number
  feePercent: number
  bomLines: BomLine[]
  cost: VariantCost
  readyForSale?: boolean
  saleRiskAcknowledged?: boolean
  commercialRights?: CommercialRights
}

export interface Product {
  id: string
  name: string
  description: string | null
  sku: string | null
  isArchived: boolean
  createdAt: string
  currency: string
  variants: Variant[]
  commercialRightsStatus?: RightsStatus
}

export interface VariantCostSnapshot {
  id: string
  variantId: string
  createdAt: string
  currency: string
  totalCost: number
  price: number
  suggestedPrice: number
  profit: number
  marginPercent: number | null
  breakdown: VariantCost
}

export interface ProductRequest {
  name: string
  description: string | null
  sku: string | null
}

export interface VariantRequest {
  name: string
  sku: string | null
  price: number
  targetMarginPercent: number
  feePercent: number
}

export interface BomLineRequest {
  kind: BomKind
  label: string | null
  materialId: string | null
  spoolId: string | null
  grams: number | null
  printerId: string | null
  modelId: string | null
  minutes: number | null
  amount: number | null
}

export type ErrorKind = 'required' | 'number' | 'range' | 'integer' | 'length'

export const limits = {
  name: 100,
  sku: 40,
  description: 2000,
  label: 60,
  money: 1_000_000,
  grams: 100_000,
  minutes: 100_000,
} as const

export interface ProductFormValues {
  name: string
  sku: string
  description: string
}

export type ProductField = 'name' | 'sku' | 'description'

export function emptyProductForm(): ProductFormValues {
  return { name: '', sku: '', description: '' }
}

export function productToForm(product: { name: string, sku: string | null, description: string | null }): ProductFormValues {
  return { name: product.name, sku: product.sku ?? '', description: product.description ?? '' }
}

export function validateProductForm(values: ProductFormValues): { request: ProductRequest | null, errors: Partial<Record<ProductField, ErrorKind>> } {
  const errors: Partial<Record<ProductField, ErrorKind>> = {}
  const name = values.name.trim()
  if (!name) errors.name = 'required'
  else if (name.length > limits.name) errors.name = 'length'
  if (values.sku.trim().length > limits.sku) errors.sku = 'length'
  if (values.description.trim().length > limits.description) errors.description = 'length'
  if (Object.keys(errors).length > 0) return { request: null, errors }

  return {
    request: { name, sku: values.sku.trim() || null, description: values.description.trim() || null },
    errors,
  }
}

export interface VariantFormValues {
  name: string
  sku: string
  price: string
  targetMargin: string
  fee: string
}

export type VariantField = 'name' | 'sku' | 'price' | 'targetMargin' | 'fee'

export function emptyVariantForm(): VariantFormValues {
  return { name: '', sku: '', price: '', targetMargin: '40', fee: '0' }
}

export function variantToForm(variant: Pick<Variant, 'name' | 'sku' | 'price' | 'targetMarginPercent' | 'feePercent'>): VariantFormValues {
  return {
    name: variant.name,
    sku: variant.sku ?? '',
    price: formatDecimal(variant.price),
    targetMargin: formatDecimal(variant.targetMarginPercent),
    fee: formatDecimal(variant.feePercent),
  }
}

export function validateVariantForm(values: VariantFormValues): { request: VariantRequest | null, errors: Partial<Record<VariantField, ErrorKind>> } {
  const errors: Partial<Record<VariantField, ErrorKind>> = {}
  const name = values.name.trim()
  if (!name) errors.name = 'required'
  else if (name.length > limits.name) errors.name = 'length'
  if (values.sku.trim().length > limits.sku) errors.sku = 'length'

  const price = parseDecimal(values.price)
  if (price === null) errors.price = 'number'
  else if (price > limits.money) errors.price = 'range'

  const margin = parseDecimal(values.targetMargin)
  if (margin === null) errors.targetMargin = 'number'
  else if (margin >= 100) errors.targetMargin = 'range'

  const fee = parseDecimal(values.fee)
  if (fee === null) errors.fee = 'number'
  else if (fee >= 100) errors.fee = 'range'

  if (margin !== null && fee !== null && !errors.targetMargin && !errors.fee && margin + fee >= 100) errors.targetMargin = 'range'

  if (Object.keys(errors).length > 0) return { request: null, errors }

  return {
    request: { name, sku: values.sku.trim() || null, price: price!, targetMarginPercent: margin!, feePercent: fee! },
    errors,
  }
}

export interface BomLineFormValues {
  kind: BomKind
  source: 'spool' | 'material'
  spoolId: string
  materialId: string
  grams: string
  printerId: string
  modelId: string
  minutes: string
  label: string
  amount: string
}

export type BomLineField = 'spoolId' | 'materialId' | 'grams' | 'printerId' | 'minutes' | 'label' | 'amount'

export function emptyBomLineForm(kind: BomKind = 'filament'): BomLineFormValues {
  return { kind, source: 'spool', spoolId: '', materialId: '', grams: '', printerId: '', modelId: '', minutes: '', label: '', amount: '' }
}

export function lineToForm(line: BomLine): BomLineFormValues {
  return {
    kind: line.kind,
    source: line.materialId ? 'material' : 'spool',
    spoolId: line.spoolId ?? '',
    materialId: line.materialId ?? '',
    grams: line.grams === null ? '' : formatDecimal(line.grams),
    printerId: line.printerId ?? '',
    modelId: line.modelId ?? '',
    minutes: line.minutes === null ? '' : String(line.minutes),
    label: line.label ?? '',
    amount: line.amount === null ? '' : formatDecimal(line.amount),
  }
}

function positiveInteger(input: string): { value: number | null, error: ErrorKind | null } {
  const parsed = parseDecimal(input)
  if (parsed === null) return { value: null, error: 'number' }
  if (!Number.isInteger(parsed)) return { value: null, error: 'integer' }
  if (parsed <= 0 || parsed > limits.minutes) return { value: null, error: 'range' }
  return { value: parsed, error: null }
}

export function validateBomLineForm(values: BomLineFormValues): { request: BomLineRequest | null, errors: Partial<Record<BomLineField, ErrorKind>> } {
  const errors: Partial<Record<BomLineField, ErrorKind>> = {}
  const request: BomLineRequest = {
    kind: values.kind,
    label: values.label.trim() || null,
    materialId: null,
    spoolId: null,
    grams: null,
    printerId: null,
    modelId: null,
    minutes: null,
    amount: null,
  }

  if (values.label.trim().length > limits.label) errors.label = 'length'

  if (values.kind === 'filament') {
    request.label = null
    if (values.source === 'spool') {
      if (!values.spoolId) errors.spoolId = 'required'
      request.spoolId = values.spoolId || null
    }
    else {
      if (!values.materialId) errors.materialId = 'required'
      request.materialId = values.materialId || null
    }
    const grams = parseDecimal(values.grams)
    if (grams === null) errors.grams = 'number'
    else if (grams <= 0 || grams > limits.grams) errors.grams = 'range'
    else request.grams = grams
  }
  else if (values.kind === 'print') {
    request.label = null
    if (!values.printerId) errors.printerId = 'required'
    request.printerId = values.printerId || null
    request.modelId = values.modelId || null
    const minutes = positiveInteger(values.minutes)
    if (minutes.error) errors.minutes = minutes.error
    else request.minutes = minutes.value
  }
  else if (values.kind === 'labour') {
    request.label = null
    const minutes = positiveInteger(values.minutes)
    if (minutes.error) errors.minutes = minutes.error
    else request.minutes = minutes.value
  }
  else {
    if (values.kind === 'extra' && !values.label.trim()) errors.label = 'required'
    const amount = parseDecimal(values.amount)
    if (amount === null) errors.amount = 'number'
    else if (amount > limits.money) errors.amount = 'range'
    else request.amount = amount
  }

  return Object.keys(errors).length > 0 ? { request: null, errors } : { request, errors }
}

export type MarginTone = 'success' | 'warning' | 'danger' | 'neutral'

export function marginTone(marginPercent: number | null): MarginTone {
  if (marginPercent === null) return 'neutral'
  if (marginPercent < 0) return 'danger'
  if (marginPercent < 20) return 'warning'
  return 'success'
}

export function priceRange(product: Pick<ProductSummary, 'lowestPrice' | 'highestPrice'>): { low: number, high: number } | null {
  if (product.lowestPrice === null || product.highestPrice === null) return null
  return { low: product.lowestPrice, high: product.highestPrice }
}

export type RightsTone = 'success' | 'warning' | 'danger' | 'neutral'

export function rightsTone(status: RightsStatus | undefined): RightsTone {
  if (status === 'Cleared') return 'success'
  if (status === 'NotCleared') return 'danger'
  return 'neutral'
}

export function isNotCleared(rights: CommercialRights | undefined): boolean {
  return rights?.status === 'NotCleared'
}

export function blockingModels(rights: CommercialRights | undefined): RightsModel[] {
  return rights?.models.filter(model => !model.allowsCommercialSale) ?? []
}

export function saleToggleAction(variant: Pick<Variant, 'readyForSale' | 'commercialRights'>): 'markReady' | 'markReadyAtRisk' | 'unmark' {
  if (variant.readyForSale) return 'unmark'
  return isNotCleared(variant.commercialRights) ? 'markReadyAtRisk' : 'markReady'
}

export function saleRiskBody(readyForSale: boolean, acknowledged: boolean): { readyForSale: boolean, acknowledgeRisk: boolean } {
  return { readyForSale, acknowledgeRisk: readyForSale && acknowledged }
}
