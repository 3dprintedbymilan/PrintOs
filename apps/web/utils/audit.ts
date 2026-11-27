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

export interface AuditLabels {
  field: (name: string) => string | undefined
  value: (raw: string) => string | undefined
  boolean: (value: boolean) => string
  dateTime: (iso: string) => string
  number: (value: number) => string
}

const isoDateTime = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/

export function formatAuditValue(value: unknown, labels?: AuditLabels): string {
  if (value === null || value === undefined) return '—'
  if (typeof value === 'boolean') return labels ? labels.boolean(value) : value ? 'true' : 'false'
  if (!labels) return String(value)
  if (typeof value === 'number') return labels.number(value)
  const text = String(value)
  if (isoDateTime.test(text) && !Number.isNaN(Date.parse(text))) return labels.dateTime(text)
  return labels.value(text) ?? text
}

export interface AuditChangeLine {
  field: string
  before: string
  after: string
}

export function describeChanges(entry: AuditEntry, labels?: AuditLabels): AuditChangeLine[] {
  return Object.entries(entry.changes).map(([field, change]) => ({
    field: labels?.field(field) ?? humanizeField(field),
    before: formatAuditValue(change.old, labels),
    after: formatAuditValue(change.new, labels),
  }))
}

export function hasMorePages(page: AuditPage): boolean {
  return page.page * page.pageSize < page.total
}
