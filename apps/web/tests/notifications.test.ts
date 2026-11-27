import { describe, expect, it } from 'vitest'
import { currentUtcOffsetMinutes, isValidTime, notificationTypes, textKey, unreadBadge } from '../utils/notifications'

describe('notification helpers', () => {
  it('caps the unread badge and hides it at zero', () => {
    expect(unreadBadge(0)).toBe('')
    expect(unreadBadge(-3)).toBe('')
    expect(unreadBadge(7)).toBe('7')
    expect(unreadBadge(99)).toBe('99')
    expect(unreadBadge(100)).toBe('99+')
  })

  it('picks the message per type and a separate one for expired licenses', () => {
    expect(textKey({ type: 'PrintFailed', parameters: {} })).toBe('notifications.types.PrintFailed.text')
    expect(textKey({ type: 'LicenseExpiring', parameters: { state: 'expiring' } })).toBe('notifications.types.LicenseExpiring.text')
    expect(textKey({ type: 'LicenseExpiring', parameters: { state: 'expired' } })).toBe('notifications.types.LicenseExpiring.textExpired')
    expect(notificationTypes).toHaveLength(9)
  })

  it('validates quiet hour times as hh:mm', () => {
    expect(isValidTime('07:30')).toBe(true)
    expect(isValidTime('23:59')).toBe(true)
    expect(isValidTime('24:00')).toBe(false)
    expect(isValidTime('7:30')).toBe(false)
    expect(isValidTime('')).toBe(false)
  })

  it('reports the browser offset in minutes east of UTC', () => {
    expect(currentUtcOffsetMinutes(new Date('2027-03-01T00:00:00Z'))).toBe(-new Date('2027-03-01T00:00:00Z').getTimezoneOffset())
  })
})
