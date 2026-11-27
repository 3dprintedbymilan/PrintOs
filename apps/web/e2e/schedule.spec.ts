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

function item(overrides: Record<string, unknown>) {
  return {
    jobId: 'job-1',
    name: '#12 Vase',
    status: 'Planned',
    start: '2027-03-01T08:00:00',
    end: '2027-03-01T09:30:00',
    durationMinutes: 90,
    durationEstimated: false,
    pinned: false,
    pinnedStartAt: null,
    late: false,
    noFilament: false,
    priority: 1,
    orderNumber: 12,
    dueDate: '2027-03-05',
    plannedUnits: 2,
    ...overrides,
  }
}

function schedule(overrides: Record<string, unknown> = {}) {
  return {
    generatedAt: '2027-03-01T07:00:00',
    horizonEnd: '2027-03-15T07:00:00',
    defaultDurationMinutes: 60,
    lanes: [{
      printerId: printer.id,
      name: printer.name,
      archived: false,
      items: [
        item({ jobId: 'job-0', name: '#11 Bowl', status: 'Printing', start: '2027-03-01T07:00:00', end: '2027-03-01T08:00:00', orderNumber: 11 }),
        item({ late: true }),
        item({ jobId: 'job-2', name: '#13 Mug', start: '2027-03-02T09:00:00', end: '2027-03-02T10:00:00', durationEstimated: true, priority: 3, orderNumber: null, dueDate: null }),
      ],
    }],
    blocked: [{ jobId: 'job-3', name: '#14 Giant', reason: 'TooLarge', priority: 1, orderNumber: 14, dueDate: null, pinned: false, partXMillimetres: 500, partYMillimetres: 10, partZMillimetres: 10 }],
    beyondHorizon: 0,
    reassigned: 0,
    ...overrides,
  }
}

test('the schedule shows an agenda per printer with flags and blocked jobs', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/production/schedule': () => ({ status: 200, json: schedule() }),
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
  })

  await page.goto('/schedule')
  const main = page.getByRole('main')
  await expect(main.getByRole('heading', { name: 'A1 #1' })).toBeVisible()
  await expect(main.getByText('#12 Vase')).toBeVisible()
  await expect(main.getByText('Te laat')).toBeVisible()
  await expect(main.locator('.chip', { hasText: 'Dringend' })).toBeVisible()
  await expect(main.getByText('geschat op 60 min')).toBeVisible()
  await expect(main.getByRole('heading', { name: 'Kan niet ingepland worden' })).toBeVisible()
  await expect(main.getByText('Past op geen enkele printer')).toBeVisible()
  await expect(main.getByText('PrintOS leest je printers alleen uit', { exact: false })).toBeVisible()
})

test('a job can be pinned with a priority and the schedule refreshes', async ({ page }) => {
  await seedSignedInSession(page)
  let body: Record<string, unknown> | null = null
  await mockApi(page, {
    'GET /api/production/schedule': () => ({ status: 200, json: schedule() }),
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
    'PUT /api/production/schedule/jobs/job-1': (payload) => {
      body = payload as Record<string, unknown>
      return { status: 200, json: schedule({ blocked: [] }) }
    },
  })

  await page.goto('/schedule')
  const main = page.getByRole('main')
  await main.getByRole('button', { name: '#12 Vase aanpassen' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Prioriteit').selectOption('2')
  await dialog.getByLabel('Printer', { exact: true }).selectOption('pr-1')
  await dialog.getByRole('button', { name: 'Opslaan' }).click()
  await expect(main.getByRole('status').filter({ hasText: '#12 Vase bijgewerkt' })).toBeVisible()
  expect(body).toMatchObject({ priority: 2, printerId: 'pr-1', clearPartSize: true })
  await expect(main.getByRole('heading', { name: 'Kan niet ingepland worden' })).toHaveCount(0)
})

test('a part size must be complete and a start time needs a printer', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/production/schedule': () => ({ status: 200, json: schedule() }),
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
  })

  await page.goto('/schedule')
  const main = page.getByRole('main')
  await main.getByRole('button', { name: '#12 Vase aanpassen' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Breedte').fill('100')
  await dialog.getByRole('button', { name: 'Opslaan' }).click()
  await expect(dialog.getByText('Vul breedte, diepte en hoogte in', { exact: false })).toBeVisible()
  await expect(dialog.getByLabel('Breedte')).toBeFocused()

  await dialog.getByLabel('Breedte').fill('')
  await dialog.getByLabel('Vaste starttijd (optioneel)').fill('2027-03-03T10:00')
  await dialog.getByRole('button', { name: 'Opslaan' }).click()
  await expect(dialog.getByText('Kies eerst een printer', { exact: false })).toBeVisible()
})

test('re-planning reports what moved and the queue links to the schedule', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/production/schedule': () => ({ status: 200, json: schedule() }),
    'POST /api/production/schedule/replan': () => ({ status: 200, json: schedule({ reassigned: 2 }) }),
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
    'GET /api/production/queue': () => ({
      status: 200,
      json: { items: [{ id: 'job-1', name: '#12 Vase', status: 'Planned', printerId: printer.id, printerName: printer.name, plannedUnits: 2, durationMinutes: 90, startedAt: null, queuePosition: null, orderId: null, orderNumber: null, customerName: null, dueDate: null, isOverdue: false, orderLineId: null, lineDescription: null, lineQuantity: null, hasFilament: true, filamentShort: false, canMoveUp: false, canMoveDown: false, filaments: [] }], estimatedMinutes: 90, withoutEstimate: 0, manualOrder: false },
    }),
    'GET /api/production/suggestions': () => ({ status: 200, json: [] }),
    'GET /api/print-jobs': () => ({ status: 200, json: [] }),
  })

  await page.goto('/prints?section=queue')
  await page.getByRole('main').getByRole('link', { name: 'Planning bekijken' }).click()
  await expect(page).toHaveURL(/\/schedule$/)
  const main = page.getByRole('main')
  await main.getByRole('button', { name: 'Nu herplannen' }).click()
  await expect(main.getByRole('status').filter({ hasText: '2 jobs naar een andere printer verplaatst' })).toBeVisible()
})

test('the schedule shows an error state and retries', async ({ page }) => {
  await seedSignedInSession(page)
  let calls = 0
  await mockApi(page, {
    'GET /api/production/schedule': () => {
      calls += 1
      return calls <= 2 ? { status: 500 } : { status: 200, json: schedule({ blocked: [] }) }
    },
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
  })

  await page.goto('/schedule')
  const main = page.getByRole('main')
  await main.getByRole('button', { name: /opnieuw/i }).click()
  await expect(main.getByRole('heading', { name: 'A1 #1' })).toBeVisible()
})
