import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const marketId = '77777777-7777-7777-7777-777777777777'
const vaseId = '44444444-4444-4444-4444-444444444444'
const lampId = '55555555-5555-5555-5555-555555555555'

const market = {
  id: marketId,
  name: 'Kerstmarkt Gent',
  location: 'Sint-Veerleplein',
  startDate: '2026-12-05',
  endDate: '2026-12-06',
  standCost: 45,
  notes: null,
  status: 'Open',
  closedAt: null,
  currency: 'EUR',
  createdAt: '2026-11-20T10:00:00Z',
  saleCount: 1,
  voidedCount: 0,
  revenue: 20,
  cost: 3.5,
  canDelete: false,
}

const catalogue = [
  { variantId: vaseId, productId: 'a1', productName: 'Vaas', variantName: 'Groot', sku: 'VAAS-G', price: 20, available: 3, commercialRightsStatus: 'Cleared' },
  { variantId: lampId, productId: 'a2', productName: 'Lamp', variantName: 'Klein', sku: null, price: 12.5, available: 1, commercialRightsStatus: 'NotCleared' },
]

function sale(overrides: Record<string, unknown> = {}) {
  return {
    id: 'sale-1',
    marketId,
    receiptNumber: 1,
    status: 'Completed',
    soldAt: '2026-12-05T10:15:00Z',
    businessDate: '2026-12-05',
    customerId: null,
    customerName: null,
    paymentMethod: 'cash',
    currency: 'EUR',
    subtotal: 20,
    discountAmount: 0,
    totalAmount: 20,
    totalCost: 3.5,
    costComplete: true,
    profit: 16.5,
    amountTendered: 20,
    changeGiven: 0,
    notes: null,
    voidedAt: null,
    voidReason: null,
    replayed: false,
    lines: [{ id: 'l1', variantId: vaseId, description: 'Vaas - Groot', sku: null, quantity: 1, listUnitPrice: 20, unitPrice: 20, discountPercent: 0, lineTotal: 20, unitCost: 3.5, riskAcknowledged: false }],
    ...overrides,
  }
}

const summary = {
  marketId,
  date: null,
  currency: 'EUR',
  saleCount: 1,
  voidedCount: 0,
  voidedAmount: 0,
  itemCount: 1,
  subtotal: 20,
  discounts: 0,
  revenue: 20,
  cost: 3.5,
  costComplete: true,
  grossProfit: 16.5,
  standCost: 45,
  byPayment: [{ method: 'cash', saleCount: 1, amount: 20 }, { method: 'card', saleCount: 0, amount: 0 }, { method: 'other', saleCount: 0, amount: 0 }],
  items: [{ variantId: vaseId, description: 'Vaas - Groot', quantity: 1, revenue: 20, cost: 3.5 }],
  days: [{ date: '2026-12-05', saleCount: 1, revenue: 20 }],
}

test.beforeEach(async ({ page }) => {
  await seedSignedInSession(page)
})

test('the market list shows markets with totals and a register shortcut', async ({ page }) => {
  await mockApi(page, { 'GET /api/markets': () => ({ status: 200, json: [market, { ...market, id: 'closed-1', name: 'Zomerfeest', status: 'Closed' }] }) })

  await page.goto('/markets')

  const main = page.getByRole('main')
  await expect(page.getByRole('heading', { level: 1, name: 'Markten' })).toBeVisible()
  await expect(main.getByRole('link', { name: /Kerstmarkt Gent/ })).toContainText('1 verkoop')
  await expect(main.getByRole('link', { name: /Kerstmarkt Gent/ })).toContainText('€')
  await expect(main.getByRole('link', { name: 'Kassa openen' })).toHaveCount(1)
  await expect(main.getByRole('link', { name: /Zomerfeest/ })).toBeVisible()
})

test('the Business page links to markets and a new market is validated and created', async ({ page }) => {
  let body: Record<string, unknown> | null = null
  await mockApi(page, {
    'GET /api/workspace/settings': () => ({ status: 200, json: { currency: 'EUR' } }),
    'GET /api/markets': () => ({ status: 200, json: [] }),
    'POST /api/markets': (posted) => {
      body = posted as Record<string, unknown>
      return { status: 201, json: { ...market, ...body } }
    },
    [`GET /api/markets/${marketId}`]: () => ({ status: 200, json: market }),
    [`GET /api/markets/${marketId}/summary`]: () => ({ status: 200, json: summary }),
    [`GET /api/markets/${marketId}/sales`]: () => ({ status: 200, json: [sale()] }),
  })

  await page.goto('/business')
  await page.getByRole('main').getByRole('link', { name: /Markten/ }).click()
  await expect(page.getByRole('main').getByText('Nog geen markten')).toBeVisible()

  await page.getByRole('main').getByRole('button', { name: 'Markt toevoegen' }).click()
  await page.getByRole('button', { name: 'Markt opslaan' }).click()
  await expect(page.getByText('Dit veld is verplicht.')).toBeVisible()
  await expect(page.getByLabel('Naam')).toBeFocused()

  await page.getByLabel('Naam').fill('Kerstmarkt Gent')
  await page.getByLabel(/Standkosten/).fill('45,50')
  await page.getByRole('button', { name: 'Markt opslaan' }).click()

  await expect(page).toHaveURL(new RegExp(`/markets/${marketId}$`))
  expect(body).toMatchObject({ name: 'Kerstmarkt Gent', standCost: 45.5, endDate: null })
  await expect(page.getByRole('main').getByRole('link', { name: 'Kassa openen' })).toBeVisible()
})

