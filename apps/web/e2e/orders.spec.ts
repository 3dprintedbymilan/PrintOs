import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const customer = {
  id: '33333333-3333-3333-3333-333333333333',
  name: 'Jan Peeters',
  email: 'jan@example.com',
  phone: null,
  address: null,
  vatNumber: null,
  notes: null,
  isArchived: false,
  createdAt: '2026-11-01T10:00:00Z',
}

const line = {
  id: '55555555-5555-5555-5555-555555555555',
  variantId: '44444444-4444-4444-4444-444444444444',
  description: 'Vase - Large',
  sku: null,
  quantity: 2,
  listUnitPrice: 20,
  unitPrice: 20,
  discountPercent: 0,
  lineTotal: 40,
  unitCost: 3.5,
  commercialRightsStatus: 'Cleared',
  commercialRightsBlockers: [],
  riskAcknowledged: false,
}

const order = {
  id: '66666666-6666-6666-6666-666666666666',
  number: 12,
  customerId: customer.id,
  customerName: customer.name,
  status: 'Draft',
  paymentStatus: 'Unpaid',
  channel: 'direct',
  currency: 'EUR',
  orderedAt: '2026-11-20T10:00:00Z',
  dueDate: '2027-01-05',
  isOverdue: false,
  totalAmount: 40,
  amountPaid: 0,
  lineCount: 1,
  itemCount: 2,
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
  allowedTransitions: ['Confirmed', 'Cancelled'],
  lines: [line],
  payments: [],
}

const productSummary = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  name: 'Vase',
  description: null,
  sku: null,
  isArchived: false,
  currency: 'EUR',
  variantCount: 1,
  lowestPrice: 20,
  highestPrice: 20,
  createdAt: '2026-11-01T10:00:00Z',
}

function productDetail(rights: 'Cleared' | 'NotCleared') {
  return {
    ...productSummary,
    variants: [{
      id: line.variantId,
      productId: productSummary.id,
      name: 'Large',
      sku: null,
      price: 20,
      targetMarginPercent: 40,
      feePercent: 0,
      bomLines: [],
      cost: {},
      commercialRights: {
        status: rights,
        isCleared: rights === 'Cleared',
        expiringSoon: false,
        blockingModelCount: rights === 'Cleared' ? 0 : 1,
        models: rights === 'Cleared' ? [] : [{ modelId: 'm1', modelName: 'Dragon', licenseName: null, licenseStatus: 'NoLicense', allowsCommercialSale: false, attributionRequired: false, daysUntilExpiry: null }],
        attributions: [],
      },
    }],
  }
}

const lookups = (rights: 'Cleared' | 'NotCleared' = 'Cleared') => ({
  'GET /api/customers': () => ({ status: 200, json: [customer] }),
  'GET /api/workspace/settings': () => ({ status: 200, json: { currency: 'EUR', language: 'nl' } }),
  'GET /api/products': () => ({ status: 200, json: [productSummary] }),
  [`GET /api/products/${productSummary.id}`]: () => ({ status: 200, json: productDetail(rights) }),
})

test('the business tab links to the order list with status and payment chips', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { 'GET /api/orders': () => ({ status: 200, json: [order, { ...order, id: 'x2', number: 13, status: 'InProduction', paymentStatus: 'Partial', isOverdue: true, customerId: null, customerName: null }] }) })

  await page.goto('/business')
  await page.getByRole('main').getByRole('link', { name: /Bestellingen/ }).click()

  await expect(page).toHaveURL(/\/orders$/)
  const first = page.getByRole('link', { name: /#12/ })
  await expect(first).toContainText('Jan Peeters')
  await expect(first).toContainText('Concept')
  await expect(first).toContainText('Onbetaald')
  await expect(first).toContainText('2 stuks')
  const second = page.getByRole('link', { name: /#13/ })
  await expect(second).toContainText('In productie')
  await expect(second).toContainText('Te laat')
  await expect(second).toContainText('Zonder klant')
})

test('the filters ask the API for the matching orders', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { 'GET /api/orders': () => ({ status: 200, json: [] }) })

  await page.goto('/orders')
  await expect(page.getByText('Geen bestellingen gevonden.')).toBeVisible()
  const request = page.waitForRequest(candidate => candidate.url().includes('status=InProduction'))
  await page.getByRole('button', { name: 'In productie' }).click()
  await request
  await expect(page.getByRole('button', { name: 'In productie' })).toHaveAttribute('aria-pressed', 'true')
})

