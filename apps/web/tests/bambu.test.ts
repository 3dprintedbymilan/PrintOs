import { describe, expect, it } from 'vitest'
import { clampPercent, formatRemaining, isActivePrint, normalizeToken, stateTone, validateToken } from '../utils/bambu'
import type { BambuPrinterStatus } from '../utils/bambu'

const printer = (state: BambuPrinterStatus['state']): BambuPrinterStatus => ({
  printerId: 'p1',
  printerName: 'A1',
  serial: 's1',
  state,
  jobName: null,
  progressPercent: null,
  remainingMinutes: null,
  layer: null,
  totalLayers: null,
  nozzleTemperature: null,
  bedTemperature: null,
  filamentType: null,
  filamentColor: null,
  lastSeenAt: null,
})

describe('bambu helpers', () => {
  it('formats remaining minutes', () => {
    expect(formatRemaining(null)).toBe('')
    expect(formatRemaining(45)).toBe('45 min')
    expect(formatRemaining(120)).toBe('2 h')
    expect(formatRemaining(83)).toBe('1 h 23 min')
  })

  it('maps states to chip tones', () => {
    expect(stateTone('printing')).toBe('info')
    expect(stateTone('failed')).toBe('danger')
    expect(stateTone('finished')).toBe('success')
    expect(stateTone('offline')).toBe('neutral')
  })

  it('treats printing and paused as active', () => {
    expect(isActivePrint(printer('printing'))).toBe(true)
    expect(isActivePrint(printer('paused'))).toBe(true)
    expect(isActivePrint(printer('idle'))).toBe(false)
  })

  it('normalizes and validates a pasted token', () => {
    expect(normalizeToken('  Bearer abc  ')).toBe('abc')
    expect(validateToken('')).toBe('required')
    expect(validateToken('a b')).toBe('spaces')
    expect(validateToken(' abc ')).toBeNull()
  })

  it('clamps progress', () => {
    expect(clampPercent(null)).toBe(0)
    expect(clampPercent(142)).toBe(100)
    expect(clampPercent(41.6)).toBe(42)
  })
})
