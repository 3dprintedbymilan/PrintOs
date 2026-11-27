import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

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
  lowStockThreshold: 3,
  isLow: true,
  averageUnitCost: 3.5,
  stockValue: 17.5,
  updatedAt: '2026-11-20T10:00:00Z',
}
const lamp = { ...vase, variantId: '55555555-5555-5555-5555-555555555555', productName: 'Lamp', variantName: 'Small', sku: null, onHand: 0, reserved: 0, available: 0, lowStockThreshold: null, isLow: false, averageUnitCost: null, stockValue: null }

const list = {
  summary: { skuCount: 2, skusInStock: 1, totalUnits: 5, totalReserved: 2, totalAvailable: 3, lowStockCount: 1, stockValue: 17.5 },
  items: [lamp, vase],
}

const movement = {
  id: 'm1',
  variantId: vase.variantId,
  kind: 'Produced',
  delta: 5,
  reservedDelta: 0,
  balanceAfter: 5,
  reservedAfter: 0,
  reason: 'Print job finished: Vases',
  unitCost: 3.5,
  printJobId: 'j1',
  printJobName: 'Vases',
  orderId: null,
  orderNumber: null,
  orderLineId: null,
  marketSaleId: null,
  createdAt: '2026-11-20T10:00:00Z',
}

test('the finished stock list shows on hand, reserved and available and filters low stock', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/finished-stock': () => ({ status: 200, json: list }),
    'GET /api/workspace/settings': () => ({ status: 200, json: { currency: 'EUR' } }),
  })

  await page.goto('/inventory/finished')

  const main = page.getByRole('main')
  await expect(page.getByRole('heading', { level: 1, name: 'Afgewerkte voorraad' })).toBeVisible()
  const vaseLink = main.getByRole('link', { name: /Vase · Large/ })
  await expect(vaseLink).toContainText('5 op voorraad')
  await expect(vaseLink).toContainText('2 gereserveerd')
  await expect(vaseLink).toContainText('3 beschikbaar')
  await expect(vaseLink).toContainText('Lage voorraad')
  await expect(main.getByRole('link', { name: /Lamp · Small/ })).toBeVisible()

  await main.getByLabel(/Alleen lage voorraad/).check()
  await expect(main.getByRole('link', { name: /Lamp · Small/ })).toHaveCount(0)
  await main.getByLabel(/Alleen lage voorraad/).uncheck()
  await main.getByLabel('Producten zoeken').fill('zzz')
  await expect(main.getByText('Geen producten gevonden voor je zoekopdracht.')).toBeVisible()
})

test('the inventory links to finished stock', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/spools': () => ({ status: 200, json: [] }),
    'GET /api/materials': () => ({ status: 200, json: [] }),
    'GET /api/finished-stock': () => ({ status: 200, json: list }),
    'GET /api/workspace/settings': () => ({ status: 200, json: { currency: 'EUR' } }),
  })

  await page.goto('/inventory')
  await page.getByRole('main').getByRole('link', { name: /Afgewerkte voorraad/ }).click()

  await expect(page).toHaveURL(/\/inventory\/finished$/)
})

