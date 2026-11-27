import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const period = { kind: 'month', from: '2026-11-01', to: '2026-11-30', previousFrom: '2026-10-01', previousTo: '2026-10-31', days: 30 }
const productId = 'a1111111-1111-1111-1111-111111111111'
const customerId = 'c1111111-1111-1111-1111-111111111111'

const productionTotals = (overrides: Record<string, unknown> = {}) => ({
  jobs: 4, done: 3, failed: 1, cancelled: 0, successRatePercent: 75, printHours: 12.5, utilisationPercent: 1.7, filamentGrams: 320, wasteGrams: 24,
  wastePercent: 7.5, filamentCost: 6.4, runningCost: 9.9, costPerPrintHour: 0.79, jobsWithEstimate: 2, averageActualMinutes: 130, averageEstimatedMinutes: 120,
  durationVariancePercent: 8.3, ...overrides,
})

const production = {
  period,
  currency: 'EUR',
  hoursPerDay: 24,
  current: productionTotals(),
  previous: productionTotals({ jobs: 2, done: 2, failed: 0, printHours: 5, filamentGrams: 100, successRatePercent: 100, wastePercent: 5 }),
  granularity: 'day',
  trend: [
    { start: '2026-11-02', jobs: 2, failed: 0, printHours: 4, filamentGrams: 120 },
    { start: '2026-11-03', jobs: 2, failed: 1, printHours: 8.5, filamentGrams: 200 },
  ],
  printers: [{ printerId: 'p1', name: 'A1 Keuken', done: 3, failed: 1, successRatePercent: 75, printHours: 12.5, utilisationPercent: 1.7, filamentGrams: 320, wasteGrams: 24, wastePercent: 7.5, costPerPrintHour: 0.79 }],
  materials: [{ material: 'PLA Basic', jobs: 4, failed: 1, failureRatePercent: 25, grams: 320, wasteGrams: 24, wastePercent: 7.5, failedGrams: 40 }],
}

const salesTotals = (overrides: Record<string, unknown> = {}) => ({
  documents: 5, revenue: 250, averageOrderValue: 50, discount: 20, discountPercent: 7.4, discountedDocuments: 2, customers: 2, repeatCustomers: 1, repeatRatePercent: 50, ...overrides,
})

const sales = {
  period,
  currency: 'EUR',
  channel: 'all',
  current: salesTotals(),
  previous: salesTotals({ documents: 3, revenue: 100, averageOrderValue: 33.3, discount: 0 }),
  granularity: 'day',
  trend: [{ start: '2026-11-02', documents: 2, revenue: 100 }, { start: '2026-11-03', documents: 3, revenue: 150 }],
  channels: [
    { key: 'online', documents: 3, revenue: 150, averageOrderValue: 50, revenueSharePercent: 60, discount: 10 },
    { key: 'pos', documents: 2, revenue: 100, averageOrderValue: 50, revenueSharePercent: 40, discount: 10 },
  ],
  customers: [
    { customerId, label: 'Anna Peeters', documents: 2, revenue: 120, averageOrderValue: 60, repeat: true, revenueSharePercent: 48 },
    { customerId: null, label: '', documents: 3, revenue: 130, averageOrderValue: 43.3, repeat: false, revenueSharePercent: 52 },
  ],
  products: [{ rank: 1, key: productId, productId, variantId: null, label: 'Vaas', sku: null, units: 6, revenue: 250, profit: 200, marginPercent: 80, unitsSharePercent: 100, revenueSharePercent: 100, profitSharePercent: 100 }],
  leadTime: { orders: 2, averageDays: 4.5, medianDays: 4.5, longestDays: 6, buckets: [{ key: 'sameDay', orders: 0 }, { key: '1to3', orders: 0 }, { key: '4to7', orders: 2 }, { key: '8to14', orders: 0 }, { key: 'over14', orders: 0 }] },
  marketTiming: {
    sales: 2,
    revenue: 100,
    weekdays: [],
    hours: [],
    cells: [{ weekday: 6, hour: 14, sales: 2, revenue: 100 }],
  },
}

const inventory = {
  period,
  currency: 'EUR',
  deadDays: 90,
  summary: { filamentValue: 120, filamentGrams: 6000, finishedValue: 80, finishedUnits: 12, totalValue: 200, filamentTurnover: 0.4, finishedTurnover: 0.6, filamentDaysOfCover: 75, finishedDaysOfCover: 20, deadStockValue: 30 },
  materials: [{ material: 'PLA Basic', onHandGrams: 6000, value: 120, usedGrams: 600, usedCost: 12, usageGramsPerDay: 20, daysOfCover: 300, turnover: 0.1, pricePerKgCurrent: 22, pricePerKgPrevious: 18, priceChangePercent: 22.2, priceImpact: 2.4 }],
  finishedGoods: [{ productId, label: 'Vaas', onHand: 12, value: 80, unitsSold: 6, daysOfCover: 60, turnover: 0.6 }],
  deadFilament: [{ spoolId: 's1', material: 'PETG', color: 'Zwart', remainingGrams: 700, value: 14, daysSinceLastUse: 120 }],
  deadFinished: [{ productId, variantId: 'v1', label: 'Vaas - Groot', onHand: 3, stockValue: 16, daysSinceLastSale: null }],
}

