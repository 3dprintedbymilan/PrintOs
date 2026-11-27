import { describe, expect, it } from 'vitest'
import { fromDateTimeLocal, groupByDay, lastEnd, parsePartSize, toDateTimeLocal, totalPlanned } from '../utils/schedule'
import type { ScheduleItem, ScheduleLane } from '../utils/schedule'

function item(overrides: Partial<ScheduleItem>): ScheduleItem {
  return {
    jobId: 'j', name: 'Job', status: 'Planned', start: '2027-03-01T08:00:00', end: '2027-03-01T09:00:00', durationMinutes: 60,
    durationEstimated: false, pinned: false, pinnedStartAt: null, late: false, noFilament: false, priority: 1,
    orderNumber: null, dueDate: null, plannedUnits: null, ...overrides,
  }
}

describe('schedule helpers', () => {
  it('groups items per local day in start order', () => {
    const groups = groupByDay([
      item({ jobId: 'c', start: '2027-03-02T07:00:00' }),
      item({ jobId: 'b', start: '2027-03-01T13:00:00' }),
      item({ jobId: 'a', start: '2027-03-01T08:00:00' }),
    ])
    expect(groups.map(group => group.key)).toEqual(['2027-03-01', '2027-03-02'])
    expect(groups[0]!.items.map(entry => entry.jobId)).toEqual(['a', 'b'])
  })

  it('round-trips a local date time', () => {
    const local = toDateTimeLocal('2027-03-01T08:30:00')
    expect(local).toBe('2027-03-01T08:30')
    expect(fromDateTimeLocal(local)).toBe(new Date('2027-03-01T08:30').toISOString())
    expect(fromDateTimeLocal('')).toBeNull()
    expect(fromDateTimeLocal('nonsense')).toBeNull()
    expect(toDateTimeLocal(null)).toBe('')
  })

  it('parses the part size as all or nothing and rejects non-integers', () => {
    expect(parsePartSize({ x: '', y: ' ', z: '' })).toEqual({ kind: 'none' })
    expect(parsePartSize({ x: '100', y: '200', z: '50' })).toEqual({ kind: 'ok', x: 100, y: 200, z: 50 })
    expect(parsePartSize({ x: '100', y: '', z: '50' })).toEqual({ kind: 'invalid' })
    expect(parsePartSize({ x: '100,5', y: '200', z: '50' })).toEqual({ kind: 'invalid' })
    expect(parsePartSize({ x: '0', y: '200', z: '50' })).toEqual({ kind: 'invalid' })
    expect(parsePartSize({ x: '2001', y: '1', z: '1' })).toEqual({ kind: 'invalid' })
  })

  it('counts planned jobs and finds the last end', () => {
    const lanes: ScheduleLane[] = [
      { printerId: 'a', name: 'A', archived: false, items: [item({ status: 'Printing' }), item({ end: '2027-03-01T10:00:00' })] },
      { printerId: 'b', name: 'B', archived: false, items: [item({ end: '2027-03-03T10:00:00' })] },
    ]
    expect(totalPlanned(lanes)).toBe(2)
    expect(lastEnd(lanes)).toBe('2027-03-03T10:00:00')
    expect(lastEnd([])).toBeNull()
  })
})
