import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const productId = '33333333-3333-3333-3333-333333333333'
const variantId = '44444444-4444-4444-4444-444444444444'
const modelId = '77777777-7777-7777-7777-777777777777'
const printerId = '22222222-2222-2222-2222-222222222222'

const cost = {
  currency: 'EUR',
  lines: [{ kind: 'labour', label: 'labour', quantity: 0.25, unit: 'h', unitCost: 20, amount: 5 }],
  totalCost: 5,
  price: 20,
  targetMarginPercent: 40,
  feePercent: 0,
  suggestedPrice: 8.33,
  feeAmount: 0,
  profit: 15,
  marginPercent: 75,
  unpricedMaterials: [] as string[],
}

const blockedRights = {
  status: 'NotCleared',
  isCleared: false,
  expiringSoon: false,
  blockingModelCount: 1,
  models: [{ modelId, modelName: 'Spiraal STL', licenseName: 'Personal', licenseStatus: 'PersonalOnly', allowsCommercialSale: false, attributionRequired: false, daysUntilExpiry: null }],
  attributions: [],
}

const clearedRights = {
  status: 'Cleared',
  isCleared: true,
  expiringSoon: false,
  blockingModelCount: 0,
  models: [{ modelId, modelName: 'Spiraal STL', licenseName: 'CC', licenseStatus: 'Valid', allowsCommercialSale: true, attributionRequired: true, daysUntilExpiry: null }],
  attributions: [{ modelId, modelName: 'Spiraal STL', licenseName: 'CC', text: 'By Alice' }],
}

function variantWith(rights: unknown, extra: Record<string, unknown> = {}) {
  return {
    id: variantId,
    productId,
    name: 'Groot',
    sku: null,
    price: 20,
    targetMarginPercent: 40,
    feePercent: 0,
    bomLines: [],
    cost,
    readyForSale: false,
    saleRiskAcknowledged: false,
    commercialRights: rights,
    ...extra,
  }
}

function productWith(variant: unknown, status: string) {
  return {
    id: productId,
    name: 'Spiraalvaas',
    description: null,
    sku: null,
    isArchived: false,
    createdAt: '2026-11-27T08:00:00Z',
    currency: 'EUR',
    variants: [variant],
    commercialRightsStatus: status,
  }
}

const support = {
  'GET /api/printers': () => ({ status: 200, json: [] }),
  'GET /api/spools': () => ({ status: 200, json: [] }),
  'GET /api/materials': () => ({ status: 200, json: [] }),
  'GET /api/models': () => ({ status: 200, json: [] }),
}

test('a variant without valid rights shows a hard warning and asks for confirmation before sale', async ({ page }) => {
  await seedSignedInSession(page)
  const sent: unknown[] = []
  await mockApi(page, {
    [`GET /api/products/${productId}`]: () => ({ status: 200, json: productWith(variantWith(blockedRights), 'NotCleared') }),
    [`PUT /api/products/${productId}/variants/${variantId}/sale-status`]: (body) => {
      sent.push(body)
      return { status: 200, json: variantWith(blockedRights, { readyForSale: true, saleRiskAcknowledged: true }) }
    },
    ...support,
  })

  await page.goto(`/products/${productId}`)

  const card = page.getByLabel('Groot', { exact: true })
  await expect(page.getByRole('heading', { level: 2, name: 'Spiraalvaas' })).toBeVisible()
  await expect(card.getByRole('alert')).toContainText('Geen geldige commerciële licentie')
  await expect(card.getByRole('alert')).toContainText('Spiraal STL: Alleen persoonlijk')

  await card.getByRole('button', { name: 'Toch markeren als klaar voor verkoop' }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('Verkopen zonder geldige rechten?')
  await dialog.getByRole('button', { name: 'Annuleren' }).click()
  await expect(dialog).toBeHidden()
  expect(sent).toHaveLength(0)

  await card.getByRole('button', { name: 'Toch markeren als klaar voor verkoop' }).click()
  await dialog.getByRole('button', { name: 'Toch klaarzetten' }).click()

  await expect(dialog).toBeHidden()
  expect(sent).toEqual([{ readyForSale: true, acknowledgeRisk: true }])
  await expect(card).toContainText('Klaar voor verkoop, op eigen risico')
  await expect(card.getByRole('button', { name: 'Haal uit verkoop' })).toBeVisible()
})

test('a cleared variant is marked ready without a dialog and shows the attribution', async ({ page }) => {
  await seedSignedInSession(page)
  const sent: unknown[] = []
  await mockApi(page, {
    [`GET /api/products/${productId}`]: () => ({ status: 200, json: productWith(variantWith(clearedRights), 'Cleared') }),
    [`PUT /api/products/${productId}/variants/${variantId}/sale-status`]: (body) => {
      sent.push(body)
      return { status: 200, json: variantWith(clearedRights, { readyForSale: true }) }
    },
    ...support,
  })

  await page.goto(`/products/${productId}`)

  const card = page.getByLabel('Groot', { exact: true })
  await expect(card).toContainText('Commerciële rechten in orde')
  await expect(card).toContainText('Spiraal STL: By Alice')
  await card.getByRole('button', { name: 'Markeer als klaar voor verkoop' }).click()

  await expect(page.getByRole('dialog')).toBeHidden()
  expect(sent).toEqual([{ readyForSale: true, acknowledgeRisk: false }])
  await expect(card.getByText('Klaar voor verkoop', { exact: true })).toBeVisible()
})

test('a server refusal is explained inline', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    [`GET /api/products/${productId}`]: () => ({ status: 200, json: productWith(variantWith(clearedRights), 'Cleared') }),
    [`PUT /api/products/${productId}/variants/${variantId}/sale-status`]: () => ({ status: 400, json: { title: 'Validation', errors: { commercialRights: ['Not verified.'] } } }),
    ...support,
  })

  await page.goto(`/products/${productId}`)
  await page.getByLabel('Groot', { exact: true }).getByRole('button', { name: 'Markeer als klaar voor verkoop' }).click()

  await expect(page.getByRole('alert').filter({ hasText: 'De rechten zijn intussen gewijzigd' })).toBeVisible()
})

