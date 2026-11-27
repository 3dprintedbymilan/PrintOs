import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const cors = { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*' }

const profile = { layerHeightMillimetres: 0.2, infillPercent: 15, supportsRequired: false, materialType: 'PLA', printProfileNotes: 'Print slowly' }
const version = { id: 'aaaaaaaa-0000-0000-0000-000000000001', versionNumber: 1, fileName: 'dragon.stl', format: 'stl', sizeBytes: 2_500_000, notes: null, printProfile: profile, createdAt: '2026-11-20T10:00:00Z' }
const detail = {
  id: '55555555-5555-5555-5555-555555555555',
  name: 'Dragon',
  description: 'A calm dragon',
  sourceUrl: 'https://example.com/dragon',
  tags: ['toy', 'dragon'],
  thumbnailUrl: null,
  versions: [version],
  createdAt: '2026-11-20T10:00:00Z',
  updatedAt: '2026-11-20T10:00:00Z',
}
const summary = (name: string, tags: string[]) => ({ id: name, name, description: null, tags, format: 'stl', sizeBytes: 2_500_000, versionCount: 1, thumbnailUrl: null, createdAt: detail.createdAt, updatedAt: detail.createdAt })

function binaryStl() {
  const buffer = Buffer.alloc(84 + 50)
  buffer.writeUInt32LE(1, 80)
  ;[0, 0, 0, 10, 0, 0, 0, 10, 10].forEach((value, index) => buffer.writeFloatLE(value, 96 + index * 4))
  return buffer
}

async function mockFiles(page: Page, onUpload?: (body: string) => void) {
  await page.route('**/api/model-files/**', route => route.request().method() === 'GET'
    ? route.fulfill({ status: 200, headers: cors, contentType: 'model/stl', body: binaryStl() })
    : route.fallback())
  await page.route('**/api/models', async (route) => {
    if (route.request().method() !== 'POST') return route.fallback()
    onUpload?.(route.request().postData() ?? '')
    return route.fulfill({ status: 201, headers: cors, contentType: 'application/json', body: JSON.stringify(detail) })
  })
  await page.route('**/api/models/*/thumbnail', route => route.request().method() === 'PUT'
    ? route.fulfill({ status: 200, headers: cors, contentType: 'application/json', body: JSON.stringify({ ...detail, thumbnailUrl: '/api/model-files/thumb' }) })
    : route.fallback())
}

const downloadUrl = { url: '/api/model-files/token', expiresAt: '2030-01-01T00:00:00Z' }

test('the models segment lists models and searches and filters on the server', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/models': () => ({ status: 200, json: [summary('Dragon', ['toy']), summary('Phone stand', ['desk'])] }),
    'GET /api/models/tags': () => ({ status: 200, json: ['desk', 'toy'] }),
  })

  await page.goto('/prints')
  await page.getByRole('radio', { name: 'Modellen' }).check()

  await expect(page.getByRole('link', { name: /Dragon/ })).toContainText('STL · 2,4 MB')
  await expect(page.getByRole('link', { name: /Phone stand/ })).toBeVisible()

  const searched = page.waitForRequest(request => request.url().includes('/api/models?') && request.url().includes('search=drag'))
  await page.getByLabel('Modellen zoeken').fill('drag')
  await searched

  const filtered = page.waitForRequest(request => request.url().includes('tag=desk'))
  await page.getByRole('button', { name: 'desk' }).click()
  await filtered
  await expect(page.getByRole('button', { name: 'desk' })).toHaveAttribute('aria-pressed', 'true')

  await page.getByRole('radio', { name: 'Lijst' }).check({ force: true })
  await expect(page.getByRole('link', { name: /Dragon/ })).toBeVisible()
})

test('an empty library invites the first upload', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/models': () => ({ status: 200, json: [] }),
    'GET /api/models/tags': () => ({ status: 200, json: [] }),
  })

  await page.goto('/prints?section=models')

  await expect(page.getByText('Nog geen modellen. Upload je eerste STL-, 3MF- of OBJ-bestand.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Model uploaden' })).toBeVisible()
})

test('a failed library load offers a retry', async ({ page }) => {
  await seedSignedInSession(page)
  let calls = 0
  await mockApi(page, {
    'GET /api/models': () => (++calls <= 2 ? { status: 500 } : { status: 200, json: [summary('Dragon', [])] }),
    'GET /api/models/tags': () => ({ status: 200, json: [] }),
  })

  await page.goto('/prints?section=models')
  await page.getByRole('button', { name: 'Opnieuw proberen' }).click()

  await expect(page.getByRole('link', { name: /Dragon/ })).toBeVisible()
})

