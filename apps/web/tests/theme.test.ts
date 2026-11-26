import { describe, expect, it } from 'vitest'
import { isThemePreference, resolveTheme } from '../utils/theme'

describe('resolveTheme', () => {
  it('follows the system when the preference is system', () => {
    expect(resolveTheme('system', true)).toBe('dark')
    expect(resolveTheme('system', false)).toBe('light')
  })

  it('uses an explicit preference regardless of the system', () => {
    expect(resolveTheme('light', true)).toBe('light')
    expect(resolveTheme('dark', false)).toBe('dark')
  })
})

describe('isThemePreference', () => {
  it('accepts only known preferences', () => {
    expect(isThemePreference('dark')).toBe(true)
    expect(isThemePreference('purple')).toBe(false)
    expect(isThemePreference(null)).toBe(false)
  })
})
