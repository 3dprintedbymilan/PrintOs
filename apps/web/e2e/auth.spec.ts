import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession, tokenResponse, workspaceResponse } from './support'

test('signed-out visitors are sent to the sign-in screen', async ({ page }) => {
  await page.goto('/')
  await expect(page).toHaveURL(/\/sign-in$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Welkom terug' })).toBeVisible()
})

test('empty submit shows field errors and focuses the first invalid field', async ({ page }) => {
  await page.goto('/sign-in')
  await page.getByRole('button', { name: 'Aanmelden', exact: true }).last().click()

  await expect(page.getByText('Vul een geldig e-mailadres in.')).toBeVisible()
  await expect(page.getByText('Vul je wachtwoord in.')).toBeVisible()
  await expect(page.getByLabel('E-mailadres')).toBeFocused()
})

test('create-account mode adds the business name and the password rules', async ({ page }) => {
  await page.goto('/sign-in')
  await page.getByRole('radio', { name: 'Account maken' }).check({ force: true })

  await expect(page.getByRole('heading', { level: 1, name: 'Richt je werkruimte in' })).toBeVisible()
  await expect(page.getByLabel('Naam van je bedrijf')).toBeVisible()
  await expect(page.getByText('Minstens 10 tekens met een hoofdletter, een kleine letter en een cijfer.')).toBeVisible()
})

test('the password can be shown and hidden', async ({ page }) => {
  await page.goto('/sign-in')
  const password = page.getByLabel('Wachtwoord', { exact: true })
  await password.fill('Secret-Pass-1')
  await expect(password).toHaveAttribute('type', 'password')

  await page.getByRole('button', { name: 'Toon wachtwoord' }).click()
  await expect(password).toHaveAttribute('type', 'text')
  await page.getByRole('button', { name: 'Verberg wachtwoord' }).click()
  await expect(password).toHaveAttribute('type', 'password')
})

test('signing in lands on Home and the Business tab offers sign-out', async ({ page }) => {
  await mockApi(page, {
    'POST /api/auth/sign-in': () => ({ status: 200, json: tokenResponse }),
    'GET /api/workspace': () => ({ status: 200, json: workspaceResponse }),
    'POST /api/auth/sign-out': () => ({ status: 204 }),
  })

  await page.goto('/sign-in')
  await page.getByLabel('E-mailadres').fill('owner@example.com')
  await page.getByLabel('Wachtwoord', { exact: true }).fill('Correct-Horse-9')
  await page.getByRole('button', { name: 'Aanmelden', exact: true }).last().click()

  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Productieoverzicht' })).toBeVisible()

  await page.getByRole('navigation', { name: 'Hoofdnavigatie' }).getByRole('link', { name: 'Bedrijf' }).click()
  await expect(page.getByText('owner@example.com')).toBeVisible()
  await expect(page.getByText('Milan Prints')).toBeVisible()

  await page.getByRole('button', { name: 'Afmelden' }).click()
  await expect(page).toHaveURL(/\/sign-in$/)
  expect(await page.evaluate(() => localStorage.getItem('printos_session'))).toBeNull()
})

test('wrong credentials show a clear message without leaving the screen', async ({ page }) => {
  await mockApi(page, { 'POST /api/auth/sign-in': () => ({ status: 401, json: { title: 'Invalid email or password.' } }) })

  await page.goto('/sign-in')
  await page.getByLabel('E-mailadres').fill('owner@example.com')
  await page.getByLabel('Wachtwoord', { exact: true }).fill('Wrong-Password-1')
  await page.getByRole('button', { name: 'Aanmelden', exact: true }).last().click()

  await expect(page.getByRole('alert')).toContainText('kloppen niet')
  await expect(page).toHaveURL(/\/sign-in$/)
})

test('a locked account explains how long to wait', async ({ page }) => {
  await mockApi(page, { 'POST /api/auth/sign-in': () => ({ status: 423, json: { title: 'Locked' } }) })

  await page.goto('/sign-in')
  await page.getByLabel('E-mailadres').fill('owner@example.com')
  await page.getByLabel('Wachtwoord', { exact: true }).fill('Correct-Horse-9')
  await page.getByRole('button', { name: 'Aanmelden', exact: true }).last().click()

  await expect(page.getByRole('alert')).toContainText('15 minuten')
})

test('creating an account sends the workspace name, then signs in', async ({ page }) => {
  let signUpBody: unknown
  await mockApi(page, {
    'POST /api/auth/sign-up': (body) => {
      signUpBody = body
      return { status: 201, json: workspaceResponse }
    },
    'POST /api/auth/sign-in': () => ({ status: 200, json: tokenResponse }),
    'GET /api/workspace': () => ({ status: 200, json: workspaceResponse }),
  })

  await page.goto('/sign-in')
  await page.getByRole('radio', { name: 'Account maken' }).check({ force: true })
  await page.getByLabel('Naam van je bedrijf').fill('Milan Prints')
  await page.getByLabel('E-mailadres').fill('owner@example.com')
  await page.getByLabel('Wachtwoord', { exact: true }).fill('Correct-Horse-9')
  await page.getByRole('button', { name: 'Account maken', exact: true }).last().click()

  await expect(page).toHaveURL(/\/$/)
  expect(signUpBody).toEqual({ email: 'owner@example.com', password: 'Correct-Horse-9', workspaceName: 'Milan Prints' })
})

test('a duplicate email from the server is shown on the email field', async ({ page }) => {
  await mockApi(page, {
    'POST /api/auth/sign-up': () => ({ status: 400, json: { errors: { DuplicateEmail: ['Email is already taken.'] } } }),
  })

  await page.goto('/sign-in?mode=sign-up')
  await page.getByLabel('Naam van je bedrijf').fill('Milan Prints')
  await page.getByLabel('E-mailadres').fill('owner@example.com')
  await page.getByLabel('Wachtwoord', { exact: true }).fill('Correct-Horse-9')
  await page.getByRole('button', { name: 'Account maken', exact: true }).last().click()

  await expect(page.getByText('Er bestaat al een account met dit e-mailadres.')).toBeVisible()
})

test('a signed-in visitor opening the sign-in screen is sent home', async ({ page }) => {
  await seedSignedInSession(page)
  await page.goto('/sign-in')
  await expect(page).toHaveURL(/\/$/)
})

test('pages get their own title and focus moves to the new heading after navigation', async ({ page }) => {
  await seedSignedInSession(page)
  await page.goto('/')
  await expect(page).toHaveTitle('Productieoverzicht · PrintOS')

  await page.getByRole('navigation', { name: 'Hoofdnavigatie' }).getByRole('link', { name: 'Prints' }).click()
  await expect(page).toHaveTitle('Prints · PrintOS')
  await expect(page.getByRole('heading', { level: 1, name: 'Prints' })).toBeFocused()
})

test('the submit button stays focusable while a sign-in request is running', async ({ page }) => {
  await page.route('**/api/auth/sign-in', async (route) => {
    if (route.request().method() === 'OPTIONS') {
      await route.fulfill({ status: 204, headers: { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*', 'access-control-allow-methods': '*' } })
      return
    }
    await new Promise(resolve => setTimeout(resolve, 600))
    await route.fulfill({ status: 401, headers: { 'access-control-allow-origin': '*' }, contentType: 'application/json', body: '{"title":"Invalid"}' })
  })

  await page.goto('/sign-in')
  await page.getByLabel('E-mailadres').fill('owner@example.com')
  await page.getByLabel('Wachtwoord', { exact: true }).fill('Wrong-Password-1')
  const submit = page.getByRole('button', { name: 'Aanmelden', exact: true }).last()
  await submit.focus()
  await submit.press('Enter')

  await expect(submit).toHaveAttribute('aria-busy', 'true')
  await expect(submit).toBeFocused()
  await expect(page.getByRole('alert')).toBeVisible()
  await expect(submit).toBeFocused()
})

test('the sign-in screen has no horizontal scroll', async ({ page }) => {
  await page.goto('/sign-in')
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(overflow).toBe(false)
})
