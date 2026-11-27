import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const productId = 'a1111111-1111-1111-1111-111111111111'

function figures(overrides: Record<string, unknown> = {}) {
  return { units: 3, revenue: 60, cost: 10.5, grossProfit: 49.5, marginPercent: 82.5, costComplete: true, standCosts: 0, netProfit: 49.5, netMarginPercent: 82.5, ...overrides }
}

const row = (key: string, label: string, overrides: Record<string, unknown> = {}, ids: Record<string, unknown> = {}) => ({ key, productId: null, variantId: null, label, sku: null, figures: figures(overrides), ...ids })

const report = {
  period: { kind: 'month', from: '2026-11-01', to: '2026-11-30', previousFrom: '2026-10-01', previousTo: '2026-10-31', days: 30 },
  currency: 'EUR',
  channel: 'all',
  current: figures({ standCosts: 45, netProfit: 4.5, netMarginPercent: 7.5 }),
  previous: figures({ revenue: 40, cost: 10, grossProfit: 30, marginPercent: 75 }),
  delta: { revenueChange: 20, revenueChangePercent: 50, grossProfitChange: 19.5, grossProfitChangePercent: 65, marginPointsChange: 7.5 },
  granularity: 'day',
  trend: [
    { start: '2026-11-01', revenue: 0, cost: 0, grossProfit: 0, marginPercent: null },
    { start: '2026-11-02', revenue: 40, cost: 7, grossProfit: 33, marginPercent: 82.5 },
    { start: '2026-11-03', revenue: 20, cost: 3.5, grossProfit: 16.5, marginPercent: 82.5 },
  ],
  channels: [row('orders', 'orders', { units: 2, revenue: 40 }), row('pos', 'pos', { units: 1, revenue: 20, standCosts: 45, netProfit: -28.5 })],
  markets: [row('m1', 'Kerstmarkt Gent', { units: 1, revenue: 20, standCosts: 45, netProfit: -28.5 })],
  products: [row(productId, 'Vaas', {}, { productId })],
  variants: [row('v1', 'Vaas - Groot', {}, { productId, variantId: 'v1' })],
}

test.beforeEach(async ({ page }) => {
  await seedSignedInSession(page)
})

