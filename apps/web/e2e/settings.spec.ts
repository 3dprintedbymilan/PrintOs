import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession, workspaceResponse } from './support'

const settings = {
  name: 'Milan Prints',
  currency: 'EUR',
  electricityPricePerKwh: 0.3,
  labourRatePerHour: 20,
  machineRatePerHour: 1,
  wastePercent: 5,
  language: 'nl',
}

test('the Business tab links to settings, which load the saved values', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/workspace': () => ({ status: 200, json: workspaceResponse }),
    'GET /api/workspace/settings': () => ({ status: 200, json: settings }),
  })

  await page.goto('/business')
  await page.locator('main').getByRole('link', { name: /Instellingen/ }).click()

  await expect(page).toHaveURL(/\/settings$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Instellingen' })).toBeVisible()
  await expect(page.getByLabel('Naam van je bedrijf')).toHaveValue('Milan Prints')
  await expect(page.getByLabel('Elektriciteitsprijs (per kWh)')).toHaveValue('0,3')
})

test('invalid numbers show field errors and focus the first one', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/workspace/settings': () => ({ status: 200, json: settings }),
  })

  await page.goto('/settings')
  await page.getByLabel('Uurtarief arbeid').fill('abc')
  await page.getByRole('button', { name: 'Instellingen opslaan' }).click()

  await expect(page.getByText('Uurtarief arbeid moet een getal zijn, bijvoorbeeld 0,30.')).toBeVisible()
  await expect(page.getByLabel('Uurtarief arbeid')).toBeFocused()
})

test('saving accepts a decimal comma, confirms in a status message and switches language', async ({ page }) => {
  await seedSignedInSession(page)
  let saved: unknown
  await mockApi(page, {
    'GET /api/workspace': () => ({ status: 200, json: workspaceResponse }),
    'GET /api/workspace/settings': () => ({ status: 200, json: settings }),
    'PUT /api/workspace/settings': (body) => {
      saved = body
      return { status: 200, json: body }
    },
  })

  await page.goto('/settings')
  await page.getByLabel('Elektriciteitsprijs (per kWh)').fill('0,25')
  await page.getByLabel('Taal').selectOption('en')
  await page.getByRole('button', { name: 'Instellingen opslaan' }).click()

  await expect(page.getByRole('status')).toHaveText('Settings saved.')
  await expect(page.getByRole('button', { name: 'Save settings' })).toBeVisible()
  expect(saved).toMatchObject({ electricityPricePerKwh: 0.25, language: 'en', name: 'Milan Prints' })
})

test('a failed save shows an error without losing the typed values', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/workspace/settings': () => ({ status: 200, json: settings }),
    'PUT /api/workspace/settings': () => ({ status: 500 }),
  })

  await page.goto('/settings')
  await page.getByLabel('Afvalmarge (%)').fill('7')
  await page.getByRole('button', { name: 'Instellingen opslaan' }).click()

  await expect(page.getByRole('alert')).toContainText('konden niet worden opgeslagen')
  await expect(page.getByLabel('Afvalmarge (%)')).toHaveValue('7')
})
