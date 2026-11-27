import { expect, test } from '@playwright/test'
import type { APIRequestContext } from '@playwright/test'

const apiBase = process.env.JOURNEY_API_BASE ?? 'http://localhost:5000'
const password = 'Journey-Pass-9'

const printer = {
  name: 'Isolation A1',
  model: 'Bambu Lab A1',
  buildVolumeXMillimetres: 256,
  buildVolumeYMillimetres: 256,
  buildVolumeZMillimetres: 256,
  nozzleDiameterMillimetres: 0.4,
  powerDrawWatts: 95,
  purchaseCost: 399,
  lifetimeHours: 5000,
}

async function signUp(request: APIRequestContext, label: string) {
  const email = `security-${label}-${Date.now()}-${Math.round(Math.random() * 1e6)}@example.com`
  const created = await request.post(`${apiBase}/api/auth/sign-up`, { data: { email, password, workspaceName: `Security ${label}` } })
  expect(created.status()).toBe(201)
  const signedIn = await request.post(`${apiBase}/api/auth/sign-in`, { data: { email, password } })
  expect(signedIn.status()).toBe(200)
  const tokens = await signedIn.json() as { accessToken: string }
  return { Authorization: `Bearer ${tokens.accessToken}` }
}

test('data endpoints reject anonymous callers', async ({ request }) => {
  for (const path of ['/api/printers', '/api/spools', '/api/products', '/api/print-jobs', '/api/dashboard/summary', '/api/audit', '/api/licenses', '/api/models']) {
    const response = await request.get(`${apiBase}${path}`)
    expect(response.status(), path).toBe(401)
  }
})

test('one workspace can never read or change another workspace', async ({ request }) => {
  const owner = await signUp(request, 'owner')
  const stranger = await signUp(request, 'stranger')

  const created = await request.post(`${apiBase}/api/printers`, { data: printer, headers: owner })
  expect(created.status()).toBe(201)
  const { id } = await created.json() as { id: string }

  expect((await request.get(`${apiBase}/api/printers/${id}`, { headers: owner })).status()).toBe(200)
  expect((await request.get(`${apiBase}/api/printers/${id}`, { headers: stranger })).status()).toBe(404)
  expect((await request.put(`${apiBase}/api/printers/${id}`, { data: { ...printer, name: 'Hijacked' }, headers: stranger })).status()).toBe(404)

  const strangerList = await (await request.get(`${apiBase}/api/printers`, { headers: stranger })).json() as { id: string }[]
  expect(strangerList.map(item => item.id)).not.toContain(id)
})

test('invalid input is rejected with field-level validation errors', async ({ request }) => {
  const owner = await signUp(request, 'validation')

  const response = await request.post(`${apiBase}/api/printers`, { data: { ...printer, name: '', powerDrawWatts: -5 }, headers: owner })
  expect(response.status()).toBe(400)
  const problem = await response.json() as { errors: Record<string, string[]> }
  expect(Object.keys(problem.errors).length).toBeGreaterThan(0)
  expect(Object.keys(problem.errors).every(key => key === key.charAt(0).toLowerCase() + key.slice(1))).toBe(true)

  const weakPassword = await request.post(`${apiBase}/api/auth/sign-up`, { data: { email: `weak-${Date.now()}@example.com`, password: 'abc', workspaceName: 'Weak' } })
  expect(weakPassword.status()).toBe(400)
})