test('the margin report shows figures, comparison, stand costs and breakdowns', async ({ page }) => {
  await mockApi(page, { 'GET /api/reports/margin': () => ({ status: 200, json: report }) })

  await page.goto('/business')
  await page.getByRole('main').getByRole('link', { name: /Rapporten/ }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Rapporten' })).toBeVisible()
  await expect(page.getByRole('main').getByText('Topproducten op winst')).toBeVisible()
  await page.getByRole('main').getByRole('link', { name: /Winstmarges/ }).click()

  const main = page.getByRole('main')
  await expect(page.getByRole('heading', { level: 1, name: 'Winstmarges' })).toBeVisible()
  await expect(main.getByText('+50% t.o.v. vorige periode')).toBeVisible()
  await expect(main.getByText('+7,5 pt t.o.v. vorige periode')).toBeVisible()
  await expect(main.getByText('82,5%').first()).toBeVisible()
  await expect(main.getByRole('heading', { name: 'Na standkosten van markten' })).toBeVisible()
  await expect(main.getByText('Kerstmarkt Gent')).toBeVisible()
  await expect(main.getByRole('link', { name: 'Vaas' })).toBeVisible()
  await expect(main.getByRole('status')).toContainText('Tik op een balk')

  await main.getByRole('button', { name: 'Toon als tabel' }).click()
  await expect(main.getByRole('table')).toContainText('2 nov')
})

test('changing the channel and a custom range reloads with validation', async ({ page }) => {
  const urls: string[] = []
  await mockApi(page, { 'GET /api/reports/margin': () => ({ status: 200, json: report }) })
  page.on('request', (request) => {
    if (request.url().includes('/api/reports/margin')) urls.push(request.url())
  })

  await page.goto('/reports/margin')
  await expect(page.getByRole('main').getByText('Per kanaal')).toBeVisible()
  await page.getByRole('radio', { name: 'Markt-kassa' }).check()
  await expect.poll(() => urls.some(url => url.includes('channel=pos'))).toBe(true)

  await page.getByLabel('Periode').selectOption('custom')
  await page.getByLabel('Tot', { exact: true }).fill('2026-01-01')
  await page.getByLabel('Van', { exact: true }).fill('2026-02-01')
  await expect(page.getByText('Vul een einddatum in op of na de begindatum.')).toBeVisible()
  await page.getByLabel('Tot', { exact: true }).fill('2026-03-01')
  await expect.poll(() => urls.some(url => url.includes('period=custom') && url.includes('from=2026-02-01') && url.includes('to=2026-03-01'))).toBe(true)
})

test('an empty period, a failing API and the product drill-down', async ({ page }) => {
  const empty = { ...report, current: figures({ units: 0, revenue: 0, cost: 0, grossProfit: 0, marginPercent: null, netProfit: 0, netMarginPercent: null }), channels: [], markets: [], products: [], variants: [], trend: [] }
  let calls = 0
  await mockApi(page, {
    'GET /api/reports/margin': () => {
      calls++
      return calls <= 2 ? { status: 500 } : calls === 3 ? { status: 200, json: empty } : { status: 200, json: report }
    },
  })

  await page.goto('/reports/margin')
  await expect(page.getByRole('alert')).toBeVisible()
  await page.getByRole('button', { name: 'Opnieuw proberen' }).click()
  await expect(page.getByText('Nog geen verkopen in deze periode')).toBeVisible()

  await page.getByLabel('Periode').selectOption('week')
  await page.getByRole('main').getByRole('link', { name: 'Vaas' }).click()
  await expect(page).toHaveURL(new RegExp(`productId=${productId}`))
  await expect(page.getByRole('main').getByText('Per variant')).toBeVisible()
})

test('the reports hub links to detail reports and downloads CSV exports', async ({ page }) => {
  const requested: string[] = []
  await mockApi(page, {
    'GET /api/reports/margin': () => ({ status: 200, json: report }),
    'GET /api/reports/margin/export': () => ({ status: 200, json: {} }),
    'GET /api/reports/slow-movers/export': () => ({ status: 200, json: {} }),
  })
  page.on('request', (request) => {
    if (request.url().includes('/export')) requested.push(request.url())
  })

  await page.goto('/reports')
  const main = page.getByRole('main')
  await expect(main.getByRole('link', { name: /Bestsellers/ })).toBeVisible()

  const download = page.waitForEvent('download')
  await main.getByRole('button', { name: 'Margerapport downloaden (CSV)' }).click()
  expect((await download).suggestedFilename()).toBe('margin-2026-11-01_2026-11-30.csv')
  await expect(main.getByRole('status').filter({ hasText: 'is gedownload' })).toBeVisible()
  expect(requested.some(url => url.includes('/api/reports/margin/export?period=month'))).toBe(true)

  await main.getByRole('button', { name: 'Verloop downloaden (CSV)' }).click()
  await expect.poll(() => requested.some(url => url.includes('part=trend'))).toBe(true)
})

test('a failing export shows an error and the hub drills down to a product', async ({ page }) => {
  await mockApi(page, { 'GET /api/reports/margin': () => ({ status: 200, json: report }) })

  await page.goto('/reports?period=year')
  const main = page.getByRole('main')
  await main.getByRole('button', { name: 'Bestsellers downloaden (CSV)' }).click()
  await expect(main.getByRole('alert')).toContainText('De export is mislukt')

  await main.getByRole('link', { name: 'Vaas' }).click()
  await expect(page).toHaveURL(new RegExp(`/reports/margin\\?period=year&productId=${productId}`))
  await expect(main.getByText('Per variant')).toBeVisible()
})
