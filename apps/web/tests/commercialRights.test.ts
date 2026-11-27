import { describe, expect, it } from 'vitest'
import { blockingModels, isNotCleared, rightsTone, saleRiskBody, saleToggleAction } from '../utils/products'
import type { CommercialRights, RightsModel } from '../utils/products'
import { jobToFormValues, validateJobForm } from '../utils/printJobs'

function model(overrides: Partial<RightsModel>): RightsModel {
  return { modelId: 'm1', modelName: 'Vase', licenseName: null, licenseStatus: 'NoLicense', allowsCommercialSale: false, attributionRequired: false, daysUntilExpiry: null, ...overrides }
}

function rights(status: CommercialRights['status'], models: RightsModel[] = []): CommercialRights {
  return { status, isCleared: status === 'Cleared', expiringSoon: false, blockingModelCount: models.filter(item => !item.allowsCommercialSale).length, models, attributions: [] }
}

describe('commercial rights helpers', () => {
  it('maps status to tone', () => {
    expect(rightsTone('Cleared')).toBe('success')
    expect(rightsTone('NotCleared')).toBe('danger')
    expect(rightsTone('NoModel')).toBe('neutral')
    expect(rightsTone(undefined)).toBe('neutral')
  })

  it('lists only the blocking models', () => {
    const value = rights('NotCleared', [model({}), model({ modelId: 'm2', modelName: 'Ok', allowsCommercialSale: true, licenseStatus: 'Valid' })])
    expect(blockingModels(value).map(item => item.modelId)).toEqual(['m1'])
    expect(isNotCleared(value)).toBe(true)
    expect(isNotCleared(undefined)).toBe(false)
  })

  it('chooses the sale action from readiness and rights', () => {
    expect(saleToggleAction({ readyForSale: false, commercialRights: rights('Cleared') })).toBe('markReady')
    expect(saleToggleAction({ readyForSale: false, commercialRights: rights('NoModel') })).toBe('markReady')
    expect(saleToggleAction({ readyForSale: false, commercialRights: rights('NotCleared') })).toBe('markReadyAtRisk')
    expect(saleToggleAction({ readyForSale: true, commercialRights: rights('NotCleared') })).toBe('unmark')
    expect(saleToggleAction({})).toBe('markReady')
  })

  it('only acknowledges risk when marking ready', () => {
    expect(saleRiskBody(true, true)).toEqual({ readyForSale: true, acknowledgeRisk: true })
    expect(saleRiskBody(true, false)).toEqual({ readyForSale: true, acknowledgeRisk: false })
    expect(saleRiskBody(false, true)).toEqual({ readyForSale: false, acknowledgeRisk: false })
  })
})

describe('print job model link', () => {
  const base = { name: 'Run', printerId: 'p1', modelId: '', status: 'Planned' as const, startedAt: '', finishedAt: '', finishedUnits: '', notes: '', lines: [] }

  it('sends null without a model and the id with one', () => {
    expect(validateJobForm(base).body?.modelId).toBeNull()
    expect(validateJobForm({ ...base, modelId: 'm9' }).body?.modelId).toBe('m9')
  })

  it('prefills the picker from an existing job', () => {
    const values = jobToFormValues({
      id: 'j', printerId: 'p1', printerName: 'A1', name: 'Run', status: 'Planned', startedAt: null, finishedAt: null, durationMinutes: null, notes: null,
      finishedUnits: null, externalSource: null, externalId: null, modelId: 'm9', totalGrams: 0, wasteGrams: 0, createdAt: '', filaments: [],
    })
    expect(values.modelId).toBe('m9')
  })
})
