export interface AccessTokenResponse {
  tokenType: string
  accessToken: string
  expiresIn: number
  refreshToken: string
}

export interface WorkspaceSummary {
  id: string
  name: string
}

export interface SignUpInput {
  email: string
  password: string
  workspaceName: string
}

export interface SignInInput {
  email: string
  password: string
}

export type AuthErrorKind = 'invalidCredentials' | 'locked' | 'network' | 'validation' | 'unknown'

export interface ParsedApiError {
  kind: AuthErrorKind
  status: number
  title: string
  fieldErrors: Record<string, string[]>
}
