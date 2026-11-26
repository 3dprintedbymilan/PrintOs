import { defineStore } from 'pinia'
import type { AccessTokenResponse, SignInInput, SignUpInput, WorkspaceSummary } from '~/types/auth'

const storageKey = 'printos_session'
const refreshMarginSeconds = 60

interface StoredSession {
  accessToken: string
  refreshToken: string
  expiresAt: number
  email: string
}

function readStoredSession(): StoredSession | null {
  try {
    const raw = localStorage.getItem(storageKey)
    return raw ? (JSON.parse(raw) as StoredSession) : null
  }
  catch {
    return null
  }
}

function writeStoredSession(session: StoredSession | null) {
  try {
    if (session) localStorage.setItem(storageKey, JSON.stringify(session))
    else localStorage.removeItem(storageKey)
  }
  catch {
    // storage unavailable (private mode): the session then lasts until the tab closes
  }
}

export const useAuthStore = defineStore('auth', () => {
  const session = ref<StoredSession | null>(null)
  const workspace = ref<WorkspaceSummary | null>(null)

  const isSignedIn = computed(() => session.value !== null)
  const email = computed(() => session.value?.email ?? '')

  function apiBase() {
    return useRuntimeConfig().public.apiBase
  }

  function hydrate() {
    session.value = readStoredSession()
  }

  function applyTokens(tokens: AccessTokenResponse, signedInEmail: string) {
    session.value = {
      accessToken: tokens.accessToken,
      refreshToken: tokens.refreshToken,
      expiresAt: Date.now() + tokens.expiresIn * 1000,
      email: signedInEmail,
    }
    writeStoredSession(session.value)
  }

  function clearSession() {
    session.value = null
    workspace.value = null
    writeStoredSession(null)
  }

  async function loadWorkspace() {
    workspace.value = await authorizedFetch<WorkspaceSummary>('/api/workspace')
  }

  async function signIn(input: SignInInput) {
    const tokens = await $fetch<AccessTokenResponse>('/api/auth/sign-in', {
      baseURL: apiBase(),
      method: 'POST',
      body: { email: input.email.trim(), password: input.password },
    })
    applyTokens(tokens, input.email.trim())
    await loadWorkspace()
  }

  async function signUp(input: SignUpInput) {
    await $fetch('/api/auth/sign-up', {
      baseURL: apiBase(),
      method: 'POST',
      body: { email: input.email.trim(), password: input.password, workspaceName: input.workspaceName.trim() },
    })
    await signIn({ email: input.email, password: input.password })
  }

  async function refresh(): Promise<boolean> {
    if (!session.value) return false
    try {
      const tokens = await $fetch<AccessTokenResponse>('/api/auth/refresh', {
        baseURL: apiBase(),
        method: 'POST',
        body: { refreshToken: session.value.refreshToken },
      })
      applyTokens(tokens, session.value.email)
      return true
    }
    catch {
      clearSession()
      return false
    }
  }

  async function authorizedFetch<T>(path: string, options: Parameters<typeof $fetch>[1] = {}): Promise<T> {
    if (session.value && session.value.expiresAt - Date.now() < refreshMarginSeconds * 1000) {
      await refresh()
    }
    if (!session.value) {
      throw createError({ statusCode: 401, statusMessage: 'Not signed in' })
    }
    return await $fetch<T>(path, {
      ...options,
      baseURL: apiBase(),
      headers: { ...(options.headers as Record<string, string> | undefined), Authorization: `Bearer ${session.value.accessToken}` },
    })
  }

  async function signOut() {
    try {
      await authorizedFetch('/api/auth/sign-out', { method: 'POST' })
    }
    catch {
      // the local session is cleared either way
    }
    clearSession()
  }

  return { session, workspace, isSignedIn, email, hydrate, signIn, signUp, refresh, authorizedFetch, loadWorkspace, signOut }
})
