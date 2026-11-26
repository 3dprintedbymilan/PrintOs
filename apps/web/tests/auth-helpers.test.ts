import { describe, expect, it } from 'vitest'
import { mapSignUpFieldErrors, parseApiError } from '../utils/apiError'
import { failedPasswordRules, isValidEmail } from '../utils/passwordRules'

describe('failedPasswordRules', () => {
  it('accepts a password that meets every rule', () => {
    expect(failedPasswordRules('Correct-Horse-9')).toEqual([])
  })

  it('reports every rule a weak password breaks', () => {
    expect(failedPasswordRules('abc')).toEqual(['length', 'uppercase', 'digit'])
  })

  it('requires a lower case letter and a digit', () => {
    expect(failedPasswordRules('ABCDEFGHIJKL')).toEqual(['lowercase', 'digit'])
  })
})

describe('isValidEmail', () => {
  it('accepts normal addresses and rejects obvious mistakes', () => {
    expect(isValidEmail('milan@example.com')).toBe(true)
    expect(isValidEmail(' milan@example.com ')).toBe(true)
    expect(isValidEmail('milan@')).toBe(false)
    expect(isValidEmail('not an email')).toBe(false)
  })
})

describe('parseApiError', () => {
  it('treats a missing status as a network failure', () => {
    expect(parseApiError(new Error('fetch failed')).kind).toBe('network')
  })

  it('maps status codes to error kinds', () => {
    expect(parseApiError({ status: 401 }).kind).toBe('invalidCredentials')
    expect(parseApiError({ statusCode: 423 }).kind).toBe('locked')
    expect(parseApiError({ status: 500 }).kind).toBe('unknown')
  })

  it('keeps the field errors of a validation problem', () => {
    const parsed = parseApiError({ status: 400, data: { errors: { PasswordTooShort: ['Too short'] } } })
    expect(parsed.kind).toBe('validation')
    expect(parsed.fieldErrors.PasswordTooShort).toEqual(['Too short'])
  })
})

describe('mapSignUpFieldErrors', () => {
  it('routes Identity error codes to the matching field', () => {
    const { fields, unmatched } = mapSignUpFieldErrors({
      PasswordRequiresDigit: ['Needs a digit'],
      DuplicateEmail: ['Already used'],
      workspaceName: ['Required'],
      Other: ['Something else'],
    })

    expect(fields.password).toBe('Needs a digit')
    expect(fields.email).toBe('Already used')
    expect(fields.workspaceName).toBe('Required')
    expect(unmatched).toEqual(['Something else'])
  })
})
