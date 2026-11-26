export type PasswordRule = 'length' | 'uppercase' | 'lowercase' | 'digit'

export const minimumPasswordLength = 10

export function failedPasswordRules(password: string): PasswordRule[] {
  const failed: PasswordRule[] = []
  if (password.length < minimumPasswordLength) failed.push('length')
  if (!/[A-Z]/.test(password)) failed.push('uppercase')
  if (!/[a-z]/.test(password)) failed.push('lowercase')
  if (!/\d/.test(password)) failed.push('digit')
  return failed
}

export function isValidEmail(email: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())
}
