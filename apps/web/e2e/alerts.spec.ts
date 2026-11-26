import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const alerts = [
  { materialId: 'm1', materialName: 'TPU', severity: 'LowStock', totalGrams: 100, thresholdGrams: 150 },
  { materialId: 'm2', materialName: 'PETG', severity: 'ReorderSoon', totalGrams: 400, thresholdGrams: 500 },
]

test('Home shows stock alerts ranked with their severity', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { 'GET /api/inventory/alerts': () => ({ status: 200, json: alerts }) })

  await page.goto('/')

  const list = page.getByRole('list').filter({ hasText: 'TPU' })
  await expect(list.getByRole('listitem').first()).toContainText('Lage voorraad')
  await expect(list.getByRole('listitem').nth(1)).toContainText('Binnenkort bijbestellen')
  await expect(list.getByRole('listitem').first()).toContainText('100 g over, grens 150 g')
})

test('Home says all is well when nothing is below a threshold', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { 'GET /api/inventory/alerts': () => ({ status: 200, json: [] }) })

  await page.goto('/')
  await expect(page.getByText('Alle materialen zitten boven hun grens.')).toBeVisible()
})

test('thresholds can be edited per material with validation', async ({ page }) => {
  await seedSignedInSession(page)
  let saved: unknown
  const material = { id: 'm1', name: 'PLA Basic', type: 'PLA', lowStockGrams: null, reorderGrams: null }
  await mockApi(page, {
    'GET /api/materials': () => ({ status: 200, json: [material] }),
    'PUT /api/materials/m1/thresholds': (body) => {
      saved = body
      return { status: 200, json: { ...material, ...(body as object) } }
    },
  })

  await page.goto('/inventory/materials')
  await page.getByLabel('Grens lage voorraad (gram)').fill('500')
  await page.getByLabel('Grens bijbestellen (gram)').fill('200')
  await page.getByRole('button', { name: 'Grenzen opslaan' }).click()
  await expect(page.getByText('De bijbestelgrens moet minstens gelijk zijn')).toBeVisible()

  await page.getByLabel('Grens bijbestellen (gram)').fill('800')
  await page.getByRole('button', { name: 'Grenzen opslaan' }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Grenzen opgeslagen.' })).toBeVisible()
  expect(saved).toEqual({ lowStockGrams: 500, reorderGrams: 800 })
})
