import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession, tokenResponse, workspaceResponse } from './support'

const settings = {
  name: 'Milan Prints',
  currency: 'EUR',
  electricityPricePerKwh: 0.3,
  labourRatePerHour: 20,
  machineRatePerHour: 1,
  wastePercent: 5,
  language: 'en',
}

test('the sign-in screen is Dutch by default and sets the html lang', async ({ page }) => {
  await page.goto('/sign-in')
  await expect(page.getByRole('heading', { level: 1, name: 'Welkom terug' })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'nl-BE')
})

test('the workspace language wins over the browser default after sign-in', async ({ page }) => {
  await mockApi(page, {
    'POST /api/auth/sign-in': () => ({ status: 200, json: tokenResponse }),
    'GET /api/workspace': () => ({ status: 200, json: workspaceResponse }),
    'GET /api/workspace/settings': () => ({ status: 200, json: settings }),
  })

  await page.goto('/sign-in')
  await page.getByLabel('E-mailadres').fill('owner@example.com')
  await page.getByLabel('Wachtwoord', { exact: true }).fill('Correct-Horse-9')
  await page.getByRole('button', { name: 'Aanmelden', exact: true }).last().click()

  await expect(page.getByRole('navigation', { name: 'Main navigation' })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'en-GB')
})

test('an already signed-in session picks up the workspace language at app start', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/workspace': () => ({ status: 200, json: workspaceResponse }),
    'GET /api/workspace/settings': () => ({ status: 200, json: settings }),
    'POST /api/auth/sign-out': () => ({ status: 204 }),
  })

  await page.goto('/business')
  await expect(page.getByRole('heading', { level: 1, name: 'Business and operations' })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'en-GB')
})