test('adjusting finished stock needs a reason and updates the figures and history', async ({ page }) => {
  await seedSignedInSession(page)
  let item = vase
  let movements = [movement]
  let posted: unknown
  await mockApi(page, {
    [`GET /api/finished-stock/${vase.variantId}`]: () => ({ status: 200, json: { item, reservations: [{ orderId: 'o1', orderNumber: 12, orderLineId: 'l1', quantity: 2, status: 'Active', createdAt: '2026-11-21T10:00:00Z' }] } }),
    [`GET /api/finished-stock/${vase.variantId}/movements`]: () => ({ status: 200, json: movements }),
    'GET /api/workspace/settings': () => ({ status: 200, json: { currency: 'EUR' } }),
    [`POST /api/finished-stock/${vase.variantId}/adjustments`]: (body) => {
      posted = body
      item = { ...vase, onHand: 4, available: 2 }
      movements = [{ ...movement, id: 'm2', kind: 'Adjustment', delta: -1, balanceAfter: 4, reason: 'Broken' }, movement]
      return { status: 200, json: item }
    },
  })

  await page.goto(`/inventory/finished/${vase.variantId}`)
  const main = page.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Vase · Large' })).toBeVisible()
  await expect(main.getByText('Geproduceerd')).toBeVisible()
  await expect(main.getByRole('link', { name: /Bestelling 12/ })).toBeVisible()

  await main.getByRole('button', { name: 'Voorraad aanpassen' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('button', { name: 'Aanpassing opslaan' }).click()
  await expect(dialog.getByText('Geef een reden op.')).toBeVisible()
  expect(posted).toBeUndefined()

  await dialog.getByRole('radio', { name: 'Verwijderen' }).check({ force: true })
  await dialog.getByLabel('Stuks').fill('1')
  await dialog.getByLabel('Reden').fill('Broken')
  await dialog.getByRole('button', { name: 'Aanpassing opslaan' }).click()

  await expect(main.getByText('Voorraad aangepast.')).toBeVisible()
  expect(posted).toEqual({ delta: -1, reason: 'Broken', allowBelowReserved: false })
  await expect(main.getByText('Aanpassing', { exact: true })).toBeVisible()
  await expect(main.getByText('4 op voorraad').first()).toBeVisible()
})

test('the order detail shows reservations and offers reserving anyway when stock is short', async ({ page }) => {
  await seedSignedInSession(page)
  const orderId = '66666666-6666-6666-6666-666666666666'
  const order = {
    id: orderId,
    number: 12,
    customerId: null,
    customerName: null,
    status: 'Confirmed',
    paymentStatus: 'Unpaid',
    channel: 'direct',
    currency: 'EUR',
    orderedAt: '2026-11-20T10:00:00Z',
    dueDate: null,
    isOverdue: false,
    totalAmount: 40,
    amountPaid: 0,
    discountAmount: 0,
    subtotal: 40,
    totalCost: 7,
    costComplete: true,
    profit: 33,
    marginPercent: 82.5,
    amountDue: 40,
    notes: null,
    confirmedAt: null,
    deliveredAt: null,
    cancelledAt: null,
    createdAt: '2026-11-20T10:00:00Z',
    linesEditable: true,
    headerEditable: true,
    paymentsEditable: true,
    hasRightsWarning: false,
    allowedTransitions: ['Ready', 'Cancelled'],
    lines: [{ id: 'l1', variantId: vase.variantId, description: 'Vase - Large', sku: null, quantity: 4, listUnitPrice: 10, unitPrice: 10, discountPercent: 0, lineTotal: 40, unitCost: 3.5, commercialRightsStatus: 'Cleared', commercialRightsBlockers: [], riskAcknowledged: false }],
    payments: [],
  }
  const reservationLine = { orderLineId: 'l1', variantId: vase.variantId, description: 'Vase - Large', quantity: 4, reserved: 0, fulfilledQuantity: 0, status: 'None', onHand: 5, available: 3 }
  const posts: unknown[] = []
  await mockApi(page, {
    [`GET /api/orders/${orderId}`]: () => ({ status: 200, json: order }),
    [`GET /api/orders/${orderId}/reservations`]: () => ({ status: 200, json: { orderId, canReserve: true, lines: [reservationLine] } }),
    [`POST /api/orders/${orderId}/reservations`]: (body) => {
      posts.push(body)
      return (body as { allowOverReserve: boolean }).allowOverReserve
        ? { status: 200, json: { orderId, canReserve: true, lines: [{ ...reservationLine, reserved: 4, status: 'Active', available: -1 }] } }
        : { status: 400, json: { title: 'Validation', errors: { 'lines[0]': ['Vase - Large: Only 3 available (2 reserved).'] } } }
    },
  })

  await page.goto(`/orders/${orderId}`)
  const main = page.getByRole('main')
  await expect(main.getByText('3 beschikbaar, 5 op voorraad')).toBeVisible()
  await main.getByRole('button', { name: 'Reserveren uit voorraad' }).click()
  await expect(main.getByRole('alert')).toContainText('Slechts 3 beschikbaar (2 gereserveerd).')

  await main.getByRole('button', { name: 'Toch reserveren' }).click()
  await expect(main.getByText('Gereserveerd 4/4')).toBeVisible()
  await expect(main.getByRole('button', { name: 'Reservatie vrijgeven' })).toBeVisible()
  expect(posts).toEqual([{ allowOverReserve: false }, { allowOverReserve: true }])
})
