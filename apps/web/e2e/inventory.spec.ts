import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const spool = {
  id: '33333333-3333-3333-3333-333333333333',
  materialId: '44444444-4444-4444-4444-444444444444',
  materialName: 'PLA Basic',
  supplierId: null,
  supplierName: 'Bambu Lab',
  colorName: 'Jade White',
  colorHex: '#F4F4F0',
  cost: 20,
  costPerGram: 0.02,
  initialWeightGrams: 1000,
  remainingWeightGrams: 750,
  isArchived: false,
}
const material = { id: spool.materialId, name: 'PLA Basic', type: 'PLA' }

test('the inventory shows the stock summary and spools, and filters by search', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/spools': () => ({ status: 200, json: [spool, { ...spool, id: 'x', colorName: 'Black', colorHex: '#111111', remainingWeightGrams: 250 }] }),
    'GET /api/materials': () => ({ status: 200, json: [material] }),
  })

  await page.goto('/inventory')

  await expect(page.getByRole('heading', { level: 1, name: 'Voorraad' })).toBeVisible()
  await expect(page.getByText('1 kg')).toBeVisible()
  await expect(page.getByRole('link', { name: /Jade White/ })).toContainText('750 g over (75%)')

  await page.getByLabel('Spoelen zoeken').fill('black')
  await expect(page.getByRole('link', { name: /Jade White/ })).toHaveCount(0)
  await expect(page.getByRole('link', { name: /Black/ })).toBeVisible()
  await page.getByLabel('Spoelen zoeken').fill('zzz')
  await expect(page.getByText('Geen spoelen gevonden voor je zoekopdracht.')).toBeVisible()
})

test('an empty inventory invites adding the first spool', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/spools': () => ({ status: 200, json: [] }),
    'GET /api/materials': () => ({ status: 200, json: [] }),
  })

  await page.goto('/inventory')
  await expect(page.getByText('Nog geen spoelen. Voeg je eerste spoel toe.')).toBeVisible()
})

test('a failed load offers a retry', async ({ page }) => {
  await seedSignedInSession(page)
  let calls = 0
  await mockApi(page, {
    'GET /api/spools': () => (++calls <= 2 ?{ status: 500 } : { status: 200, json: [spool] }),
    'GET /api/materials': () => ({ status: 200, json: [material] }),
  })

  await page.goto('/inventory')
  await page.getByRole('button', { name: 'Opnieuw proberen' }).click()
  await expect(page.getByRole('link', { name: /Jade White/ })).toBeVisible()
})

test('adding a spool with a new material creates the material first', async ({ page }) => {
  await seedSignedInSession(page)
  const calls: string[] = []
  await mockApi(page, {
    'GET /api/materials': () => ({ status: 200, json: [] }),
    'GET /api/suppliers': () => ({ status: 200, json: [] }),
    'POST /api/materials': (body) => {
      calls.push('material')
      return { status: 201, json: { id: material.id, ...(body as object) } }
    },
    'POST /api/spools': (body) => {
      calls.push('spool')
      expect(body).toMatchObject({ materialId: material.id, colorName: 'Jade White', cost: 19.99, initialWeightGrams: 1000 })
      return { status: 201, json: spool }
    },
    [`GET /api/spools/${spool.id}`]: () => ({ status: 200, json: spool }),
    [`GET /api/spools/${spool.id}/movements`]: () => ({ status: 200, json: [] }),
  })

  await page.goto('/inventory/new')
  await page.getByRole('button', { name: 'Spoel opslaan' }).click()
  await expect(page.getByText('Vul Materiaalnaam in.')).toBeVisible()
  await expect(page.getByLabel('Materiaalnaam')).toBeFocused()

  await page.getByLabel('Materiaalnaam').fill('PLA Basic')
  await page.getByLabel('Soort materiaal').fill('PLA')
  await page.getByLabel('Kleur', { exact: true }).fill('Jade White')
  await page.getByLabel('Aankoopprijs').fill('19,99')
  await page.getByRole('button', { name: 'Spoel opslaan' }).click()

  await expect(page).toHaveURL(new RegExp(`/inventory/${spool.id}$`))
  expect(calls).toEqual(['material', 'spool'])
})

test('adjusting stock requires a reason, then updates the balance and history', async ({ page }) => {
  await seedSignedInSession(page)
  let current = spool
  const movements = [{ id: 'm1', kind: 'Initial', deltaGrams: 1000, balanceAfterGrams: 1000, reason: 'Spool added', createdAt: '2026-11-26T10:00:00Z' }]
  await mockApi(page, {
    [`GET /api/spools/${spool.id}`]: () => ({ status: 200, json: current }),
    [`GET /api/spools/${spool.id}/movements`]: () => ({ status: 200, json: movements }),
    [`POST /api/spools/${spool.id}/adjustments`]: (body) => {
      const adjustment = body as { deltaGrams: number, reason: string }
      current = { ...current, remainingWeightGrams: current.remainingWeightGrams + adjustment.deltaGrams }
      movements.unshift({ id: 'm2', kind: 'Adjustment', ...adjustment, balanceAfterGrams: current.remainingWeightGrams, createdAt: '2026-11-26T11:00:00Z' })
      return { status: 200, json: current }
    },
  })

  await page.goto(`/inventory/${spool.id}`)
  await page.getByLabel('Hoeveelheid (gram)').fill('50')
  await page.getByRole('button', { name: 'Aanpassing opslaan' }).click()
  await expect(page.getByText('Vul een reden in zodat de wijziging traceerbaar blijft.')).toBeVisible()
  await expect(page.getByLabel('Reden')).toBeFocused()

  await page.getByLabel('Reden').fill('Mislukte print')
  await page.getByRole('button', { name: 'Aanpassing opslaan' }).click()

  await expect(page.getByRole('status').filter({ hasText: 'Voorraad aangepast.' })).toBeVisible()
  await expect(page.getByText('700 g over (70%)')).toBeVisible()
  await expect(page.getByRole('region', { name: 'Bewegingen' }).getByText('Mislukte print')).toBeVisible()
})
