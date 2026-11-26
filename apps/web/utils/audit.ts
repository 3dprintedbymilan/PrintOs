export interface AuditChange {
  old: unknown
  new: unknown
}

export interface AuditEntry {
  id: string
  createdAt: string
  actorUserId: string | null
  actorEmail: string | null
  entityType: string
  entityId: string
  action: 'Created' | 'Updated' | 'Deleted'
  changes: Record<string, AuditChange>
}

export interface AuditPage {
  items: AuditEntry[]
  page: number
  pageSize: number
  total: number
}

export function humanizeField(name: string): string {
  const spaced = name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase()
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}

export function formatAuditValue(value: unknown): string {
  if (value === null || value === undefined) return '—'
  if (typeof value === 'boolean') return value ? 'true' : 'false'
  return String(value)
}

export interface AuditChangeLine {
  field: string
  before: string
  after: string
}

export function describeChanges(entry: AuditEntry): AuditChangeLine[] {
  return Object.entries(entry.changes).map(([field, change]) => ({
    field: humanizeField(field),
    before: formatAuditValue(change.old),
    after: formatAuditValue(change.new),
  }))
}

export function hasMorePages(page: AuditPage): boolean {
  return page.page * page.pageSize < page.total
}
