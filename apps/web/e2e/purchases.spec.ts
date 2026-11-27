import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const supplierId = '55555555-5555-5555-5555-555555555555'
const materialId = '44444444-4444-4444-4444-444444444444'
const purchaseId = '66666666-6666-6666-6666-666666666666'
const lineId = '77777777-7777-7777-7777-777777777777'

const line = {
  id: lineId, materialId, materialName: 'PLA Basic', colorName: 'Jade White', colorHex: '#F4F4F0', quantity: 5, receivedQuantity: 0,
  weightGramsPerSpool: 1000, unitPrice: 20, landedUnitCost: 21, costPerKg: 21, lineTotal: 100,
}
const detail = {
  id: purchaseId, supplierId, supplierName: 'Bambu Lab', purchasedOn: '2026-11-01', invoiceReference: 'INV-1', notes: null, status: 'Ordered', currency: 'EUR',
  subtotal: 100, shippingCost: 5, discount: 0, total: 105, hasReceiptFile: false, receiptFileName: null, lines: [line], createdAt: '2026-11-01T10:00:00Z',
}
const summary = {
  id: purchaseId, supplierId, supplierName: 'Bambu Lab', purchasedOn: '2026-11-01', invoiceReference: 'INV-1', status: 'Ordered', total: 105, currency: 'EUR',
  lineCount: 1, orderedQuantity: 5, receivedQuantity: 0,
}

test('the purchase list shows status and links to the detail', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/purchases': () => ({ status: 200, json: [summary, { ...summary, id: 'x', status: 'Cancelled', invoiceReference: null }] }),
  })

  await page.goto('/inventory/purchases')

  await expect(page.getByRole('heading', { level: 1, name: 'Inkopen' })).toBeVisible()
  await expect(page.getByRole('link', { name: /Bambu Lab · INV-1/ })).toContainText('Besteld')
  await page.getByLabel('Status').selectOption('Cancelled')
  await expect(page.getByRole('link', { name: /INV-1/ })).toHaveCount(0)
})

test('an empty list invites the first purchase', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { 'GET /api/purchases': () => ({ status: 200, json: [] }) })

  await page.goto('/inventory/purchases')

  await expect(page.getByText('Nog geen inkopen.')).toBeVisible()
})

test('adding a purchase validates, then posts the lines and opens the detail', async ({ page }) => {
  await seedSignedInSession(page)
  let posted: unknown
  await mockApi(page, {
    'GET /api/materials': () => ({ status: 200, json: [{ id: materialId, name: 'PLA Basic', type: 'PLA' }] }),
    'GET /api/suppliers': () => ({ status: 200, json: [{ id: supplierId, name: 'Bambu Lab', website: null }] }),
    'GET /api/workspace/settings': () => ({ status: 200, json: { currency: 'EUR' } }),
    'POST /api/purchases': (body) => {
      posted = body
      return { status: 201, json: detail }
    },
    [`GET /api/purchases/${purchaseId}`]: () => ({ status: 200, json: detail }),
  })

  await page.goto('/inventory/purchases/new')
  await page.getByRole('button', { name: 'Inkoop opslaan' }).click()
  await expect(page.getByText('Kleur is verplicht.')).toBeVisible()
  await expect(page.getByLabel('Kleur', { exact: true })).toBeFocused()

  await page.getByLabel('Kleur', { exact: true }).fill('Jade White')
  await page.getByLabel('Aantal spoelen').fill('5')
  await page.getByLabel('Prijs per spoel').fill('20,00')
  await page.getByLabel('Verzendkosten').fill('5')
  await expect(page.getByText('€ 105,00').last()).toBeVisible()
  await page.getByRole('button', { name: 'Inkoop opslaan' }).click()

  await expect(page).toHaveURL(/\/inventory\/purchases\/6666/)
  expect(posted).toMatchObject({ supplierId, shippingCost: 5, receiveImmediately: true, lines: [{ materialId, colorName: 'Jade White', quantity: 5, unitPrice: 20, weightGramsPerSpool: 1000 }] })
})