test('uploading validates the file, then creates the model and opens it', async ({ page }) => {
  await seedSignedInSession(page)
  let uploaded = ''
  await mockApi(page, {
    [`GET /api/models/${detail.id}`]: () => ({ status: 200, json: detail }),
    [`GET /api/models/${detail.id}/versions/${version.id}/download-url`]: () => ({ status: 200, json: downloadUrl }),
  })
  await mockFiles(page, (body) => { uploaded = body })

  await page.goto('/models/new')
  await page.getByRole('button', { name: 'Uploaden' }).click()
  await expect(page.getByText('Kies een modelbestand.')).toBeVisible()
  await expect(page.getByLabel('Modelbestand')).toBeFocused()

  await page.getByLabel('Modelbestand').setInputFiles({ name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('hello') })
  await page.getByRole('button', { name: 'Uploaden' }).click()
  await expect(page.getByText('Alleen STL-, 3MF- en OBJ-bestanden worden ondersteund.')).toBeVisible()

  await page.getByLabel('Modelbestand').setInputFiles({ name: 'dragon.stl', mimeType: 'model/stl', buffer: binaryStl() })
  await expect(page.getByLabel('Naam')).toHaveValue('dragon')
  await page.getByLabel('Naam').fill('Dragon')
  await page.getByLabel('Tags', { exact: true }).fill('Toy, Dragon')
  await page.getByLabel('Laagdikte (mm)').fill('0,2')
  await page.getByRole('button', { name: 'Uploaden' }).click()

  await expect(page).toHaveURL(new RegExp(`/models/${detail.id}`))
  await expect(page.getByRole('heading', { name: 'Dragon' })).toBeVisible()
  expect(uploaded).toContain('name="tags"')
  expect(uploaded).toContain('toy,dragon')
  expect(uploaded).toContain('0.2')
})

test('the detail page shows the profile, edits details and shows the preview region', async ({ page }) => {
  await seedSignedInSession(page)
  let updated: unknown
  await mockApi(page, {
    [`GET /api/models/${detail.id}`]: () => ({ status: 200, json: detail }),
    [`GET /api/models/${detail.id}/versions/${version.id}/download-url`]: () => ({ status: 200, json: downloadUrl }),
    [`PUT /api/models/${detail.id}`]: (body) => {
      updated = body
      return { status: 200, json: { ...detail, ...(body as object) } }
    },
  })
  await mockFiles(page)

  await page.goto(`/models/${detail.id}`)

  await expect(page.getByRole('heading', { name: 'Dragon' })).toBeVisible()
  await expect(page.getByText('PLA', { exact: true })).toBeVisible()
  await expect(page.getByText('15%')).toBeVisible()
  await expect(page.getByRole('img', { name: '3D-voorbeeld van dragon.stl' })).toBeAttached()
  await expect(page.getByText('Voorbeeld laden...')).toBeHidden({ timeout: 15_000 })

  await page.getByRole('button', { name: 'Gegevens bewerken' }).click()
  await page.getByLabel('Tags', { exact: true }).fill('Toy, mythical')
  await page.getByRole('button', { name: 'Opslaan' }).click()

  await expect(page.getByRole('status').filter({ hasText: 'Opgeslagen.' })).toBeVisible()
  expect(updated).toMatchObject({ name: 'Dragon', tags: ['toy', 'mythical'] })
})

test('deleting a model asks for confirmation and returns to the library', async ({ page }) => {
  await seedSignedInSession(page)
  let deleted = false
  await mockApi(page, {
    [`GET /api/models/${detail.id}`]: () => ({ status: 200, json: detail }),
    [`GET /api/models/${detail.id}/versions/${version.id}/download-url`]: () => ({ status: 200, json: downloadUrl }),
    [`DELETE /api/models/${detail.id}`]: () => {
      deleted = true
      return { status: 204 }
    },
    'GET /api/models': () => ({ status: 200, json: [] }),
    'GET /api/models/tags': () => ({ status: 200, json: [] }),
  })
  await mockFiles(page)

  await page.goto(`/models/${detail.id}`)
  await page.getByRole('button', { name: 'Model verwijderen' }).click()
  await expect(page.getByRole('dialog')).toContainText('definitief verwijderd')
  await page.getByRole('button', { name: 'Definitief verwijderen' }).click()

  await expect(page).toHaveURL(/\/prints\?section=models/)
  expect(deleted).toBe(true)
})
