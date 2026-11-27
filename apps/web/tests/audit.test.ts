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

  it('localises field names, enum values, booleans, numbers and dates when labels are given', () => {
    const labels = {
      field: (name: string) => (name === 'readyForSale' ? 'Klaar voor verkoop' : undefined),
      value: (raw: string) => (raw === 'Done' ? 'Klaar' : undefined),
      boolean: (value: boolean) => (value ? 'Ja' : 'Nee'),
      dateTime: () => '26 nov. 2026',
      number: (value: number) => `#${value}`,
    }
    const entry: AuditEntry = {
      id: '1',
      createdAt: '2026-01-01T00:00:00Z',
      actorUserId: null,
      actorEmail: null,
      entityType: 'PrintJob',
      entityId: 'x',
      action: 'Updated',
      changes: {
        readyForSale: { old: false, new: true },
        status: { old: 'Planned', new: 'Done' },
        weight: { old: 5, new: 8 },
        archivedAt: { old: null, new: '2026-11-26T10:00:00Z' },
      },
    }
    expect(describeChanges(entry, labels)).toEqual([
      { field: 'Klaar voor verkoop', before: 'Nee', after: 'Ja' },
      { field: 'Status', before: 'Planned', after: 'Klaar' },
      { field: 'Weight', before: '#5', after: '#8' },
      { field: 'Archived at', before: '—', after: '26 nov. 2026' },
    ])
  })

  it('knows when more pages exist', () => {
    expect(hasMorePages({ items: [], page: 1, pageSize: 25, total: 26 })).toBe(true)
    expect(hasMorePages({ items: [], page: 2, pageSize: 25, total: 26 })).toBe(false)
  })
})
