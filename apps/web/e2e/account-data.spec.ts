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

const baseHandlers = {
  'GET /api/workspace': () => ({ status: 200, json: workspaceResponse }),
  'GET /api/workspace/settings': () => ({ status: 200, json: settings }),
}

test('exporting needs the password, shows progress and starts a download', async ({ page }) => {
  await seedSignedInSession(page)
  let body: unknown
  await mockApi(page, {
    ...baseHandlers,
    'POST /api/account/export': (request) => {
      body = request
      return { status: 200, json: {} }
    },
  })

  await page.goto('/settings')
  await expect(page.getByRole('heading', { name: 'Account en gegevens' })).toBeVisible()
  await page.getByRole('button', { name: 'Mijn gegevens exporteren' }).click()
  await expect(page.getByText('Vul je wachtwoord in.')).toBeVisible()

  await page.getByLabel('Je wachtwoord').first().fill('Correct-Horse-9')
  const downloadPromise = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Mijn gegevens exporteren' }).click()
  const download = await downloadPromise

  expect(download.suggestedFilename()).toMatch(/^printos-export-\d{8}\.zip$/)
  expect(body).toEqual({ password: 'Correct-Horse-9' })
  await expect(page.getByRole('status').filter({ hasText: 'Download gestart' })).toBeVisible()
})

test('a wrong password and a rate limit are explained inline', async ({ page }) => {
  await seedSignedInSession(page)
  let calls = 0
  await mockApi(page, {
    ...baseHandlers,
    'POST /api/account/export': () => {
      calls += 1
      return calls === 1
        ? { status: 400, json: { errors: { password: ['The password is incorrect.'] } } }
        : { status: 429, json: { title: 'Too many exports.' } }
    },
  })

  await page.goto('/settings')
  await page.getByLabel('Je wachtwoord').first().fill('nope')
  await page.getByRole('button', { name: 'Mijn gegevens exporteren' }).click()
  await expect(page.getByText('Dat wachtwoord klopt niet.')).toBeVisible()

  await page.getByRole('button', { name: 'Mijn gegevens exporteren' }).click()
  await expect(page.getByRole('alert')).toContainText('Je hebt net al geëxporteerd')
})

test('deleting needs the password and the workspace name, then signs out', async ({ page }) => {
  await seedSignedInSession(page)
  let body: unknown
  await mockApi(page, {
    ...baseHandlers,
    'POST /api/account/delete': (request) => {
      body = request
      return { status: 204 }
    },
    'POST /api/auth/sign-out': () => ({ status: 401 }),
  })

  await page.goto('/settings')
  await page.getByRole('button', { name: 'Mijn account verwijderen' }).last().click()
  const dialog = page.getByRole('dialog', { name: 'Account verwijderen?' })
  await expect(dialog).toBeVisible()
  await expect(dialog.getByText('Dit gebeurt direct en kan niet ongedaan worden gemaakt.')).toBeVisible()

  await dialog.getByRole('button', { name: 'Alles definitief verwijderen' }).click()
  await expect(dialog.getByText('Vul je wachtwoord in.')).toBeVisible()
  await expect(dialog.getByText('De naam komt niet overeen')).toBeVisible()
  await expect(dialog.getByLabel('Je wachtwoord')).toBeFocused()

  await dialog.getByLabel('Je wachtwoord').fill('Correct-Horse-9')
  await dialog.getByRole('button', { name: 'Toon wachtwoord' }).click()
  await expect(dialog.getByLabel('Je wachtwoord')).toHaveAttribute('type', 'text')
  await dialog.getByLabel(/Typ de naam van de werkruimte/).fill('milan prints')
  await dialog.getByRole('button', { name: 'Alles definitief verwijderen' }).click()

  await expect(page).toHaveURL(/\/sign-in\?deleted=1/)
  await expect(page.getByRole('status').filter({ hasText: 'zijn verwijderd' })).toBeVisible()
  expect(body).toEqual({ password: 'Correct-Horse-9', workspaceName: 'milan prints' })
})

test('a rejected deletion keeps the dialog open with the error and removes nothing', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    ...baseHandlers,
    'POST /api/account/delete': () => ({ status: 400, json: { errors: { password: ['The password is incorrect.'] } } }),
  })

  await page.goto('/settings')
  await page.getByRole('button', { name: 'Mijn account verwijderen' }).last().click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Je wachtwoord').fill('wrong')
  await dialog.getByLabel(/Typ de naam van de werkruimte/).fill('Milan Prints')
  await dialog.getByRole('button', { name: 'Alles definitief verwijderen' }).click()

  await expect(dialog.getByText('Dat wachtwoord klopt niet.')).toBeVisible()
  await expect(page).toHaveURL(/\/settings$/)
})
