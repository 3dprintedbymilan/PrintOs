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

type Translate = (key: string, params?: Record<string, unknown>) => string

const knownServerMessages: { pattern: RegExp, key: string, params: (match: RegExpExecArray) => Record<string, unknown> }[] = [
  { pattern: /^The spool only has (\d+) g left\.$/, key: 'common.serverErrors.spoolStock', params: match => ({ grams: Number(match[1]) }) },
]

export function localizeServerMessage(message: string, translate: Translate): string | undefined {
  for (const known of knownServerMessages) {
    const match = known.pattern.exec(message)
    if (match) return translate(known.key, known.params(match))
  }
  return undefined
}

export type SignUpField = 'email' | 'password' | 'workspaceName'
export type SignUpFieldProblem = 'email' | 'emailTaken' | 'passwordRejected' | 'passwordRequired' | 'workspaceName'

export function mapSignUpFieldErrors(fieldErrors: Record<string, string[]>): { fields: Partial<Record<SignUpField, SignUpFieldProblem>>, hasUnmatched: boolean } {
  const fields: Partial<Record<SignUpField, SignUpFieldProblem>> = {}
  let hasUnmatched = false

  for (const key of Object.keys(fieldErrors)) {
    const normalized = key.toLowerCase()
    if (normalized === 'password') fields.password ??= 'passwordRequired'
    else if (normalized.startsWith('password')) fields.password ??= 'passwordRejected'
    else if (normalized.includes('duplicate')) fields.email ??= 'emailTaken'
    else if (normalized === 'email' || normalized.includes('invalidemail') || normalized.includes('invalidusername')) fields.email ??= 'email'
    else if (normalized === 'workspacename') fields.workspaceName ??= 'workspaceName'
    else hasUnmatched = true
  }

  return { fields, hasUnmatched }
}