const profit = {
  period,
  currency: 'EUR',
  channel: 'all',
  current: { revenue: 250, cost: 50, fees: 10, contribution: 190, contributionMarginPercent: 76, standCosts: 45, netProfit: 145, netMarginPercent: 58, costComplete: true },
  previous: { revenue: 100, cost: 20, fees: 0, contribution: 80, contributionMarginPercent: 80, standCosts: 0, netProfit: 80, netMarginPercent: 80, costComplete: true },
  components: [
    { kind: 'material', amount: 30, sharePercent: 60 },
    { kind: 'electricity', amount: 5, sharePercent: 10 },
    { kind: 'unallocated', amount: 15, sharePercent: 30 },
  ],
  granularity: 'day',
  trend: [{ start: '2026-11-02', revenue: 100, cost: 20, contribution: 75, net: 75 }, { start: '2026-11-03', revenue: 150, cost: 30, contribution: 115, net: 70 }],
  channels: [
    { key: 'orders', id: null, label: 'orders', figures: { units: 3, revenue: 150, cost: 30, fees: 10, contribution: 110, contributionMarginPercent: 73.3, costComplete: true } },
    { key: 'pos', id: null, label: 'pos', figures: { units: 3, revenue: 100, cost: 20, fees: 0, contribution: 80, contributionMarginPercent: 80, costComplete: true } },
  ],
  products: [{ key: productId, id: productId, label: 'Vaas', figures: { units: 6, revenue: 250, cost: 50, fees: 10, contribution: 190, contributionMarginPercent: 76, costComplete: true } }],
  customers: [{ key: customerId, id: customerId, label: 'Anna Peeters', figures: { units: 2, revenue: 120, cost: 20, fees: 5, contribution: 95, contributionMarginPercent: 79.2, costComplete: true } }],
}

test.beforeEach(async ({ page }) => {
  await seedSignedInSession(page)
})

test('production analytics shows figures, charts, a table alternative and exports', async ({ page }) => {
  const exported: string[] = []
  await mockApi(page, {
    'GET /api/reports/analytics/production': () => ({ status: 200, json: production }),
    'GET /api/reports/analytics/production/export': () => ({ status: 200, json: {} }),
  })
  page.on('request', (request) => {
    if (request.url().includes('/analytics/production')) exported.push(request.url())
  })

  await page.goto('/reports/production')
  const main = page.getByRole('main')
  await expect(page.getByRole('heading', { level: 1, name: 'Productie-analyse' })).toBeVisible()
  await expect(main.getByText('12,5 u').first()).toBeVisible()
  await expect(main.getByText('75%').first()).toBeVisible()
  await expect(main.getByRole('note')).toContainText('130 min tegenover 120 min')
  await expect(main.getByRole('link', { name: 'A1 Keuken' })).toBeVisible()
  await expect(main.getByText('Oorzaken van mislukkingen worden niet per job vastgelegd')).toBeVisible()

  await main.getByRole('button', { name: 'Toon als tabel' }).click()
  await expect(main.getByRole('table')).toContainText('3 nov')

  await main.getByRole('button', { name: 'Printers downloaden (CSV)' }).click()
  await expect.poll(() => exported.some(url => url.includes('part=printers') && url.includes('hoursPerDay=24'))).toBe(true)

  await page.getByLabel('Beschikbare uren per printer per dag').selectOption('12')
  await expect.poll(() => exported.some(url => !url.includes('/export') && url.includes('hoursPerDay=12'))).toBe(true)
})

test('sales analytics covers channels, customers, lead time, heatmap and drill-downs', async ({ page }) => {
  await mockApi(page, { 'GET /api/reports/analytics/sales': () => ({ status: 200, json: sales }) })

  await page.goto('/reports/sales')
  const main = page.getByRole('main')
  await expect(page.getByRole('heading', { level: 1, name: 'Verkoop-analyse' })).toBeVisible()
  await expect(main.getByText('Online bestellingen')).toBeVisible()
  await expect(main.getByText('Geen klant gekoppeld')).toBeVisible()
  await expect(main.getByText('Gemiddeld 4,5 dagen, mediaan 4,5, langste 6')).toBeVisible()
  await expect(main.getByRole('img', { name: 'Marktomzet per weekdag en uur' })).toBeVisible()
  await expect(main.getByRole('link', { name: 'Anna Peeters' })).toHaveAttribute('href', `/customers/${customerId}`)

  await main.getByRole('button', { name: 'Toon als tabel' }).last().click()
  await expect(main.getByRole('table').last()).toContainText('za')

  await main.getByRole('link', { name: 'Vaas' }).click()
  await expect(page).toHaveURL(new RegExp(`/reports/margin\\?.*productId=${productId}`))
})

