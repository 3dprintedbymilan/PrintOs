import { describe, expect, it } from 'vitest'
import { describeChanges, formatAuditValue, hasMorePages, humanizeField } from '../utils/audit'
import type { AuditEntry } from '../utils/audit'

describe('audit helpers', () => {
  it('humanizes camel case field names', () => {
    expect(humanizeField('electricityPricePerKwh')).toBe('Electricity price per kwh')
    expect(humanizeField('name')).toBe('Name')
  })

  it('formats empty values with a dash', () => {
    expect(formatAuditValue(null)).toBe('—')
    expect(formatAuditValue(0)).toBe('0')
    expect(formatAuditValue(true)).toBe('true')
  })

  it('describes each changed field with before and after', () => {
    const entry: AuditEntry = {
      id: '1',
      createdAt: '2026-01-01T00:00:00Z',
      actorUserId: null,
      actorEmail: null,
      entityType: 'Workspace',
      entityId: 'x',
      action: 'Updated',
      changes: { wastePercent: { old: 5, new: 8 } },
    }
    expect(describeChanges(entry)).toEqual([{ field: 'Waste percent', before: '5', after: '8' }])
  })

  it('knows when more pages exist', () => {
    expect(hasMorePages({ items: [], page: 1, pageSize: 25, total: 26 })).toBe(true)
    expect(hasMorePages({ items: [], page: 2, pageSize: 25, total: 26 })).toBe(false)
  })
})
