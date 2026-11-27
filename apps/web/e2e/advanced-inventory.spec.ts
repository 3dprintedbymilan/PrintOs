import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const spool = {
  id: '22222222-2222-2222-2222-222222222222',
  materialId: 'm1',
  materialName: 'PLA Basic',
  supplierId: null,
  supplierName: 'Bambu Lab',
  colorName: 'Jade White',
  colorHex: '#F4F4F0',
  cost: 20,
  costPerGram: 0.02,
  initialWeightGrams: 1000,
  remainingWeightGrams: 1000,
  isArchived: false,
  emptySpoolGrams: null as number | null,
}
const vase = {
  variantId: '44444444-4444-4444-4444-444444444444',
  productId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  productName: 'Vase',
  variantName: 'Large',
  sku: 'VASE-L',
  productArchived: false,
  onHand: 5,
  reserved: 2,
  available: 3,
  lowStockThreshold: null,
  isLow: false,
  averageUnitCost: 3.5,
  stockValue: 17.5,
  updatedAt: '2026-11-20T10:00:00Z',
}
const finishedList = {
  summary: { skuCount: 1, skusInStock: 1, totalUnits: 5, totalReserved: 2, totalAvailable: 3, lowStockCount: 0, stockValue: 17.5 },
  items: [vase],
}

test('weighing a spool corrects the remaining grams and remembers nothing without valid input', async ({ page }) => {
  await seedSignedInSession(page)
  let current = spool
  let posted: unknown
  await mockApi(page, {
    [`GET /api/spools/${spool.id}`]: () => ({ status: 200, json: current }),
    [`GET /api/spools/${spool.id}/movements`]: () => ({ status: 200, json: [] }),
    [`POST /api/spools/${spool.id}/weigh`]: (body) => {
      posted = body
      current = { ...spool, remainingWeightGrams: 700, emptySpoolGrams: 250 }
      return { status: 200, json: current }
    },
  })

  await page.goto(`/inventory/${spool.id}`)
  const main = page.getByRole('main')
  await main.getByRole('button', { name: 'Gewicht opslaan' }).click()
  await expect(main.getByText('Vul een geheel aantal gram in tussen 1 en 100000.')).toBeVisible()
  expect(posted).toBeUndefined()

  await main.getByLabel('Gewicht op de weegschaal (gram)').fill('950')
  await main.getByLabel('Gewicht lege spoel (gram)').fill('250')
  await expect(main.getByText('Restant na wegen: 700 g (-300 g)')).toBeVisible()
  await main.getByRole('button', { name: 'Gewicht opslaan' }).click()

  await expect(main.getByText('Gewogen: 700 g over.')).toBeVisible()
  expect(posted).toEqual({ grossWeightGrams: 950, emptySpoolGrams: 250 })
  await expect(main.getByText('Onthouden: 250 g.', { exact: false })).toBeVisible()
})

test('the stock take books only the changed filament rows', async ({ page }) => {
  await seedSignedInSession(page)
  let posted: { reason: string, lines: unknown[] } | undefined
  await mockApi(page, {
    'GET /api/spools': () => ({ status: 200, json: [spool, { ...spool, id: 'x', colorName: 'Black', colorHex: '#111111', remainingWeightGrams: 250 }] }),
    'GET /api/finished-stock': () => ({ status: 200, json: finishedList }),
    'POST /api/spools/bulk-adjustments': (body) => {
      posted = body as typeof posted
      return { status: 200, json: { changed: 1, unchanged: 0, results: [] } }
    },
  })

  await page.goto('/inventory/count')
  const main = page.getByRole('main')
  await expect(main.getByText('1.000 g op voorraad')).toBeVisible()

  await main.getByLabel('Geteld aantal voor PLA Basic · Jade White').fill('abc')
  await main.getByRole('button', { name: /correcties boeken/ }).click()
  await expect(main.getByText('Vul een geheel getal in, 0 of meer.')).toBeVisible()
  expect(posted).toBeUndefined()

  await main.getByLabel('Geteld aantal voor PLA Basic · Jade White').fill('800')
  await expect(main.getByText('-200 g')).toBeVisible()
  await main.getByRole('button', { name: '1 correcties boeken' }).click()

  await expect(main.getByText('1 correcties geboekt.')).toBeVisible()
  expect(posted).toEqual({ reason: 'Voorraadtelling', lines: [{ spoolId: spool.id, remainingGrams: 800 }] })
})

