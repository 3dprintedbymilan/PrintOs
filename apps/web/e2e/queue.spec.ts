import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const printer = {
  id: 'pr-1',
  name: 'A1 #1',
  model: 'Bambu Lab A1',
  buildVolumeXMillimetres: 256,
  buildVolumeYMillimetres: 256,
  buildVolumeZMillimetres: 256,
  nozzleDiameterMillimetres: 0.4,
  powerDrawWatts: 95,
  purchaseCost: 339,
  lifetimeHours: 5000,
  depreciationPerHour: 0.07,
  isArchived: false,
}

function queueItem(overrides: Record<string, unknown>) {
  return {
    id: 'job-1',
    name: '#12 Vase',
    status: 'Planned',
    printerId: printer.id,
    printerName: printer.name,
    plannedUnits: 2,
    durationMinutes: 90,
    startedAt: null,
    queuePosition: null,
    orderId: 'order-12',
    orderNumber: 12,
    customerName: 'Jan Peeters',
    dueDate: '2027-01-05',
    isOverdue: false,
    orderLineId: 'line-1',
    lineDescription: 'Vase',
    lineQuantity: 4,
    hasFilament: true,
    filamentShort: false,
    canMoveUp: false,
    canMoveDown: true,
    filaments: [],
    ...overrides,
  }
}

const shortFilament = {
  spoolId: 's1',
  materialName: 'PLA',
  colorName: 'Jade White',
  colorHex: '#F4F4F0',
  neededGrams: 90,
  availableGrams: 40,
  archived: false,
  enough: false,
}

const failedJob = {
  id: 'job-9',
  printerId: printer.id,
  printerName: printer.name,
  name: '#12 Vase (spaghetti)',
  status: 'Failed',
  startedAt: '2026-11-20T10:00:00Z',
  finishedAt: '2026-11-20T11:00:00Z',
  durationMinutes: 60,
  notes: null,
  finishedUnits: null,
  externalSource: null,
  externalId: null,
  modelId: null,
  totalGrams: 0,
  wasteGrams: 0,
  createdAt: '2026-11-20T09:00:00Z',
  filaments: [],
  orderLineId: 'line-1',
  orderId: 'order-12',
  orderNumber: 12,
  orderLineDescription: 'Vase',
  plannedUnits: 2,
  requeued: false,
}

test('the queue segment lists jobs, warns about filament and moves a job down', async ({ page }) => {
  await seedSignedInSession(page)
  let items = [
    queueItem({ id: 'job-1', name: '#12 Vase', canMoveUp: false, canMoveDown: true, filamentShort: true, filaments: [shortFilament] }),
    queueItem({ id: 'job-2', name: '#13 Bowl', orderNumber: 13, customerName: null, dueDate: null, canMoveUp: true, canMoveDown: false, hasFilament: false }),
  ]
  let moved = ''
  await mockApi(page, {
    'GET /api/production/queue': () => ({ status: 200, json: { items, estimatedMinutes: 180, withoutEstimate: 0, manualOrder: moved !== '' } }),
    'GET /api/production/suggestions': () => ({ status: 200, json: [] }),
    'GET /api/print-jobs': () => ({ status: 200, json: [] }),
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
    'POST /api/production/queue/job-1/move': (body) => {
      moved = (body as { direction: string }).direction
      items = [
        { ...items[1]!, canMoveUp: false, canMoveDown: true },
        { ...items[0]!, canMoveUp: true, canMoveDown: false },
      ]
      return { status: 200, json: { items, estimatedMinutes: 180, withoutEstimate: 0, manualOrder: true } }
    },
  })

  await page.goto('/prints?section=queue')
  const main = page.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Wachtrij' })).toBeVisible()
  await expect(main.getByText('#12 Vase').first()).toBeVisible()
  await expect(main.getByRole('note').filter({ hasText: 'nog maar 40 g over' })).toBeVisible()
  await expect(main.getByRole('button', { name: '#12 Vase omhoog' })).toBeDisabled()

  await main.getByRole('button', { name: '#12 Vase omlaag' }).click()
  await expect.poll(() => moved).toBe('down')
  await expect(main.getByRole('status').filter({ hasText: 'verplaatst' })).toBeVisible()
  await expect(main.locator('ol > li').first()).toContainText('#13 Bowl')
  await expect(main.getByRole('button', { name: '#13 Bowl omhoog' })).toBeDisabled()
  await expect(main.getByRole('button', { name: 'Terug naar automatische volgorde' })).toBeVisible()
})

