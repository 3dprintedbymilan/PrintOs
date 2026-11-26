import { parseDecimal } from './settingsForm'

export interface Spool {
  id: string
  materialId: string
  materialName: string
  supplierId: string | null
  supplierName: string | null
  colorName: string
  colorHex: string | null
  cost: number
  costPerGram: number
  initialWeightGrams: number
  remainingWeightGrams: number
  isArchived: boolean
}

export interface Material {
  id: string
  name: string
  type: string
}

export interface Supplier {
  id: string
  name: string
  website: string | null
}

export interface Movement {
  id: string
  kind: 'Initial' | 'Adjustment' | 'Consumption'
  deltaGrams: number
  balanceAfterGrams: number
  reason: string
  createdAt: string
}

export interface StockSummary {
  totalKilograms: number
  materialCount: number
  spoolCount: number
}

export function summarizeStock(spools: Spool[]): StockSummary {
  const active = spools.filter(spool => !spool.isArchived)
  const grams = active.reduce((sum, spool) => sum + spool.remainingWeightGrams, 0)
  return {
    totalKilograms: Math.round(grams / 10) / 100,
    materialCount: new Set(active.map(spool => spool.materialId)).size,
    spoolCount: active.length,
  }
}

export function filterSpools(spools: Spool[], materialId: string, search: string): Spool[] {
  const needle = search.trim().toLowerCase()
  return spools.filter((spool) => {
    if (materialId && spool.materialId !== materialId) return false
    if (!needle) return true
    return [spool.colorName, spool.materialName, spool.supplierName ?? ''].some(text => text.toLowerCase().includes(needle))
  })
}

export function remainingPercent(spool: Spool): number {
  return spool.initialWeightGrams > 0 ? Math.round((spool.remainingWeightGrams / spool.initialWeightGrams) * 100) : 0
}

export type SpoolErrorKind = 'required' | 'number' | 'range' | 'hex'
export type SpoolField = 'colorName' | 'colorHex' | 'cost' | 'weight' | 'materialName' | 'materialType' | 'supplierName'

export interface SpoolFormValues {
  materialId: string
  materialName: string
  materialType: string
  supplierId: string
  supplierName: string
  colorName: string
  colorHex: string
  cost: string
  weight: string
}

export const newMaterialValue = '__new__'
export const newSupplierValue = '__new__'

export interface ValidatedSpool {
  newMaterial: { name: string, type: string } | null
  newSupplier: { name: string } | null
  materialId: string | null
  supplierId: string | null
  colorName: string
  colorHex: string | null
  cost: number
  initialWeightGrams: number
}

export function validateSpoolForm(values: SpoolFormValues): { spool: ValidatedSpool | null, errors: Partial<Record<SpoolField, SpoolErrorKind>> } {
  const errors: Partial<Record<SpoolField, SpoolErrorKind>> = {}
  const creatingMaterial = values.materialId === newMaterialValue
  const creatingSupplier = values.supplierId === newSupplierValue

  if (creatingMaterial) {
    if (!values.materialName.trim()) errors.materialName = 'required'
    if (!values.materialType.trim()) errors.materialType = 'required'
  }
  if (creatingSupplier && !values.supplierName.trim()) errors.supplierName = 'required'
  if (!values.colorName.trim()) errors.colorName = 'required'

  const hex = values.colorHex.trim()
  if (hex && !/^#[0-9A-Fa-f]{6}$/.test(hex)) errors.colorHex = 'hex'

  const cost = parseDecimal(values.cost)
  if (cost === null) errors.cost = 'number'
  else if (cost > 100_000) errors.cost = 'range'

  const weight = parseDecimal(values.weight)
  if (weight === null || !Number.isInteger(weight)) errors.weight = 'number'
  else if (weight < 1 || weight > 100_000) errors.weight = 'range'

  if (Object.keys(errors).length > 0) return { spool: null, errors }

  return {
    spool: {
      newMaterial: creatingMaterial ? { name: values.materialName.trim(), type: values.materialType.trim() } : null,
      newSupplier: creatingSupplier ? { name: values.supplierName.trim() } : null,
      materialId: creatingMaterial ? null : values.materialId,
      supplierId: creatingSupplier || !values.supplierId ? null : values.supplierId,
      colorName: values.colorName.trim(),
      colorHex: hex || null,
      cost: cost!,
      initialWeightGrams: weight!,
    },
    errors,
  }
}

export type AdjustmentErrorKind = 'amount' | 'reason'

export function validateAdjustment(direction: 'add' | 'remove', amount: string, reason: string): {
  adjustment: { deltaGrams: number, reason: string } | null
  errors: Partial<Record<'amount' | 'reason', AdjustmentErrorKind>>
} {
  const errors: Partial<Record<'amount' | 'reason', AdjustmentErrorKind>> = {}
  const grams = parseDecimal(amount)
  if (grams === null || !Number.isInteger(grams) || grams < 1 || grams > 100_000) errors.amount = 'amount'
  if (!reason.trim()) errors.reason = 'reason'
  if (Object.keys(errors).length > 0) return { adjustment: null, errors }
  return { adjustment: { deltaGrams: direction === 'remove' ? -grams! : grams!, reason: reason.trim() }, errors }
}
