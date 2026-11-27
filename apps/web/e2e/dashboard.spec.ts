import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const trend = Array.from({ length: 7 }, (_, index) => ({ date: `2026-11-${21 + index}`, printHours: index === 6 ? 3 : index === 3 ? 1.5 : 0, prints: index === 6 || index === 3 ? 1 : 0 }))

function summary(period: string, overrides: Record<string, unknown> = {}) {
  return {
    period,
    from: '2026-11-21T00:00:00Z',
    to: '2026-11-27T15:00:00Z',
    currency: 'EUR',
    production: { printsDone: 8, printsFailed: 2, successRatePercent: 80, printHours: 24.5, filamentKilograms: 0.62, filamentCost: 12.4, trend },
    printers: { total: 2, online: 1, printing: 1 },
    stock: { spools: 4, totalGrams: 3200, alertCount: 1 },
    licenses: { expiringSoon: 2, expired: 0 },
    recentPrints: [
      { id: 'j1', name: 'Benchy', printerName: 'A1 #1', status: 'Done', at: '2026-11-27T10:00:00Z', durationMinutes: 95, grams: 42 },
      { id: 'j2', name: 'Vase', printerName: 'A1 #1', status: 'Failed', at: '2026-11-26T10:00:00Z', durationMinutes: 30, grams: 10 },
    ],
    ...overrides,
  }
}

const quiet = {
  'GET /api/inventory/alerts': () => ({ status: 200, json: [] }),
  'GET /api/bambu/status': () => ({ status: 200, json: { connected: false, connectionStatus: null, printers: [] } }),
}

test('dashboard shows KPI tiles, recent prints and quick actions', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { ...quiet, 'GET /api/dashboard/summary': () => ({ status: 200, json: summary('7d') }) })

  await page.goto('/')

  const kpis = page.getByRole('group', { name: 'Kerncijfers' })
  await expect(kpis).toContainText('Prints klaar')
  await expect(kpis).toContainText('80% geslaagd')
  await expect(kpis).toContainText('24,5 u')
  await expect(kpis).toContainText('1 van 2')
  await expect(kpis).toContainText('1 melding')
  await expect(page.getByRole('link', { name: /2 licenties verlopen binnen 30 dagen/ })).toBeVisible()
  await expect(page.getByRole('link', { name: /Benchy/ })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Print toevoegen' })).toHaveAttribute('href', '/print-jobs/new')
  await expect(page.getByRole('link', { name: 'Spool toevoegen' })).toBeVisible()
  await expect(page.locator('main').getByRole('link', { name: 'Kostencalculator' })).toBeVisible()
})

test('switching the period reloads the summary', async ({ page }) => {
  await seedSignedInSession(page)
  const requested: string[] = []
  await mockApi(page, quiet)
  await page.route('**/api/dashboard/summary?*', async (route) => {
    const period = new URL(route.request().url()).searchParams.get('period') ?? ''
    requested.push(period)
    await route.fulfill({
      status: 200,
      headers: { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*' },
      contentType: 'application/json',
      body: JSON.stringify(summary(period, period === '30d' ? { production: { ...summary('30d').production, printsDone: 31 } } : {})),
    })
  })
  await page.goto('/')
  await expect(page.getByRole('group', { name: 'Kerncijfers' })).toContainText('8')
  await page.getByRole('radio', { name: '30 dagen' }).check({ force: true })

  await expect(page.getByRole('group', { name: 'Kerncijfers' })).toContainText('31')
  expect(requested).toEqual(['7d', '30d'])
})

test('dashboard explains an empty period', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    ...quiet,
    'GET /api/dashboard/summary': () => ({
      status: 200,
      json: summary('7d', {
        production: { printsDone: 0, printsFailed: 0, successRatePercent: null, printHours: 0, filamentKilograms: 0, filamentCost: 0, trend: [] },
        printers: { total: 0, online: null, printing: 0 },
        stock: { spools: 0, totalGrams: 0, alertCount: 0 },
        licenses: { expiringSoon: 0, expired: 0 },
        recentPrints: [],
      }),
    }),
  })

  await page.goto('/')

  await expect(page.getByText('In deze periode is nog niets geprint.')).toBeVisible()
  await expect(page.getByText('Nog geen prints gelogd.')).toBeVisible()
})

test('dashboard shows an error with retry', async ({ page }) => {
  await seedSignedInSession(page)
  let calls = 0
  await mockApi(page, {
    ...quiet,
    'GET /api/dashboard/summary': () => (++calls <= 2 ? { status: 500 } : { status: 200, json: summary('7d') }),
  })

  await page.goto('/')
  const retry = page.getByRole('button', { name: /opnieuw/i })
  await expect(retry).toBeVisible()
  await retry.click()

  await expect(page.getByRole('group', { name: 'Kerncijfers' })).toBeVisible()
})