test('selling at the register: grid, cart, discount, cash change and receipt', async ({ page }) => {
  let posted: Record<string, unknown> | null = null
  await mockApi(page, {
    [`GET /api/markets/${marketId}`]: () => ({ status: 200, json: market }),
    'GET /api/pos/catalogue': () => ({ status: 200, json: catalogue }),
    [`POST /api/markets/${marketId}/sales`]: (body) => {
      posted = body as Record<string, unknown>
      return { status: 201, json: sale({ receiptNumber: 7, totalAmount: 37.5, subtotal: 40, discountAmount: 2.5, amountTendered: 50, changeGiven: 12.5 }) }
    },
  })

  await page.goto(`/markets/${marketId}/pos`)

  const main = page.getByRole('main')
  await expect(page.getByRole('heading', { level: 1, name: 'Kassa' })).toBeVisible()
  const vase = main.getByRole('button', { name: /Vaas Groot/ })
  await expect(vase).toContainText('3 over')
  await expect(main.getByRole('button', { name: /Lamp Klein/ })).toContainText('Rechten niet bevestigd')

  await vase.click()
  await vase.click()
  await expect(vase).toContainText('1 over')
  await main.getByRole('button', { name: /2 artikelen/ }).click()

  await expect(main.getByRole('heading', { name: 'Winkelmandje' })).toBeVisible()
  await main.getByLabel('Korting', { exact: true }).fill('2,50')
  await expect(main.locator('.grand')).toContainText('37,50')
  await main.getByRole('button', { name: 'Betalen' }).click()

  await expect(main.getByRole('heading', { name: 'Betaling' })).toBeVisible()
  await main.getByLabel('Ontvangen').fill('50')
  await expect(main.getByRole('status')).toContainText('12,50')
  await main.getByRole('button', { name: 'Verkoop afronden' }).click()

  await expect(main.getByRole('status')).toContainText('Verkoop voltooid')
  await expect(main.getByRole('status')).toContainText('Bon #7')
  await expect(main.getByRole('status')).toContainText('12,50')
  expect(posted).toMatchObject({
    paymentMethod: 'cash',
    amountTendered: 50,
    discountAmount: 2.5,
    lines: [{ variantId: vaseId, quantity: 2 }],
    acknowledgeRisk: false,
  })
  expect(String(posted!.idempotencyKey).length).toBeGreaterThanOrEqual(8)

  await main.getByRole('button', { name: 'Nieuwe verkoop' }).click()
  await expect(main.getByRole('button', { name: /Vaas Groot/ })).toBeVisible()
})

test('selling a product without verified rights needs an explicit acknowledgement', async ({ page }) => {
  let posted: Record<string, unknown> | null = null
  await mockApi(page, {
    [`GET /api/markets/${marketId}`]: () => ({ status: 200, json: market }),
    'GET /api/pos/catalogue': () => ({ status: 200, json: catalogue }),
    [`POST /api/markets/${marketId}/sales`]: (body) => {
      posted = body as Record<string, unknown>
      return { status: 201, json: sale() }
    },
  })

  await page.goto(`/markets/${marketId}/pos`)
  const main = page.getByRole('main')
  await main.getByRole('button', { name: /Lamp Klein/ }).click()
  await main.getByRole('button', { name: /1 artikel/ }).click()
  await main.getByRole('button', { name: 'Betalen' }).click()
  await main.getByRole('radio', { name: 'Kaart' }).check({ force: true })

  const confirm = main.getByRole('button', { name: 'Verkoop afronden' })
  await expect(confirm).toBeDisabled()
  await main.getByRole('checkbox').check()
  await confirm.click()

  await expect(main.getByRole('status')).toContainText('Verkoop voltooid')
  expect(posted).toMatchObject({ paymentMethod: 'card', amountTendered: null, acknowledgeRisk: true })
})