test('a new order is built from a product, a free-text line and a quick-added customer', async ({ page }) => {
  await seedSignedInSession(page)
  let created: Record<string, unknown> | undefined
  let customerBody: unknown
  await mockApi(page, {
    ...lookups(),
    'POST /api/customers': (body) => {
      customerBody = body
      return { status: 201, json: { ...customer, id: '77777777-7777-7777-7777-777777777777', name: 'Fatima' } }
    },
    'POST /api/orders': (body) => {
      created = body as Record<string, unknown>
      return { status: 201, json: order }
    },
    [`GET /api/orders/${order.id}`]: () => ({ status: 200, json: order }),
  })

  await page.goto('/orders/new')

  await page.getByRole('button', { name: 'Nieuwe klant toevoegen' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Naam', { exact: true }).fill('Fatima')
  await dialog.getByRole('button', { name: 'Klant opslaan' }).click()
  await expect(page.getByLabel('Klant', { exact: true })).toHaveValue('77777777-7777-7777-7777-777777777777')
  expect(customerBody).toMatchObject({ name: 'Fatima' })

  await page.getByRole('button', { name: 'Regel toevoegen' }).click()
  await page.getByLabel('Product', { exact: true }).selectOption({ label: 'Vase - Large' })
  await expect(page.getByLabel('Prijs per stuk')).toHaveValue('20')
  await page.getByLabel('Aantal').fill('3')
  await page.getByLabel('Prijs per stuk').fill('18,50')

  await page.getByRole('button', { name: 'Regel toevoegen' }).click()
  await page.getByLabel('Product', { exact: true }).last().selectOption({ label: 'Vrije regel (zonder product)' })
  await page.getByRole('button', { name: 'Bestelling opslaan' }).click()
  await expect(page.getByText('Vul Omschrijving in.')).toBeVisible()
  await page.getByLabel('Omschrijving').fill('Naamplaatje')
  await page.getByLabel('Prijs per stuk').last().fill('12,50')
  await expect(page.getByRole('status')).toContainText('68')

  await page.getByRole('button', { name: 'Bestelling opslaan' }).click()
  await expect(page).toHaveURL(new RegExp(`/orders/${order.id}$`))
  expect(created).toMatchObject({
    customerId: '77777777-7777-7777-7777-777777777777',
    channel: 'direct',
    acknowledgeRisk: false,
    lines: [
      { variantId: line.variantId, quantity: 3, unitPrice: 18.5, description: null },
      { variantId: null, description: 'Naamplaatje', quantity: 1, unitPrice: 12.5 },
    ],
  })
})

test('a line with uncleared commercial rights needs an explicit acknowledgement', async ({ page }) => {
  await seedSignedInSession(page)
  let created: Record<string, unknown> | undefined
  await mockApi(page, {
    ...lookups('NotCleared'),
    'POST /api/orders': (body) => {
      created = body as Record<string, unknown>
      return { status: 201, json: order }
    },
    [`GET /api/orders/${order.id}`]: () => ({ status: 200, json: order }),
  })

  await page.goto('/orders/new')
  await page.getByRole('button', { name: 'Regel toevoegen' }).click()
  await page.getByLabel('Product', { exact: true }).selectOption({ label: 'Vase - Large' })
  await expect(page.getByRole('alert')).toContainText('Dragon')

  await page.getByRole('button', { name: 'Bestelling opslaan' }).click()
  await expect(page.getByText('Bevestig dat je het risico aanvaardt om door te gaan.')).toBeVisible()
  expect(created).toBeUndefined()

  await page.getByLabel('Ik begrijp het risico en wil dit toch verkopen').check()
  await page.getByRole('button', { name: 'Bestelling opslaan' }).click()
  await expect(page).toHaveURL(new RegExp(`/orders/${order.id}$`))
  expect(created).toMatchObject({ acknowledgeRisk: true })
})

test('the detail screen shows progress, totals and moves the order along', async ({ page }) => {
  await seedSignedInSession(page)
  let current: Record<string, unknown> = order
  const moves: unknown[] = []
  await mockApi(page, {
    [`GET /api/orders/${order.id}`]: () => ({ status: 200, json: current }),
    [`POST /api/orders/${order.id}/status`]: (body) => {
      moves.push(body)
      current = { ...order, status: 'Confirmed', allowedTransitions: ['InProduction', 'Ready', 'Cancelled'], confirmedAt: '2026-11-21T10:00:00Z' }
      return { status: 200, json: current }
    },
  })

  await page.goto(`/orders/${order.id}`)
  await expect(page.getByRole('heading', { name: '#12' })).toBeVisible()
  await expect(page.getByText('Vase - Large')).toBeVisible()
  await expect(page.getByText('2 × € 20,00')).toBeVisible()
  await expect(page.getByRole('listitem').filter({ hasText: 'Concept' }).first()).toHaveAttribute('aria-current', 'step')
  await expect(page.getByText('Winst')).toBeVisible()

  await page.getByRole('button', { name: 'Bevestigen' }).click()

  await expect(page.getByRole('status').first()).toContainText('Status is nu Bevestigd.')
  await expect(page.getByRole('button', { name: 'Start productie' })).toBeVisible()
  expect(moves).toEqual([{ status: 'Confirmed', acknowledgeRisk: false }])
})

test('confirming an order with uncleared rights asks for the risk to be accepted', async ({ page }) => {
  await seedSignedInSession(page)
  const moves: Array<Record<string, unknown>> = []
  await mockApi(page, {
    [`GET /api/orders/${order.id}`]: () => ({ status: 200, json: { ...order, hasRightsWarning: true, lines: [{ ...line, commercialRightsStatus: 'NotCleared', commercialRightsBlockers: ['Dragon'] }] } }),
    [`POST /api/orders/${order.id}/status`]: (body) => {
      moves.push(body as Record<string, unknown>)
      return moves.length === 1
        ? { status: 400, json: { title: 'Validation', errors: { commercialRights: ['Commercial rights are not verified'] } } }
        : { status: 200, json: { ...order, status: 'Confirmed', allowedTransitions: ['Ready'] } }
    },
  })

  await page.goto(`/orders/${order.id}`)
  await expect(page.getByText('Risico nog niet erkend')).toBeVisible()
  await page.getByRole('button', { name: 'Bevestigen' }).click()
  await expect(page.getByRole('dialog')).toContainText('auditlog')
  await page.getByRole('button', { name: 'Risico aanvaarden en bevestigen' }).click()

  await expect(page.getByRole('status').first()).toContainText('Bevestigd')
  expect(moves.map(move => move.acknowledgeRisk)).toEqual([false, true])
})

test('recording a payment validates the amount and updates the payment status', async ({ page }) => {
  await seedSignedInSession(page)
  let body: unknown
  await mockApi(page, {
    [`GET /api/orders/${order.id}`]: () => ({ status: 200, json: order }),
    [`POST /api/orders/${order.id}/payments`]: (payload) => {
      body = payload
      return { status: 200, json: { ...order, paymentStatus: 'Partial', amountPaid: 15, amountDue: 25, payments: [{ id: 'p1', amount: 15, paidAt: '2026-11-21T10:00:00Z', method: 'cash', note: null }] } }
    },
  })

  await page.goto(`/orders/${order.id}`)
  await page.getByRole('button', { name: 'Betaling registreren' }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByLabel('Bedrag')).toHaveValue('40')
  await dialog.getByLabel('Bedrag').fill('60')
  await dialog.getByRole('button', { name: 'Betaling registreren' }).click()
  await expect(dialog.getByText('Het bedrag is hoger dan wat nog openstaat.')).toBeVisible()
  await expect(dialog.getByLabel('Bedrag')).toBeFocused()

  await dialog.getByLabel('Bedrag').fill('15')
  await dialog.getByLabel('Betaalwijze').selectOption('cash')
  await dialog.getByRole('button', { name: 'Betaling registreren' }).click()

  await expect(page.getByText('€ 15,00 betaald van € 40,00')).toBeVisible()
  await expect(page.getByText('Deels betaald').first()).toBeVisible()
  expect(body).toEqual({ amount: 15, method: 'cash', note: null })
})

test('a customer page lists the order history', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    [`GET /api/customers/${customer.id}`]: () => ({ status: 200, json: customer }),
    'GET /api/orders': () => ({ status: 200, json: [order] }),
  })

  await page.goto(`/customers/${customer.id}`)

  await expect(page.getByRole('link', { name: /#12/ })).toContainText('Concept')
  await expect(page.getByRole('button', { name: 'Nieuwe bestelling' })).toBeVisible()
})