test('receiving part of a purchase books the spools and keeps the rest open', async ({ page }) => {
  await seedSignedInSession(page)
  let received: unknown
  await mockApi(page, {
    [`GET /api/purchases/${purchaseId}`]: () => ({ status: 200, json: detail }),
    [`POST /api/purchases/${purchaseId}/receive`]: (body) => {
      received = body
      return { status: 200, json: { ...detail, status: 'PartiallyReceived', lines: [{ ...line, receivedQuantity: 2 }] } }
    },
  })

  await page.goto(`/inventory/purchases/${purchaseId}`)
  await expect(page.getByRole('heading', { level: 2, name: 'Bambu Lab' })).toBeVisible()
  await expect(page.getByText('0 van 5 ontvangen')).toBeVisible()

  const quantity = page.getByLabel(/PLA Basic · Jade White \(nog maximaal 5\)/)
  await quantity.fill('9')
  await page.getByRole('button', { name: 'Op voorraad zetten' }).click()
  await expect(page.getByText('Vul een heel getal van 0 tot 5 in.')).toBeVisible()

  await quantity.fill('2')
  await page.getByRole('button', { name: 'Op voorraad zetten' }).click()
  await expect(page.getByRole('status').filter({ hasText: '2 spoelen op voorraad gezet.' })).toBeVisible()
  await expect(page.getByText('Deels ontvangen')).toBeVisible()
  await expect(page.getByText('Nog 3 spoelen te ontvangen.')).toBeVisible()
  expect(received).toEqual({ lines: [{ lineId, quantity: 2 }] })
})

test('cancelling asks for confirmation', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    [`GET /api/purchases/${purchaseId}`]: () => ({ status: 200, json: detail }),
    [`POST /api/purchases/${purchaseId}/cancel`]: () => ({ status: 200, json: { ...detail, status: 'Cancelled' } }),
  })

  await page.goto(`/inventory/purchases/${purchaseId}`)
  await page.getByRole('button', { name: 'Inkoop annuleren' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.getByRole('dialog').getByRole('button', { name: 'Annuleren' }).click()

  await expect(page.getByRole('status').filter({ hasText: 'Inkoop geannuleerd.' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Inkoop annuleren' })).toHaveCount(0)
})

test('the price screen shows the latest price, change and cheapest supplier', async ({ page }) => {
  await seedSignedInSession(page)
  const price = (id: string, name: string, cost: number, change: number | null, cheapest: boolean) => ({
    supplierId: id, supplierName: name, lastCostPerKg: cost, lastPurchasedOn: '2026-11-01', previousCostPerKg: null, changePercent: change, purchaseCount: 2, isCheapest: cheapest,
  })
  await mockApi(page, {
    'GET /api/price-history': () => ({
      status: 200,
      json: [{
        materialId, materialName: 'PLA Basic', currency: 'EUR',
        points: [
          { purchaseId: 'a', supplierId, supplierName: 'Bambu Lab', purchasedOn: '2026-09-01', costPerKg: 20 },
          { purchaseId: 'b', supplierId: 's2', supplierName: 'Filament Shop', purchasedOn: '2026-10-01', costPerKg: 30 },
          { purchaseId: 'c', supplierId, supplierName: 'Bambu Lab', purchasedOn: '2026-11-01', costPerKg: 22 },
        ],
        latest: { purchaseId: 'c', supplierId, supplierName: 'Bambu Lab', purchasedOn: '2026-11-01', costPerKg: 22 },
        previousCostPerKg: 30, changePercent: -26.7,
        cheapest: price(supplierId, 'Bambu Lab', 22, 10, true),
        suppliers: [price(supplierId, 'Bambu Lab', 22, 10, true), price('s2', 'Filament Shop', 30, null, false)],
      }],
    }),
  })

  await page.goto('/inventory/prices')

  await expect(page.getByRole('heading', { level: 1, name: 'Prijzen' })).toBeVisible()
  await expect(page.getByText('€ 22,00/kg').first()).toBeVisible()
  await expect(page.getByText('−26,7% t.o.v. vorige')).toBeVisible()
  await expect(page.getByText('Goedkoopst')).toBeVisible()
  await expect(page.getByRole('img', { name: /Prijsverloop van PLA Basic/ })).toBeVisible()

  await page.getByRole('radio', { name: 'Per leverancier' }).check({ force: true })
  await expect(page.getByRole('heading', { level: 2, name: 'Filament Shop' })).toBeVisible()
})
