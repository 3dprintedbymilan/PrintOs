import { describe, expect, it } from 'vitest'
import type { BambuMonitorPrinter } from '../utils/bambuMonitor'
import { formatMinutes, hasHealthProblem, relativeAge, windowFor } from '../utils/bambuMonitor'

const printer = {
  utilization: [
    { hours: 24, printingPercent: 40, printingMinutes: 576, observedMinutes: 1440, offlineMinutes: 0, coveragePercent: 100 },
    { hours: 168, printingPercent: null, printingMinutes: 0, observedMinutes: 0, offlineMinutes: 0, coveragePercent: 0 },
  ],
  printErrorCode: null,
  hmsCodes: [],
} as unknown as BambuMonitorPrinter

describe('bambu monitor helpers', () => {
  it('finds the utilization window by hours', () => {
    expect(windowFor(printer, 24)?.printingPercent).toBe(40)
    expect(windowFor(printer, 720)).toBeNull()
  })

  it('formats minutes compactly', () => {
    expect(formatMinutes(0)).toBe('0 min')
    expect(formatMinutes(45)).toBe('45 min')
    expect(formatMinutes(120)).toBe('2 h')
    expect(formatMinutes(125)).toBe('2 h 5 min')
    expect(formatMinutes(60 * 24 * 7)).toBe('7 d')
  })

  it('describes ages relative to now', () => {
    const now = Date.parse('2026-11-27T12:00:00Z')
    expect(relativeAge(null, now, 'en')).toBe('')
    expect(relativeAge('2026-11-27T11:55:00Z', now, 'en')).toBe('5 minutes ago')
    expect(relativeAge('2026-11-27T09:00:00Z', now, 'en')).toBe('3 hours ago')
    expect(relativeAge('2026-11-25T12:00:00Z', now, 'en')).toBe('2 days ago')
  })

  it('flags error codes and HMS messages as a health problem', () => {
    expect(hasHealthProblem(printer)).toBe(false)
    expect(hasHealthProblem({ ...printer, printErrorCode: '0500-8011' })).toBe(true)
    expect(hasHealthProblem({ ...printer, hmsCodes: ['0300-0100-0001-0002'] })).toBe(true)
  })
})
