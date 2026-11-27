import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const jan = {
  id: '33333333-3333-3333-3333-333333333333',
  name: 'Jan Peeters',
  email: 'jan@example.com',
  phone: '0470 12 34 56',
  address: 'Kerkstraat 1\n9000 Gent',
  vatNumber: null,
  notes: null,
  isArchived: false,
  createdAt: '2026-11-01T10:00:00Z',
}

test('the customer list shows contact details and is reachable from the Business tab', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { 'GET /api/customers': () => ({ status: 200, json: [jan] }) })

  await page.goto('/business')
  await page.getByRole('main').getByRole('link', { name: /Klanten/ }).click()

  await expect(page).toHaveURL(/\/customers$/)
  await expect(page.getByRole('link', { name: /Jan Peeters/ })).toContainText('jan@example.com')
})

test('an empty customer list invites adding the first customer', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { 'GET /api/customers': () => ({ status: 200, json: [] }) })

  await page.goto('/customers')

  await expect(page.getByText('Nog geen klanten')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Klant toevoegen' })).toBeVisible()
})

test('adding a customer validates inline and saves', async ({ page }) => {
  await seedSignedInSession(page)
  let created: unknown
  await mockApi(page, {
    'GET /api/customers': () => ({ status: 200, json: [] }),
    'POST /api/customers': (body) => {
      created = body
      return { status: 201, json: { ...jan, ...(body as object) } }
    },
  })

  await page.goto('/customers/new')
  await page.getByRole('button', { name: 'Klant opslaan' }).click()
  await expect(page.getByText('Vul Naam in.')).toBeVisible()
  await expect(page.getByLabel('Naam', { exact: true })).toBeFocused()

  await page.getByLabel('Naam', { exact: true }).fill('Fatima')
  await page.getByLabel('E-mail').fill('niet-geldig')
  await page.getByRole('button', { name: 'Klant opslaan' }).click()
  await expect(page.getByText('Vul een geldig e-mailadres in.')).toBeVisible()

  await page.getByLabel('E-mail').fill('fatima@example.com')
  await page.getByRole('button', { name: 'Klant opslaan' }).click()
  await expect(page).toHaveURL(/\/customers$/)
  expect(created).toMatchObject({ name: 'Fatima', email: 'fatima@example.com', phone: null })
})

test('an existing customer can be archived', async ({ page }) => {
  await seedSignedInSession(page)
  let archivedCalled = false
  await mockApi(page, {
    'GET /api/customers': () => ({ status: 200, json: [] }),
    [`GET /api/customers/${jan.id}`]: () => ({ status: 200, json: jan }),
    'GET /api/orders': () => ({ status: 200, json: [] }),
    [`POST /api/customers/${jan.id}/archive`]: () => {
      archivedCalled = true
      return { status: 200, json: { ...jan, isArchived: true } }
    },
  })

  await page.goto(`/customers/${jan.id}`)
  await expect(page.getByLabel('Naam', { exact: true })).toHaveValue('Jan Peeters')
  await page.getByRole('button', { name: 'Klant archiveren' }).click()

  await expect(page).toHaveURL(/\/customers$/)
  expect(archivedCalled).toBe(true)
})