test('failed prints can be requeued and orders can be planned in jobs', async ({ page }) => {
  await seedSignedInSession(page)
  let failed = [failedJob]
  let planned = false
  let planBody: unknown = null
  await mockApi(page, {
    'GET /api/production/queue': () => ({ status: 200, json: { items: [], estimatedMinutes: 0, withoutEstimate: 0, manualOrder: false } }),
    'GET /api/production/suggestions': () => ({
      status: 200,
      json: planned
        ? []
        : [{ orderId: 'order-14', orderNumber: 14, customerName: 'Els', dueDate: null, isOverdue: false, status: 'Confirmed', lines: [{ lineId: 'l14', description: 'Mug', ordered: 5, produced: 0, queued: 0, remaining: 5, complete: false, jobs: [] }] }],
    }),
    'GET /api/print-jobs': () => ({ status: 200, json: failed }),
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
    'POST /api/print-jobs/job-9/requeue': () => {
      failed = []
      return { status: 201, json: { ...failedJob, id: 'job-10', status: 'Planned' } }
    },
    'POST /api/production/orders/order-14/jobs': (body) => {
      planBody = body
      planned = true
      return { status: 201, json: [{}, {}, {}] }
    },
  })

  await page.goto('/prints?section=queue')
  const main = page.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Mislukte prints' })).toBeVisible()
  await main.getByRole('button', { name: '#12 Vase (spaghetti) opnieuw inplannen' }).click()
  await expect(main.getByRole('status').filter({ hasText: 'opnieuw ingepland' })).toBeVisible()
  await expect(main.getByRole('heading', { name: 'Mislukte prints' })).toHaveCount(0)

  await expect(main.getByText('5 van 5 te plannen')).toBeVisible()
  await main.getByRole('button', { name: 'Productie van bestelling 14 plannen' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Stuks per plaat (optioneel)').fill('2')
  await expect(dialog.getByText('5 stuks worden 3 jobs.')).toBeVisible()
  await dialog.getByRole('button', { name: 'Jobs aanmaken' }).click()
  await expect(main.getByRole('status').filter({ hasText: '3 jobs aangemaakt' })).toBeVisible()
  expect(planBody).toMatchObject({ lineId: null, printerId: printer.id, unitsPerJob: 2 })
})

test('the queue shows an empty state', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/production/queue': () => ({ status: 200, json: { items: [], estimatedMinutes: 0, withoutEstimate: 0, manualOrder: false } }),
    'GET /api/production/suggestions': () => ({ status: 200, json: [] }),
    'GET /api/print-jobs': () => ({ status: 200, json: [] }),
    'GET /api/printers': () => ({ status: 200, json: [] }),
  })

  await page.goto('/prints?section=queue')
  await expect(page.getByRole('main').getByText('Er valt nu niets te produceren')).toBeVisible()
})

