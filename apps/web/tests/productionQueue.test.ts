import { describe, expect, it } from 'vitest'
import { filamentIssues, lineProgressPercent, parseUnits, parseUnitsPerJob, plannedItems, printingItems, splitCount } from '../utils/productionQueue'
import type { QueueItem } from '../utils/productionQueue'
import { activeSecondary, secondaryDestinations } from '../utils/navigation'

function item(overrides: Partial<QueueItem>): QueueItem {
  return {
    id: 'j1',
    name: 'Part',
    status: 'Planned',
    printerId: 'p1',
    printerName: 'A1',
    plannedUnits: 2,
    durationMinutes: null,
    startedAt: null,
    queuePosition: null,
    orderId: null,
    orderNumber: null,
    customerName: null,
    dueDate: null,
    isOverdue: false,
    orderLineId: null,
    lineDescription: null,
    lineQuantity: null,
    hasFilament: false,
    filamentShort: false,
    canMoveUp: false,
    canMoveDown: false,
    filaments: [],
    ...overrides,
  }
}

describe('production queue helpers', () => {
  it('caps line progress at 100 percent and handles empty lines', () => {
    expect(lineProgressPercent({ ordered: 4, produced: 1 })).toBe(25)
    expect(lineProgressPercent({ ordered: 4, produced: 9 })).toBe(100)
    expect(lineProgressPercent({ ordered: 0, produced: 0 })).toBe(0)
  })

  it('counts the jobs a split produces', () => {
    expect(splitCount(5, 2)).toBe(3)
    expect(splitCount(5, null)).toBe(1)
    expect(splitCount(3, 10)).toBe(1)
    expect(splitCount(0, 2)).toBe(0)
  })

  it('parses units per job with a decimal comma and rejects non-integers', () => {
    expect(parseUnitsPerJob('')).toEqual({ value: null, valid: true })
    expect(parseUnitsPerJob('4')).toEqual({ value: 4, valid: true })
    expect(parseUnitsPerJob('2,5').valid).toBe(false)
    expect(parseUnitsPerJob('0').valid).toBe(false)
    expect(parseUnitsPerJob('abc').valid).toBe(false)
  })

  it('parses piece counts between 1 and 100000', () => {
    expect(parseUnits('12')).toBe(12)
    expect(parseUnits('0')).toBeNull()
    expect(parseUnits('100001')).toBeNull()
  })

  it('splits the queue into printing and planned jobs and finds filament issues', () => {
    const items = [item({ id: 'a', status: 'Printing' }), item({ id: 'b' }), item({
      id: 'c',
      filaments: [
        { spoolId: 's', materialName: 'PLA', colorName: 'Red', colorHex: null, neededGrams: 90, availableGrams: 50, archived: false, enough: false },
        { spoolId: 't', materialName: 'PLA', colorName: 'Blue', colorHex: null, neededGrams: 10, availableGrams: 50, archived: false, enough: true },
      ],
    })]
    expect(printingItems(items).map(entry => entry.id)).toEqual(['a'])
    expect(plannedItems(items).map(entry => entry.id)).toEqual(['b', 'c'])
    expect(filamentIssues(items[2]!).map(check => check.spoolId)).toEqual(['s'])
  })

  it('registers the queue as a sidebar destination that highlights on the Prints page', () => {
    expect(secondaryDestinations.some(destination => destination.to === '/prints?section=queue')).toBe(true)
    expect(activeSecondary('/prints', { section: 'queue' })).toBe('/prints?section=queue')
    expect(activeSecondary('/prints', {})).toBeNull()
  })
})
