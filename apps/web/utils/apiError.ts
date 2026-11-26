import type { ParsedApiError } from '~/types/auth'

interface FetchErrorLike {
  status?: number
  statusCode?: number
  data?: { title?: string, errors?: Record<string, string[]> }
}

export function parseApiError(error: unknown): ParsedApiError {
  const fetchError = (error ?? {}) as FetchErrorLike
  const status = fetchError.status ?? fetchError.statusCode ?? 0
  const title = fetchError.data?.title ?? ''
  const fieldErrors = fetchError.data?.errors ?? {}

  if (status === 0) return { kind: 'network', status, title, fieldErrors }
  if (status === 401) return { kind: 'invalidCredentials', status, title, fieldErrors }
  if (status === 423) return { kind: 'locked', status, title, fieldErrors }
  if (status === 400) return { kind: 'validation', status, title, fieldErrors }
  return { kind: 'unknown', status, title, fieldErrors }
}

export type SignUpField = 'email' | 'password' | 'workspaceName'

export function mapSignUpFieldErrors(fieldErrors: Record<string, string[]>): { fields: Partial<Record<SignUpField, string>>, unmatched: string[] } {
  const fields: Partial<Record<SignUpField, string>> = {}
  const unmatched: string[] = []

  for (const [key, messages] of Object.entries(fieldErrors)) {
    const normalized = key.toLowerCase()
    const message = messages[0] ?? ''
    if (normalized.startsWith('password')) fields.password ??= message
    else if (normalized === 'email' || normalized.includes('duplicate') || normalized.includes('invalidemail')) fields.email ??= message
    else if (normalized === 'workspacename') fields.workspaceName ??= message
    else unmatched.push(message)
  }

  return { fields, unmatched }
}
