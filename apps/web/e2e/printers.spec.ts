import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const a1 = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'A1 desk',
  model: 'Bambu Lab A1',
  buildVolumeXMillimetres: 256,
  buildVolumeYMillimetres: 256,
  buildVolumeZMillimetres: 256,
  nozzleDiameterMillimetres: 0.4,
  powerDrawWatts: 95,
  purchaseCost: 339,
  lifetimeHours: 5000,
  depreciationPerHour: 0.0678,
  isArchived: false,
}
const { id: _id, depreciationPerHour: _rate, isArchived: _archived, ...a1Defaults } = a1

test('the printer list shows machines with their depreciation', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { 'GET /api/printers': () => ({ status: 200, json: [a1] }) })

  await page.goto('/printers')

  await expect(page.getByRole('heading', { level: 1, name: 'Printers' })).toBeVisible()
  await expect(page.getByRole('link', { name: /A1 desk/ })).toContainText('256×256×256 mm')
})

test('adding a printer starts from the Bambu Lab A1 defaults and shows the depreciation', async ({ page }) => {
  await seedSignedInSession(page)
  let created: unknown
  await mockApi(page, {
    'GET /api/printers': () => ({ status: 200, json: [] }),
    'GET /api/printers/defaults': () => ({ status: 200, json: a1Defaults }),
    'POST /api/printers': (body) => {
      created = body
      return { status: 201, json: { ...a1, ...(body as object) } }
    },
  })

  await page.goto('/printers/new')
  await expect(page.getByLabel('Model')).toHaveValue('Bambu Lab A1')
  await expect(page.getByLabel('Breedte (mm)')).toHaveValue('256')
  await expect(page.getByRole('status')).toContainText('Afschrijving')

  await page.getByRole('button', { name: 'Printer opslaan' }).click()
  await expect(page.getByText('Vul Naam in.')).toBeVisible()
  await expect(page.getByLabel('Naam', { exact: true })).toBeFocused()

  await page.getByLabel('Naam', { exact: true }).fill('A1 shelf')
  await page.getByRole('button', { name: 'Printer opslaan' }).click()
  await expect(page).toHaveURL(/\/printers$/)
  expect(created).toMatchObject({ name: 'A1 shelf', model: 'Bambu Lab A1', lifetimeHours: 5000 })
})

test('an existing printer can be archived', async ({ page }) => {
  await seedSignedInSession(page)
  let archivedCalled = false
  await mockApi(page, {
    'GET /api/printers': () => ({ status: 200, json: [] }),
    [`GET /api/printers/${a1.id}`]: () => ({ status: 200, json: a1 }),
    [`POST /api/printers/${a1.id}/archive`]: () => {
      archivedCalled = true
      return { status: 200, json: { ...a1, isArchived: true } }
    },
  })

  await page.goto(`/printers/${a1.id}`)
  await page.getByRole('button', { name: 'Printer archiveren' }).click()

  await expect(page).toHaveURL(/\/printers$/)
  expect(archivedCalled).toBe(true)
})
