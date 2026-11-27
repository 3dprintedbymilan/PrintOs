import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const item = (rank: number, label: string, units: number, revenue: number, profit: number, shares: [number, number, number]) => ({
  rank,
  key: label,
  productId: `p${rank}`,
  variantId: null,
  label,
  sku: null,
  units,
  revenue,
  profit,
  marginPercent: 50,
  unitsSharePercent: shares[0],
  revenueSharePercent: shares[1],
  profitSharePercent: shares[2],
})

const alpha = item(1, 'Vaas', 8, 80, 50, [80, 44.4, 40])
const beta = item(2, 'Lamp', 2, 100, 75, [20, 55.6, 60])

const report = {
  period: { kind: 'month', from: '2026-11-01', to: '2026-11-30', previousFrom: '2026-10-01', previousTo: '2026-10-31', days: 30 },
  currency: 'EUR',
  channel: 'all',
  level: 'product',
  totals: { units: 10, revenue: 180, profit: 125, itemCount: 2 },
  byUnits: [alpha, beta],
  byRevenue: [{ ...beta, rank: 1 }, { ...alpha, rank: 2 }],
  byProfit: [{ ...beta, rank: 1 }, { ...alpha, rank: 2 }],
}

const slow = {
  days: 30,
  currency: 'EUR',
  count: 2,
  stockValue: 30,
  items: [
    { productId: 'p1', variantId: 'v1', label: 'Oude vaas - Groot', sku: null, onHand: 3, stockValue: 20, lastSoldAt: null, daysSinceLastSale: null, stockSince: '2026-09-01T10:00:00Z' },
    { productId: 'p2', variantId: 'v2', label: 'Lamp - Klein', sku: null, onHand: 1, stockValue: 10, lastSoldAt: '2026-10-01T10:00:00Z', daysSinceLastSale: 57, stockSince: null },
  ],
}

test.beforeEach(async ({ page }) => {
  await seedSignedInSession(page)
})

test('bestsellers rank by units, revenue and profit with shares, and list slow movers', async ({ page }) => {
  await mockApi(page, {
    'GET /api/reports/bestsellers': () => ({ status: 200, json: report }),
    'GET /api/reports/slow-movers': () => ({ status: 200, json: slow }),
  })

  await page.goto('/reports/bestsellers')

  const main = page.getByRole('main')
  await expect(page.getByRole('heading', { level: 1, name: 'Bestsellers' })).toBeVisible()
  await expect(main.getByRole('listitem').filter({ hasText: 'Vaas' }).first()).toContainText('80% van het totaal')
  await expect(main.getByRole('listitem').first()).toContainText('Vaas')

  await page.getByRole('radio', { name: 'Omzet' }).check()
  await expect(main.getByRole('listitem').first()).toContainText('Lamp')
  await expect(main.getByRole('listitem').first()).toContainText('55,6% van het totaal')

  await expect(main.getByText('Oude vaas - Groot')).toBeVisible()
  await expect(main.getByText('nooit verkocht')).toBeVisible()
  await expect(main.getByText('57 dagen geleden verkocht')).toBeVisible()
  await expect(main.getByText('2 producten leggen')).toBeVisible()
})

test('changing the period and level reloads, empty and error states show', async ({ page }) => {
  const urls: string[] = []
  let calls = 0
  await mockApi(page, {
    'GET /api/reports/bestsellers': () => {
      calls++
      return calls <= 2 ? { status: 500 } : { status: 200, json: { ...report, byUnits: [], byRevenue: [], byProfit: [] } }
    },
    'GET /api/reports/slow-movers': () => ({ status: 200, json: { ...slow, count: 0, items: [] } }),
  })
  page.on('request', (request) => {
    if (request.url().includes('/api/reports/bestsellers')) urls.push(request.url())
  })

  await page.goto('/reports/bestsellers')
  await expect(page.getByRole('alert')).toBeVisible()
  await page.getByRole('button', { name: 'Opnieuw proberen' }).click()
  await expect(page.getByText('In deze periode is nog niets verkocht.')).toBeVisible()
  await expect(page.getByText('Er ligt geen voorraad die 30 dagen')).toBeVisible()

  await page.getByLabel('Periode').selectOption('year')
  await page.getByRole('radio', { name: 'Variant' }).check()
  await expect.poll(() => urls.some(url => url.includes('period=year') && url.includes('level=variant'))).toBe(true)
})
