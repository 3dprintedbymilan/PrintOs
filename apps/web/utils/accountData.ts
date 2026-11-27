export interface DeleteAccountFormValues {
  password: string
  workspaceName: string
}

export type DeleteAccountField = keyof DeleteAccountFormValues
export type DeleteAccountErrorKind = 'passwordRequired' | 'nameMismatch' | 'passwordWrong' | 'workspaceNameWrong'

export function namesMatch(typed: string, workspaceName: string): boolean {
  return typed.trim().length > 0 && typed.trim().toLowerCase() === workspaceName.trim().toLowerCase()
}

export function validateDeleteAccountForm(values: DeleteAccountFormValues, workspaceName: string) {
  const errors: Partial<Record<DeleteAccountField, DeleteAccountErrorKind>> = {}
  if (!values.password) errors.password = 'passwordRequired'
  if (!namesMatch(values.workspaceName, workspaceName)) errors.workspaceName = 'nameMismatch'
  return { valid: Object.keys(errors).length === 0, errors }
}

export function mapAccountServerErrors(fieldErrors: Record<string, string[]>): Partial<Record<DeleteAccountField, DeleteAccountErrorKind>> {
  const errors: Partial<Record<DeleteAccountField, DeleteAccountErrorKind>> = {}
  if (fieldErrors.password) errors.password = 'passwordWrong'
  if (fieldErrors.workspaceName) errors.workspaceName = 'workspaceNameWrong'
  return errors
}

export function exportFileName(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0')
  return `printos-export-${date.getFullYear()}${pad(date.getMonth() + 1)}${pad(date.getDate())}.zip`
}
