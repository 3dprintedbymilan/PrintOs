import { describe, expect, it } from 'vitest'
import { filterPrintJobs, formatDuration, mapServerErrors, statusTone, jobToFormValues, validateJobForm } from '../utils/printJobs'
import type { JobFormValues, PrintJob } from '../utils/printJobs'

function job(overrides: Partial<PrintJob>): PrintJob {
  return {
    id: 'j1',
    printerId: 'p1',
    printerName: 'A1 #1',
    name: 'Benchy',
    status: 'Done',
    startedAt: '2026-11-26T08:00:00Z',
    finishedAt: '2026-11-26T09:35:00Z',
    durationMinutes: 95,
    notes: null,
    finishedUnits: null,
    externalSource: null,
    externalId: null,
    totalGrams: 42,
    wasteGrams: 4,
    createdAt: '2026-11-26T07:00:00Z',
    filaments: [{ spoolId: 's1', materialName: 'PLA Basic', colorName: 'Jade White', colorHex: null, grams: 42, wasteGrams: 4 }],
    ...overrides,
  }
}

function form(overrides: Partial<JobFormValues>): JobFormValues {
  return {
    name: 'Benchy',
    printerId: 'p1',
    status: 'Done',
    startedAt: '2026-11-26T08:00',
    finishedAt: '2026-11-26T09:35',
    finishedUnits: '',
    notes: '',
    lines: [{ key: 1, spoolId: 's1', grams: '42,0', wasteGrams: '' }],
    ...overrides,
  }
}

describe('formatDuration', () => {
  it('formats minutes as hours and minutes', () => {
    expect(formatDuration(95)).toBe('1 h 35 min')
    expect(formatDuration(120)).toBe('2 h')
    expect(formatDuration(8)).toBe('8 min')
    expect(formatDuration(null)).toBe('')
  })
})

describe('statusTone', () => {
  it('maps statuses to chip tones', () => {
    expect(statusTone('Done')).toBe('success')
    expect(statusTone('Failed')).toBe('danger')
    expect(statusTone('Printing')).toBe('info')
  })
})

describe('filterPrintJobs', () => {
  const jobs = [job({}), job({ id: 'j2', name: 'Vase', status: 'Failed', printerId: 'p2', printerName: 'A1 #2', filaments: [] })]

  it('filters by status, printer and search text', () => {
    expect(filterPrintJobs(jobs, { status: 'Failed', printerId: '', search: '' }).map(item => item.id)).toEqual(['j2'])
    expect(filterPrintJobs(jobs, { status: '', printerId: 'p1', search: '' }).map(item => item.id)).toEqual(['j1'])
    expect(filterPrintJobs(jobs, { status: '', printerId: '', search: 'jade' }).map(item => item.id)).toEqual(['j1'])
    expect(filterPrintJobs(jobs, { status: '', printerId: '', search: '' })).toHaveLength(2)
  })
})

describe('validateJobForm', () => {
  it('builds the request body and accepts a decimal comma', () => {
    const { body, errors } = validateJobForm(form({ lines: [{ key: 1, spoolId: 's1', grams: '42,0', wasteGrams: '4' }], notes: ' ok ' }))
    expect(errors).toEqual({})
    expect(body).toMatchObject({ name: 'Benchy', status: 'Done', notes: 'ok', filaments: [{ spoolId: 's1', grams: 42, wasteGrams: 4 }] })
    expect(body?.finishedAt).toBeTruthy()
  })

  it('reports missing and invalid fields', () => {
    const { body, errors } = validateJobForm(form({
      name: ' ',
      printerId: '',
      lines: [{ key: 1, spoolId: '', grams: 'abc', wasteGrams: '' }, { key: 2, spoolId: 's1', grams: '10', wasteGrams: '20' }],
    }))
    expect(body).toBeNull()
    expect(errors).toMatchObject({ name: 'required', printerId: 'required', 'lines.0.spoolId': 'required', 'lines.0.grams': 'number', 'lines.1.wasteGrams': 'waste' })
  })

  it('rejects a duplicate spool and a finish time before the start', () => {
    const { errors } = validateJobForm(form({
      finishedAt: '2026-11-26T07:00',
      lines: [{ key: 1, spoolId: 's1', grams: '5', wasteGrams: '' }, { key: 2, spoolId: 's1', grams: '5', wasteGrams: '' }],
    }))
    expect(errors['lines.1.spoolId']).toBe('duplicate')
    expect(errors.finishedAt).toBe('order')
  })

  it('requires a start time when printing and ignores the finish time', () => {
    expect(validateJobForm(form({ status: 'Printing', startedAt: '' })).errors.startedAt).toBe('required')
    expect(validateJobForm(form({ status: 'Planned', finishedAt: '2026-11-26T07:00' })).body?.finishedAt).toBeNull()
  })

  it('only keeps finished units for a done print', () => {
    expect(validateJobForm(form({ finishedUnits: '3' })).body?.finishedUnits).toBe(3)
    expect(validateJobForm(form({ status: 'Failed', finishedUnits: '3' })).body?.finishedUnits).toBeNull()
    expect(validateJobForm(form({ finishedUnits: '1,5' })).errors.finishedUnits).toBe('number')
  })

  it('keeps the external pair of an imported job', () => {
    const { body } = validateJobForm(form({}), { externalSource: 'bambu', externalId: 'task-1' })
    expect(body).toMatchObject({ externalSource: 'bambu', externalId: 'task-1' })
  })
})

describe('jobToFormValues', () => {
  it('turns a job into form values', () => {
    const values = jobToFormValues(job({}))
    expect(values.lines).toEqual([{ key: 0, spoolId: 's1', grams: '42', wasteGrams: '4' }])
    expect(values.startedAt).toMatch(/^2026-11-26T\d\d:\d\d$/)
  })
})

describe('mapServerErrors', () => {
  it('maps line errors and known fields, the rest is general', () => {
    const { fields, general } = mapServerErrors({ 'filaments[1].grams': ['Only 3 g left.'], name: ['Required.'], other: ['Odd.'] })
    expect(fields).toEqual({ 'lines.1.grams': 'Only 3 g left.', name: 'Required.' })
    expect(general).toEqual(['Odd.'])
  })
})