test('the order detail shows production progress per line', async ({ page }) => {
  await seedSignedInSession(page)
  const order = {
    id: 'order-12',
    number: 12,
    customerId: null,
    customerName: null,
    status: 'InProduction',
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
    linesEditable: false,
    headerEditable: true,
    paymentsEditable: true,
    hasRightsWarning: false,
    allowedTransitions: ['Ready', 'Confirmed', 'Cancelled'],
    lines: [{ id: 'line-1', variantId: null, description: 'Vase', sku: null, quantity: 4, listUnitPrice: null, unitPrice: 10, discountPercent: 0, lineTotal: 40, unitCost: 1.75, commercialRightsStatus: null, commercialRightsBlockers: [], riskAcknowledged: false }],
    payments: [],
  }
  await mockApi(page, {
    'GET /api/orders/order-12': () => ({ status: 200, json: order }),
    'GET /api/production/orders/order-12': () => ({
      status: 200,
      json: {
        orderId: 'order-12',
        ordered: 4,
        produced: 2,
        queued: 1,
        percent: 50,
        complete: false,
        failedJobs: 1,
        lines: [{
          lineId: 'line-1',
          description: 'Vase',
          ordered: 4,
          produced: 2,
          queued: 1,
          remaining: 1,
          complete: false,
          jobs: [
            { id: 'job-1', name: '#12 Vase', status: 'Done', printerName: 'A1 #1', plannedUnits: 2, finishedUnits: 2, requeued: false, requeuedFromId: null },
            { id: 'job-2', name: '#12 Vase (2/2)', status: 'Planned', printerName: 'A1 #1', plannedUnits: 1, finishedUnits: null, requeued: false, requeuedFromId: null },
          ],
        }],
      },
    }),
  })

  await page.goto('/orders/order-12')
  const main = page.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Productie' })).toBeVisible()
  await expect(main.getByText('2 van 4 stuks geproduceerd')).toBeVisible()
  await expect(main.getByRole('progressbar', { name: 'Productie' })).toHaveAttribute('aria-valuenow', '50')
  await expect(main.getByText('2 / 4')).toBeVisible()
  await expect(main.getByRole('link', { name: /#12 Vase \(2\/2\)/ })).toBeVisible()
  await expect(main.getByRole('note').filter({ hasText: 'telt niet mee' })).toBeVisible()
})

test('a finished import can be linked to an order line', async ({ page }) => {
  await seedSignedInSession(page)
  const imported = { ...failedJob, id: 'job-5', name: 'Bambu task 5', status: 'Done', finishedUnits: null, plannedUnits: null, orderLineId: null, orderId: null, orderNumber: null, orderLineDescription: null, externalSource: 'bambu-cloud', externalId: '5' }
  let linkBody: unknown = null
  await mockApi(page, {
    'GET /api/print-jobs/job-5': () => ({ status: 200, json: imported }),
    'GET /api/production/suggestions': () => ({
      status: 200,
      json: [{ orderId: 'order-12', orderNumber: 12, customerName: null, dueDate: null, isOverdue: false, status: 'Confirmed', lines: [{ lineId: 'line-1', description: 'Vase', ordered: 4, produced: 0, queued: 0, remaining: 4, complete: false, jobs: [] }] }],
    }),
    'POST /api/print-jobs/job-5/order-line': (body) => {
      linkBody = body
      return { status: 200, json: { ...imported, finishedUnits: 3, orderLineId: 'line-1', orderId: 'order-12', orderNumber: 12, orderLineDescription: 'Vase' } }
    },
  })

  await page.goto('/print-jobs/job-5')
  const main = page.getByRole('main')
  await main.getByRole('button', { name: 'Koppelen aan bestelregel' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('button', { name: 'Koppelen' }).click()
  await expect(dialog.getByText('Kies een bestelregel.')).toBeVisible()
  await dialog.getByLabel('Bestelregel').selectOption('line-1')
  await dialog.getByLabel('Aantal stuks klaar').fill('3')
  await dialog.getByRole('button', { name: 'Koppelen' }).click()

  await expect(main.getByRole('status').filter({ hasText: 'Gekoppeld aan de bestelregel.' })).toBeVisible()
  await expect(main.getByRole('link', { name: '#12 · Vase' })).toBeVisible()
  expect(linkBody).toEqual({ orderLineId: 'line-1', units: 3 })
})
