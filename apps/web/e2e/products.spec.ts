import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const productId = '33333333-3333-3333-3333-333333333333'
const variantId = '44444444-4444-4444-4444-444444444444'
const spoolId = '55555555-5555-5555-5555-555555555555'
const printerId = '22222222-2222-2222-2222-222222222222'

const cost = {
  currency: 'EUR',
  lines: [
    { kind: 'material', label: 'material', quantity: 105, unit: 'g', unitCost: 0.02, amount: 2.1 },
    { kind: 'labour', label: 'labour', quantity: 0.25, unit: 'h', unitCost: 20, amount: 5 },
  ],
  totalCost: 7.1,
  price: 20,
  targetMarginPercent: 40,
  feePercent: 0,
  suggestedPrice: 11.83,
  feeAmount: 0,
  profit: 12.9,
  marginPercent: 64.5,
  unpricedMaterials: [] as string[],
}

const filamentLine = {
  id: '66666666-6666-6666-6666-666666666666',
  kind: 'filament',
  label: null,
  materialId: null,
  materialName: null,
  spoolId,
  spoolLabel: 'PLA Basic - Wit',
  grams: 100,
  printerId: null,
  printerName: null,
  modelId: null,
  modelName: null,
  minutes: null,
  amount: null,
}

const variant = {
  id: variantId,
  productId,
  name: 'Groot',
  sku: 'VAAS-G',
  price: 20,
  targetMarginPercent: 40,
  feePercent: 0,
  bomLines: [filamentLine],
  cost,
}

const product = {
  id: productId,
  name: 'Spiraalvaas',
  description: 'Een vaas',
  sku: 'VAAS',
  isArchived: false,
  createdAt: '2026-11-27T08:00:00Z',
  currency: 'EUR',
  variants: [variant],
}

const summary = {
  id: productId,
  name: 'Spiraalvaas',
  description: 'Een vaas',
  sku: 'VAAS',
  isArchived: false,
  currency: 'EUR',
  variantCount: 1,
  lowestPrice: 20,
  highestPrice: 20,
  createdAt: product.createdAt,
}

const supportData = {
  'GET /api/printers': () => ({ status: 200, json: [{ id: printerId, name: 'A1 desk', model: 'Bambu Lab A1', isArchived: false }] }),
  'GET /api/spools': () => ({ status: 200, json: [{ id: spoolId, materialId: 'm1', materialName: 'PLA Basic', colorName: 'Wit', isArchived: false }] }),
  'GET /api/materials': () => ({ status: 200, json: [{ id: 'm1', name: 'PLA Basic', type: 'PLA' }] }),
  'GET /api/models': () => ({ status: 200, json: [] }),
}

