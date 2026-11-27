import { describe, expect, it } from 'vitest'
import { exportFileName, mapAccountServerErrors, namesMatch, validateDeleteAccountForm } from '../utils/accountData'

describe('namesMatch', () => {
  it('ignores case and surrounding spaces but not content', () => {
    expect(namesMatch('  milan prints ', 'Milan Prints')).toBe(true)
    expect(namesMatch('Milan', 'Milan Prints')).toBe(false)
    expect(namesMatch('', '')).toBe(false)
  })
})

describe('validateDeleteAccountForm', () => {
  it('needs the password and the workspace name', () => {
    const result = validateDeleteAccountForm({ password: '', workspaceName: 'x' }, 'Milan Prints')

    expect(result.valid).toBe(false)
    expect(result.errors).toEqual({ password: 'passwordRequired', workspaceName: 'nameMismatch' })
  })

  it('accepts a complete form', () => {
    expect(validateDeleteAccountForm({ password: 'secret', workspaceName: 'Milan Prints' }, 'Milan Prints').valid).toBe(true)
  })
})

describe('mapAccountServerErrors', () => {
  it('maps server field keys to form errors', () => {
    expect(mapAccountServerErrors({ password: ['The password is incorrect.'] })).toEqual({ password: 'passwordWrong' })
    expect(mapAccountServerErrors({ workspaceName: ['x'] })).toEqual({ workspaceName: 'workspaceNameWrong' })
  })
})

describe('exportFileName', () => {
  it('uses the local date', () => {
    expect(exportFileName(new Date(2026, 10, 5))).toBe('printos-export-20261105.zip')
  })
})