test('the stock take for finished goods sends counted quantities and shows server errors per row', async ({ page }) => {
  await seedSignedInSession(page)
  let calls = 0
  let posted: unknown
  await mockApi(page, {
    'GET /api/spools': () => ({ status: 200, json: [] }),
    'GET /api/finished-stock': () => ({ status: 200, json: finishedList }),
    'POST /api/finished-stock/bulk-adjustments': (body) => {
      posted = body
      calls++
      return calls === 1
        ? { status: 400, json: { errors: { 'lines[0].counted': ['Only 5 available (2 reserved).'] } } }
        : { status: 200, json: { changed: 1, unchanged: 0, results: [] } }
    },
  })

  await page.goto('/inventory/count')
  const main = page.getByRole('main')
  await main.getByRole('radio', { name: 'Afgewerkte producten' }).check({ force: true })
  await main.getByLabel('Geteld aantal voor Vase · Large').fill('1')
  await main.getByRole('button', { name: '1 correcties boeken' }).click()
  await expect(main.getByText(/Slechts 5 beschikbaar/)).toBeVisible()

  await main.getByLabel('Voorraad onder het gereserveerde aantal toestaan').check()
  await main.getByRole('button', { name: '1 correcties boeken' }).click()
  await expect(main.getByText('1 correcties geboekt.')).toBeVisible()
  expect(posted).toEqual({ reason: 'Voorraadtelling', allowBelowReserved: true, lines: [{ variantId: vase.variantId, counted: 1 }] })
})

test('the reservation overview lists reserved stock per order and filters shortages', async ({ page }) => {
  await seedSignedInSession(page)
  const line = {
    orderId: '66666666-6666-6666-6666-666666666666',
    orderNumber: 12,
    orderStatus: 'Confirmed',
    customerName: 'Anna Peeters',
    dueDate: '2026-12-01',
    orderLineId: 'l1',
    variantId: vase.variantId,
    productName: 'Vase',
    variantName: 'Large',
    sku: 'VASE-L',
    quantity: 2,
    onHand: 5,
    reserved: 2,
    available: 3,
    isShort: false,
  }
  const short = { ...line, orderId: 'o2', orderNumber: 13, orderLineId: 'l2', customerName: null, productName: 'Lamp', onHand: 1, reserved: 3, available: -2, isShort: true }
  await mockApi(page, {
    'GET /api/finished-stock/reservations': () => ({
      status: 200,
      json: { summary: { lines: 2, orders: 2, reservedUnits: 4, shortLines: 1, shortUnits: 2 }, items: [line, short] },
    }),
  })

  await page.goto('/inventory/reservations')
  const main = page.getByRole('main')
  await expect(page.getByRole('heading', { level: 1, name: 'Reserveringen' })).toBeVisible()
  const first = main.getByRole('link', { name: /Vase · Large/ })
  await expect(first).toContainText('Bestelling 12')
  await expect(first).toContainText('Anna Peeters')
  await expect(first).toContainText('3 beschikbaar')
  await expect(main.getByRole('link', { name: /Lamp/ })).toContainText('Tekort van 2')

  await main.getByLabel(/Alleen tekorten/).check()
  await expect(first).toHaveCount(0)
  await main.getByLabel(/Alleen tekorten/).uncheck()
  await main.getByLabel('Zoek bestelling, klant of product').fill('zzz')
  await expect(main.getByText('Geen reserveringen gevonden.')).toBeVisible()
})

test('the inventory links to the stock take and the reservations', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/spools': () => ({ status: 200, json: [] }),
    'GET /api/materials': () => ({ status: 200, json: [] }),
    'GET /api/inventory/alerts': () => ({ status: 200, json: [] }),
  })

  await page.goto('/inventory')
  const main = page.getByRole('main')
  await expect(main.getByRole('link', { name: /Voorraad tellen/ })).toHaveAttribute('href', '/inventory/count')
  await expect(main.getByRole('link', { name: /Reserveringen/ })).toHaveAttribute('href', '/inventory/reservations')
})
