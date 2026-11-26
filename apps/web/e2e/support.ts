import type { Page } from '@playwright/test'

const corsHeaders = {
  'access-control-allow-origin': '*',
  'access-control-allow-headers': '*',
  'access-control-allow-methods': '*',
}

export interface MockResponse {
  status: number
  json?: unknown
}

export type MockHandlers = Record<string, (body: unknown) => MockResponse>

export const workspaceResponse = { id: '11111111-1111-1111-1111-111111111111', name: 'Milan Prints' }
export const tokenResponse = { tokenType: 'Bearer', accessToken: 'access-token', expiresIn: 1800, refreshToken: 'refresh-token' }

export async function mockApi(page: Page, handlers: MockHandlers) {
  await page.route('**/api/**', async (route) => {
    const request = route.request()
    if (request.method() === 'OPTIONS') {
      await route.fulfill({ status: 204, headers: corsHeaders })
      return
    }

    const path = new URL(request.url()).pathname
    const handler = handlers[`${request.method()} ${path}`]
    if (!handler) {
      await route.fulfill({ status: 404, headers: corsHeaders })
      return
    }

    const result = handler(request.postDataJSON())
    await route.fulfill({
      status: result.status,
      headers: corsHeaders,
      contentType: 'application/json',
      body: JSON.stringify(result.json ?? {}),
    })
  })
}

export async function seedSignedInSession(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('printos_session', JSON.stringify({
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      expiresAt: Date.now() + 60 * 60 * 1000,
      email: 'owner@example.com',
    }))
  })
  await mockApi(page, {
    'GET /api/workspace': () => ({ status: 200, json: workspaceResponse }),
    'POST /api/auth/sign-out': () => ({ status: 204 }),
  })
}
