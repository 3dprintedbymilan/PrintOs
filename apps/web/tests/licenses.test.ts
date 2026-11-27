import { describe, expect, it } from 'vitest'
import { describeExpiry, emptyLicenseForm, expiryKind, licenseStatusTone, validateLicenseForm, validateProofFile } from '../utils/licenses'
import type { LicenseAssessment } from '../utils/licenses'

const assessment = (daysUntilExpiry: number | null): LicenseAssessment => ({
  status: 'Valid',
  allowsCommercialSale: true,
  attributionRequired: false,
  daysUntilExpiry,
  unitsUsed: null,
  unitsRemaining: null,
  limitNearlyReached: false,
})

describe('validateLicenseForm', () => {
  it('requires a name', () => {
    const { request, errors } = validateLicenseForm(emptyLicenseForm())
    expect(request).toBeNull()
    expect(errors.name).toBe('required')
  })

  it('builds a request and trims blanks to null', () => {
    const { request } = validateLicenseForm({ ...emptyLicenseForm(), name: '  Benchy  ', notes: '  ', unitsLimit: '5' })
    expect(request).toMatchObject({ name: 'Benchy', notes: null, unitsLimit: null, type: 'Personal' })
  })

  it('accepts a units limit only for limited commercial licenses', () => {
    const { request } = validateLicenseForm({ ...emptyLicenseForm(), name: 'A', type: 'CommercialWithLimits', unitsLimit: '250' })
    expect(request?.unitsLimit).toBe(250)
  })

  it('rejects a non-integer units limit', () => {
    const { errors } = validateLicenseForm({ ...emptyLicenseForm(), name: 'A', type: 'CommercialWithLimits', unitsLimit: '2,5' })
    expect(errors.unitsLimit).toBe('number')
  })

  it('rejects an end date before the start date', () => {
    const { errors } = validateLicenseForm({ ...emptyLicenseForm(), name: 'A', validFrom: '2026-05-02', validUntil: '2026-05-01' })
    expect(errors.validUntil).toBe('dateOrder')
  })

  it('rejects a proof link that is not http(s)', () => {
    expect(validateLicenseForm({ ...emptyLicenseForm(), name: 'A', proofUrl: 'ftp://x' }).errors.proofUrl).toBe('url')
    expect(validateLicenseForm({ ...emptyLicenseForm(), name: 'A', proofUrl: 'https://example.com/l' }).errors.proofUrl).toBeUndefined()
  })
})

describe('validateProofFile', () => {
  it('accepts pdf and images and rejects others', () => {
    expect(validateProofFile({ name: 'l.PDF', size: 10 })).toBeNull()
    expect(validateProofFile({ name: 'l.png', size: 10 })).toBeNull()
    expect(validateProofFile({ name: 'l.exe', size: 10 })).toBe('type')
    expect(validateProofFile({ name: 'l.pdf', size: 0 })).toBe('empty')
    expect(validateProofFile({ name: 'l.pdf', size: 11 * 1024 * 1024 })).toBe('size')
  })
})

describe('status helpers', () => {
  it('maps statuses to tones', () => {
    expect(licenseStatusTone('Valid')).toBe('success')
    expect(licenseStatusTone('ExpiringSoon')).toBe('warning')
    expect(licenseStatusTone('Expired')).toBe('danger')
    expect(licenseStatusTone('PersonalOnly')).toBe('info')
    expect(licenseStatusTone('NoLicense')).toBe('neutral')
  })

  it('describes expiry', () => {
    const translate = (key: string, params?: Record<string, unknown>) => `${key}:${params?.days ?? ''}`
    expect(expiryKind(assessment(null))).toBe('none')
    expect(describeExpiry(translate, assessment(12))).toBe('licenses.expiry.future:12')
    expect(describeExpiry(translate, assessment(0))).toBe('licenses.expiry.today:')
    expect(describeExpiry(translate, assessment(-3))).toBe('licenses.expiry.past:3')
  })
})
