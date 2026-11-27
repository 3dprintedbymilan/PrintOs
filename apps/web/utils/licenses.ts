import { parseDecimal } from './settingsForm'

export const licenseSources = ['OwnDesign', 'MakerWorld', 'Printables', 'Thingiverse', 'Other'] as const
export const licenseTypes = ['Personal', 'Commercial', 'CommercialWithLimits'] as const
export type LicenseSource = typeof licenseSources[number]
export type LicenseType = typeof licenseTypes[number]
export type LicenseStatusName = 'Valid' | 'ExpiringSoon' | 'Expired' | 'PersonalOnly' | 'LimitReached' | 'NotYetValid' | 'NoLicense'

export interface LicenseAssessment {
  status: LicenseStatusName
  allowsCommercialSale: boolean
  attributionRequired: boolean
  daysUntilExpiry: number | null
  unitsUsed: number | null
  unitsRemaining: number | null
  limitNearlyReached: boolean
}

export interface License {
  id: string
  name: string
  source: LicenseSource
  type: LicenseType
  attributionRequired: boolean
  attributionText: string | null
  unitsLimit: number | null
  validFrom: string | null
  validUntil: string | null
  notes: string | null
  proofUrl: string | null
  hasProofFile: boolean
  proofFileName: string | null
  modelCount: number
  assessment: LicenseAssessment
  createdAt: string
  updatedAt: string
}

export interface LicenseDetail {
  license: License
  models: { id: string, name: string }[]
}

export interface LicenseSummary {
  id: string
  name: string
  source: LicenseSource
  type: LicenseType
  attributionRequired: boolean
  attributionText: string | null
  validUntil: string | null
  assessment: LicenseAssessment
}

export interface LicenseRequest {
  name: string
  source: LicenseSource
  type: LicenseType
  attributionRequired: boolean
  attributionText: string | null
  unitsLimit: number | null
  validFrom: string | null
  validUntil: string | null
  notes: string | null
  proofUrl: string | null
}

export interface LicenseFormValues {
  name: string
  source: LicenseSource
  type: LicenseType
  attributionRequired: boolean
  attributionText: string
  unitsLimit: string
  validFrom: string
  validUntil: string
  notes: string
  proofUrl: string
}

export type LicenseField = 'name' | 'attributionText' | 'unitsLimit' | 'validUntil' | 'proofUrl'
export type LicenseErrorKind = 'required' | 'number' | 'dateOrder' | 'url'

export const maxProofBytes = 10 * 1024 * 1024
export const acceptedProofAttribute = '.pdf,.png,.jpg,.jpeg,.webp'

export type LicenseStatusTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral'

export function licenseStatusTone(status: LicenseStatusName): LicenseStatusTone {
  switch (status) {
    case 'Valid': return 'success'
    case 'ExpiringSoon':
    case 'NotYetValid': return 'warning'
    case 'Expired':
    case 'LimitReached': return 'danger'
    case 'PersonalOnly': return 'info'
    default: return 'neutral'
  }
}

export function allowsCommercialUse(type: LicenseType): boolean {
  return type !== 'Personal'
}

export function emptyLicenseForm(): LicenseFormValues {
  return {
    name: '',
    source: 'Printables',
    type: 'Personal',
    attributionRequired: false,
    attributionText: '',
    unitsLimit: '',
    validFrom: '',
    validUntil: '',
    notes: '',
    proofUrl: '',
  }
}

export function licenseToForm(license: License): LicenseFormValues {
  return {
    name: license.name,
    source: license.source,
    type: license.type,
    attributionRequired: license.attributionRequired,
    attributionText: license.attributionText ?? '',
    unitsLimit: license.unitsLimit === null ? '' : String(license.unitsLimit),
    validFrom: license.validFrom ?? '',
    validUntil: license.validUntil ?? '',
    notes: license.notes ?? '',
    proofUrl: license.proofUrl ?? '',
  }
}

function blankToNull(value: string): string | null {
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : null
}

export function validateLicenseForm(values: LicenseFormValues): { request: LicenseRequest | null, errors: Partial<Record<LicenseField, LicenseErrorKind>> } {
  const errors: Partial<Record<LicenseField, LicenseErrorKind>> = {}
  const name = values.name.trim()
  if (name.length === 0 || name.length > 100) errors.name = 'required'

  let unitsLimit: number | null = null
  if (values.type === 'CommercialWithLimits' && values.unitsLimit.trim().length > 0) {
    const parsed = parseDecimal(values.unitsLimit)
    if (parsed === null || !Number.isInteger(parsed) || parsed < 1 || parsed > 1_000_000) errors.unitsLimit = 'number'
    else unitsLimit = parsed
  }

  if (values.validFrom && values.validUntil && values.validUntil < values.validFrom) errors.validUntil = 'dateOrder'

  const proofUrl = blankToNull(values.proofUrl)
  if (proofUrl && !/^https?:\/\/\S+$/i.test(proofUrl)) errors.proofUrl = 'url'

  if (Object.keys(errors).length > 0) return { request: null, errors }

  return {
    request: {
      name,
      source: values.source,
      type: values.type,
      attributionRequired: values.attributionRequired,
      attributionText: blankToNull(values.attributionText),
      unitsLimit,
      validFrom: blankToNull(values.validFrom),
      validUntil: blankToNull(values.validUntil),
      notes: blankToNull(values.notes),
      proofUrl,
    },
    errors,
  }
}

export function validateProofFile(file: { name: string, size: number }): 'type' | 'empty' | 'size' | null {
  const dot = file.name.lastIndexOf('.')
  const extension = dot < 0 ? '' : file.name.slice(dot + 1).toLowerCase()
  if (!['pdf', 'png', 'jpg', 'jpeg', 'webp'].includes(extension)) return 'type'
  if (file.size === 0) return 'empty'
  if (file.size > maxProofBytes) return 'size'
  return null
}

export function describeExpiry(translate: (key: string, params?: Record<string, unknown>, plural?: number) => string, assessment: LicenseAssessment): string {
  const days = assessment.daysUntilExpiry
  switch (expiryKind(assessment)) {
    case 'future': return translate('licenses.expiry.future', { days }, days ?? 0)
    case 'today': return translate('licenses.expiry.today')
    case 'past': return translate('licenses.expiry.past', { days: Math.abs(days ?? 0) }, Math.abs(days ?? 0))
    default: return translate('licenses.expiry.none')
  }
}

export function expiryKind(assessment: LicenseAssessment): 'none' | 'future' | 'today' | 'past' {
  const days = assessment.daysUntilExpiry
  if (days === null) return 'none'
  if (days < 0) return 'past'
  return days === 0 ? 'today' : 'future'
}