test('the product list flags products whose rights are not verified', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/products': () => ({
      status: 200,
      json: [
        { id: productId, name: 'Spiraalvaas', description: null, sku: null, isArchived: false, currency: 'EUR', variantCount: 1, lowestPrice: 20, highestPrice: 20, createdAt: '2026-11-27T08:00:00Z', commercialRightsStatus: 'NotCleared', notClearedVariantCount: 1 },
        { id: 'a', name: 'Schaal', description: null, sku: null, isArchived: false, currency: 'EUR', variantCount: 1, lowestPrice: 9, highestPrice: 9, createdAt: '2026-11-27T08:00:00Z', commercialRightsStatus: 'Cleared', notClearedVariantCount: 0 },
      ],
    }),
  })

  await page.goto('/products')

  await expect(page.getByRole('link', { name: /Spiraalvaas/ })).toContainText('Rechten ontbreken')
  await expect(page.getByRole('link', { name: /Schaal/ })).not.toContainText('Rechten ontbreken')
})

test('the print job form lets you pick a model and sends its id', async ({ page }) => {
  await seedSignedInSession(page)
  let posted: unknown
  const job = { id: 'job-1', printerId, printerName: 'A1', name: 'Run', status: 'Planned', startedAt: null, finishedAt: null, durationMinutes: null, notes: null, finishedUnits: null, externalSource: null, externalId: null, modelId, totalGrams: 0, wasteGrams: 0, createdAt: '2026-11-27T08:00:00Z', filaments: [] }
  await mockApi(page, {
    'GET /api/printers': () => ({ status: 200, json: [{ id: printerId, name: 'A1', model: 'Bambu Lab A1', isArchived: false }] }),
    'GET /api/spools': () => ({ status: 200, json: [] }),
    'GET /api/models': () => ({ status: 200, json: [{ id: modelId, name: 'Spiraal STL', description: null, tags: [], format: 'stl', sizeBytes: 1, versionCount: 1, thumbnailUrl: null, createdAt: '2026-11-27T08:00:00Z', updatedAt: '2026-11-27T08:00:00Z' }] }),
    'POST /api/print-jobs': (body) => {
      posted = body
      return { status: 201, json: job }
    },
    'GET /api/print-jobs/job-1': () => ({ status: 200, json: job }),
  })

  await page.goto('/print-jobs/new')
  await page.getByLabel('Naam').fill('Run')
  await page.getByLabel('Model (optioneel)').selectOption(modelId)
  await page.getByRole('button', { name: 'Print opslaan' }).click()

  await expect(page).toHaveURL(/\/print-jobs\/job-1$/)
  expect(posted).toMatchObject({ name: 'Run', modelId })
})