test('a failed request is kept, shown as pending and retried with the same idempotency key', async ({ page }) => {
  const keys: string[] = []
  let attempts = 0
  await mockApi(page, {
    [`GET /api/markets/${marketId}`]: () => ({ status: 200, json: market }),
    'GET /api/pos/catalogue': () => ({ status: 200, json: catalogue }),
    [`POST /api/markets/${marketId}/sales`]: (body) => {
      keys.push((body as { idempotencyKey: string }).idempotencyKey)
      attempts++
      return attempts === 1 ? { status: 503 } : { status: attempts === 2 ? 200 : 201, json: sale({ replayed: true }) }
    },
  })

  await page.goto(`/markets/${marketId}/pos`)
  const main = page.getByRole('main')
  await main.getByRole('button', { name: /Vaas Groot/ }).click()
  await main.getByRole('button', { name: /1 artikel/ }).click()
  await main.getByRole('button', { name: 'Betalen' }).click()
  await main.getByRole('button', { name: 'Verkoop afronden' }).click()

  await expect(main.getByRole('status')).toContainText('Opgeslagen op dit toestel')
  await main.getByRole('button', { name: 'Nieuwe verkoop' }).click()
  const pending = main.getByRole('button', { name: 'Wachtende verkopen nu versturen' })
  await expect(pending).toContainText('1 verkoop wacht')
  await expect(main.getByRole('button', { name: /Vaas Groot/ })).toContainText('2 over')

  await pending.click()

  await expect(pending).toBeHidden()
  expect(keys).toHaveLength(2)
  expect(keys[0]).toBe(keys[1])
  expect(await page.evaluate(() => JSON.parse(localStorage.getItem('printos_pos_queue') ?? '[]'))).toEqual([])
})

test('a refused sale shows the server message and keeps the cart', async ({ page }) => {
  await mockApi(page, {
    [`GET /api/markets/${marketId}`]: () => ({ status: 200, json: market }),
    'GET /api/pos/catalogue': () => ({ status: 200, json: catalogue }),
    [`POST /api/markets/${marketId}/sales`]: () => ({ status: 400, json: { title: 'Validation', errors: { 'lines[0].quantity': ['Only 1 available (0 reserved).'] } } }),
  })

  await page.goto(`/markets/${marketId}/pos`)
  const main = page.getByRole('main')
  await main.getByRole('button', { name: /Vaas Groot/ }).click()
  await main.getByRole('button', { name: /1 artikel/ }).click()
  await main.getByRole('button', { name: 'Betalen' }).click()
  await main.getByRole('button', { name: 'Verkoop afronden' }).click()

  await expect(main.getByRole('alert')).toContainText('Slechts 1 beschikbaar')
  await expect(main.getByRole('button', { name: 'Verkoop afronden' })).toBeVisible()
})

test('the market overview shows totals and a sale can be voided with a reason', async ({ page }) => {
  let voidBody: Record<string, unknown> | null = null
  let voided = false
  await mockApi(page, {
    [`GET /api/markets/${marketId}`]: () => ({ status: 200, json: market }),
    [`GET /api/markets/${marketId}/summary`]: () => ({ status: 200, json: summary }),
    [`GET /api/markets/${marketId}/sales`]: () => ({ status: 200, json: [sale(voided ? { status: 'Voided', voidReason: 'Klant twijfelde' } : {})] }),
    'POST /api/market-sales/sale-1/void': (body) => {
      voidBody = body as Record<string, unknown>
      voided = true
      return { status: 200, json: sale({ status: 'Voided' }) }
    },
  })

  await page.goto(`/markets/${marketId}`)

  const main = page.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Kerstmarkt Gent' })).toBeVisible()
  await expect(main.getByText('Omzet')).toBeVisible()
  await expect(main.getByText('Per betaalmethode')).toBeVisible()
  await expect(main.getByText('Standkosten: €').first()).toBeVisible()

  await main.getByText('#1').click()
  await main.getByRole('button', { name: 'Verkoop annuleren' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('button', { name: 'Verkoop annuleren' }).click()
  await expect(dialog.getByText('Vul een reden in.')).toBeVisible()
  await dialog.getByLabel('Reden').fill('Klant twijfelde')
  await dialog.getByRole('button', { name: 'Verkoop annuleren' }).click()

  await expect(main.getByText('Geannuleerd').first()).toBeVisible()
  expect(voidBody).toEqual({ reason: 'Klant twijfelde' })
})

test('the register fits a phone without horizontal scrolling', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await mockApi(page, {
    [`GET /api/markets/${marketId}`]: () => ({ status: 200, json: market }),
    'GET /api/pos/catalogue': () => ({ status: 200, json: catalogue }),
  })

  await page.goto(`/markets/${marketId}/pos`)
  const main = page.getByRole('main')
  await main.getByRole('button', { name: /Vaas Groot/ }).click()

  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  const tile = await main.getByRole('button', { name: /Vaas Groot/ }).boundingBox()
  expect(tile!.height).toBeGreaterThanOrEqual(44)
  await expect(main.getByRole('button', { name: /1 artikel/ })).toBeVisible()
})
