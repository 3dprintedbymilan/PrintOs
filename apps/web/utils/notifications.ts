export const notificationTypes = [
  'LowStock',
  'ReorderSoon',
  'PrintFailed',
  'PrintFinished',
  'PrinterOffline',
  'MaintenanceDue',
  'LicenseExpiring',
  'OrderDueSoon',
  'OrderOverdue',
] as const

export type NotificationType = typeof notificationTypes[number]

export type NotificationSeverity = 'info' | 'warning' | 'danger'

export interface AppNotification {
  id: string
  type: NotificationType
  severity: NotificationSeverity
  parameters: Record<string, string>
  linkPath: string | null
  createdAt: string
  isRead: boolean
}

export interface NotificationList {
  items: AppNotification[]
  unreadCount: number
  hasMore: boolean
}

export interface NotificationTypePreference {
  type: NotificationType
  inApp: boolean
  email: boolean
}

export interface QuietHours {
  enabled: boolean
  start: string
  end: string
  utcOffsetMinutes: number
}

export interface NotificationPreferences {
  types: NotificationTypePreference[]
  quietHours: QuietHours
  channels: { emailAvailable: boolean, realtime: boolean, push: boolean }
}

export const notificationPollMilliseconds = 30_000

export function unreadBadge(count: number): string {
  if (count <= 0) return ''
  return count > 99 ? '99+' : String(count)
}

export function severityTone(severity: NotificationSeverity): 'info' | 'warning' | 'danger' {
  return severity
}

export function textKey(notification: Pick<AppNotification, 'type' | 'parameters'>): string {
  if (notification.type === 'LicenseExpiring' && notification.parameters.state === 'expired') return 'notifications.types.LicenseExpiring.textExpired'
  return `notifications.types.${notification.type}.text`
}

export function isValidTime(value: string): boolean {
  return /^([01]\d|2[0-3]):[0-5]\d$/.test(value)
}

export function currentUtcOffsetMinutes(date = new Date()): number {
  return -date.getTimezoneOffset()
}