async function captureSearch(page: Page, calls: string[]) {
  await page.route(url => url.pathname === '/api/products', async (route) => {
    const url = new URL(route.request().url())
    if (route.request().method() === 'OPTIONS') {
      await route.fulfill({ status: 204, headers: { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*' } })
      return
    }
    calls.push(url.search)
    const term = url.searchParams.get('search')
    await route.fulfill({
      status: 200,
      headers: { 'access-control-allow-origin': '*' },
      contentType: 'application/json',
      body: JSON.stringify(term && !'spiraalvaas'.includes(term.toLowerCase()) ? [] : [summary]),
    })
  })
}

test('the product list shows variants and price range and supports search', async ({ page }) => {
  await seedSignedInSession(page)
  const calls: string[] = []
  await captureSearch(page, calls)

  await page.goto('/products')

  await expect(page.getByRole('heading', { level: 1, name: 'Producten' })).toBeVisible()
  const row = page.getByRole('link', { name: /Spiraalvaas/ })
  await expect(row).toContainText('VAAS')
  await expect(row).toContainText('1 variant')
  await expect(row).toContainText('20,00')

  await page.getByLabel('Zoek producten').fill('xyz')
  await expect(page.getByText('Geen producten gevonden voor deze zoekopdracht.')).toBeVisible()
  expect(calls.some(call => call.includes('search=xyz'))).toBe(true)

  await page.getByLabel('Toon gearchiveerde producten').check()
  await expect.poll(() => calls.some(call => call.includes('includeArchived=true'))).toBe(true)
})

test('the business tab links to products', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { 'GET /api/products': () => ({ status: 200, json: [] }) })

  await page.goto('/business')
  await page.getByRole('link', { name: /Producten/ }).click()

  await expect(page).toHaveURL(/\/products$/)
  await expect(page.getByText('Nog geen producten. Voeg je eerste product toe.')).toBeVisible()
})

test('creating a product validates the name and opens the detail page', async ({ page }) => {
  await seedSignedInSession(page)
  let created: unknown
  await mockApi(page, {
    'POST /api/products': (body) => {
      created = body
      return { status: 201, json: { ...product, variants: [] } }
    },
    [`GET /api/products/${productId}`]: () => ({ status: 200, json: { ...product, variants: [] } }),
    ...supportData,
  })

  await page.goto('/products/new')
  await page.getByRole('button', { name: 'Product aanmaken' }).click()
  await expect(page.getByText('Vul Naam in.')).toBeVisible()
  await expect(page.getByLabel('Naam', { exact: true })).toBeFocused()

  await page.getByLabel('Naam', { exact: true }).fill('Spiraalvaas')
  await page.getByLabel('SKU').fill('VAAS')
  await page.getByRole('button', { name: 'Product aanmaken' }).click()

  await expect(page).toHaveURL(new RegExp(`/products/${productId}$`))
  expect(created).toMatchObject({ name: 'Spiraalvaas', sku: 'VAAS', description: null })
  await expect(page.getByText('Nog geen varianten.')).toBeVisible()
})

test('a variant shows cost and margin and a bill of materials line can be added', async ({ page }) => {
  await seedSignedInSession(page)
  let postedLine: unknown
  await mockApi(page, {
    [`GET /api/products/${productId}`]: () => ({ status: 200, json: product }),
    [`POST /api/products/${productId}/variants/${variantId}/bom-lines`]: (body) => {
      postedLine = body
      return {
        status: 201,
        json: {
          ...variant,
          bomLines: [filamentLine, { ...filamentLine, id: 'x', kind: 'labour', spoolId: null, spoolLabel: null, grams: null, minutes: 15 }],
        },
      }
    },
    ...supportData,
  })

  await page.goto(`/products/${productId}`)

  const card = page.getByLabel('Groot', { exact: true })
  await expect(card).toContainText('Marge 64,5%')
  await expect(card).toContainText('7,10')
  await expect(card).toContainText('12,90')
  await expect(card.getByText('PLA Basic - Wit · 100 g')).toBeVisible()

  await card.getByRole('button', { name: 'Regel toevoegen' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('button', { name: 'Regel opslaan' }).click()
  await expect(dialog.getByText('Vul Spoel in.')).toBeVisible()
  await expect(dialog.getByRole('combobox', { name: 'Spoel' })).toBeFocused()

  await dialog.getByLabel('Soort regel').selectOption('labour')
  await dialog.getByLabel('Arbeid (minuten)').fill('15')
  await dialog.getByRole('button', { name: 'Regel opslaan' }).click()

  await expect(dialog).toBeHidden()
  expect(postedLine).toMatchObject({ kind: 'labour', minutes: 15, grams: null })
  await expect(card.getByText('15 min')).toBeVisible()
})

test('a cost snapshot can be saved and its history read', async ({ page }) => {
  await seedSignedInSession(page)
  const snapshot = {
    id: '77777777-7777-7777-7777-777777777777',
    variantId,
    createdAt: '2026-11-27T09:00:00Z',
    currency: 'EUR',
    totalCost: 7.1,
    price: 20,
    suggestedPrice: 11.83,
    profit: 12.9,
    marginPercent: 64.5,
    breakdown: cost,
  }
  await mockApi(page, {
    [`GET /api/products/${productId}`]: () => ({ status: 200, json: product }),
    [`GET /api/products/${productId}/variants/${variantId}/cost-snapshots`]: () => ({ status: 200, json: [snapshot] }),
    [`POST /api/products/${productId}/variants/${variantId}/cost-snapshots`]: () => ({ status: 201, json: snapshot }),
    ...supportData,
  })

  await page.goto(`/products/${productId}`)
  const card = page.getByLabel('Groot', { exact: true })
  await card.getByRole('button', { name: 'Kostprijs vastleggen' }).click()
  await expect(card.getByRole('status')).toContainText('Kostprijs vastgelegd')

  await card.getByText('Geschiedenis').click()
  await expect(card.getByText(/Kost .*7,10.*prijs .*20,00.*marge 64,5%/)).toBeVisible()
})

test('editing a variant saves the new price and archiving a product is possible', async ({ page }) => {
  await seedSignedInSession(page)
  let putBody: unknown
  let archived = false
  await mockApi(page, {
    [`GET /api/products/${productId}`]: () => ({ status: 200, json: product }),
    [`PUT /api/products/${productId}/variants/${variantId}`]: (body) => {
      putBody = body
      return { status: 200, json: { ...variant, price: 25 } }
    },
    [`POST /api/products/${productId}/archive`]: () => {
      archived = true
      return { status: 200, json: { ...product, isArchived: true } }
    },
    ...supportData,
  })

  await page.goto(`/products/${productId}`)
  const card = page.getByLabel('Groot', { exact: true })
  await card.getByRole('button', { name: 'Variant bewerken' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Verkoopprijs').fill('25,50')
  await dialog.getByRole('button', { name: 'Variant opslaan' }).click()
  await expect(dialog).toBeHidden()
  expect(putBody).toMatchObject({ name: 'Groot', price: 25.5, targetMarginPercent: 40 })

  await page.getByRole('button', { name: 'Product archiveren' }).click()
  await expect(page.getByText('Gearchiveerd')).toBeVisible()
  expect(archived).toBe(true)
})