test('inventory analytics shows stock value, price impact and dead stock', async ({ page }) => {
  await mockApi(page, { 'GET /api/reports/analytics/inventory': () => ({ status: 200, json: inventory }) })

  await page.goto('/reports/inventory')
  const main = page.getByRole('main')
  await expect(page.getByRole('heading', { level: 1, name: 'Voorraad-analyse' })).toBeVisible()
  await expect(main.getByText('Totale voorraadwaarde')).toBeVisible()
  await expect(main.getByRole('table')).toContainText('PLA Basic')
  await expect(main.getByRole('table')).toContainText('22,2%')
  await expect(main.getByRole('link', { name: 'PETG - Zwart' })).toHaveAttribute('href', '/inventory/s1')
  await expect(main.getByText('3 op voorraad, nooit verkocht')).toBeVisible()
})

test('profitability analytics shows the cost breakdown, drills into a product and keeps the filter in the URL', async ({ page }) => {
  const urls: string[] = []
  await mockApi(page, { 'GET /api/reports/analytics/profitability': () => ({ status: 200, json: profit }) })
  page.on('request', (request) => {
    if (request.url().includes('/analytics/profitability')) urls.push(request.url())
  })

  await page.goto('/reports/profitability')
  const main = page.getByRole('main')
  await expect(page.getByRole('heading', { level: 1, name: 'Winstgevendheid-analyse' })).toBeVisible()
  await expect(main.getByText('Niet uitgesplitst')).toBeVisible()
  await expect(main.getByText('60% van de kosten')).toBeVisible()
  await expect(main.getByText('Van omzet naar nettowinst')).toBeVisible()

  await page.getByRole('radio', { name: 'Markt-kassa' }).check()
  await expect.poll(() => urls.some(url => url.includes('channel=pos'))).toBe(true)

  await main.getByRole('link', { name: 'Vaas' }).click()
  await expect(page).toHaveURL(new RegExp(`/reports/profitability\\?.*productId=${productId}`))
  await expect(main.getByRole('link', { name: 'Alle producten' })).toBeVisible()
})

test('empty periods, failures and the hub links', async ({ page }) => {
  let calls = 0
  await mockApi(page, {
    'GET /api/reports/margin': () => ({ status: 200, json: { ...profit, current: { units: 0, revenue: 0, cost: 0, grossProfit: 0, marginPercent: null, costComplete: true, standCosts: 0, netProfit: 0, netMarginPercent: null }, previous: { units: 0, revenue: 0, cost: 0, grossProfit: 0, marginPercent: null, costComplete: true, standCosts: 0, netProfit: 0, netMarginPercent: null }, delta: { revenueChange: 0, revenueChangePercent: null, grossProfitChange: 0, grossProfitChangePercent: null, marginPointsChange: null }, products: [], trend: [] } }),
    'GET /api/reports/analytics/production': () => {
      calls++
      return calls <= 2 ? { status: 500 } : { status: 200, json: { ...production, current: productionTotals({ jobs: 0, done: 0, failed: 0 }) } }
    },
  })

  await page.goto('/reports')
  const main = page.getByRole('main')
  for (const name of ['Productie-analyse', 'Verkoop-analyse', 'Voorraad-analyse', 'Winstgevendheid-analyse']) {
    await expect(main.getByRole('link', { name: new RegExp(name) })).toBeVisible()
  }

  await main.getByRole('link', { name: /Productie-analyse/ }).click()
  await expect(page.getByRole('alert')).toBeVisible()
  await page.getByRole('button', { name: 'Opnieuw proberen' }).click()
  await expect(page.getByText('Geen afgeronde printjobs in deze periode')).toBeVisible()
})

for (const theme of ['dark', 'light'] as const) {
  test(`the analytics screens follow the ${theme} theme setting`, async ({ page }, testInfo) => {
    await page.addInitScript((value) => localStorage.setItem('printos_theme', value), theme)
    await mockApi(page, { 'GET /api/reports/analytics/profitability': () => ({ status: 200, json: profit }) })

    await page.goto('/reports/profitability')
    await expect(page.getByRole('heading', { level: 1, name: 'Winstgevendheid-analyse' })).toBeVisible()
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme)
    const background = await page.evaluate(() => getComputedStyle(document.body).backgroundColor)
    const [red = 0, green = 0, blue = 0] = background.match(/\d+/g)?.map(Number) ?? []
    const luminance = (red + green + blue) / 3
    if (theme === 'dark') expect(luminance).toBeLessThan(60)
    else expect(luminance).toBeGreaterThan(200)

    if (process.env.SHOTS) await page.screenshot({ path: `${process.env.SHOTS}/profit-${theme}-${testInfo.project.name}.png`, fullPage: true })
  })
}
