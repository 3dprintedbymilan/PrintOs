import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const printer = {
  id: '55555555-5555-5555-5555-555555555555',
  name: 'A1 #1',
  model: 'Bambu Lab A1',
  buildVolumeXMillimetres: 256,
  buildVolumeYMillimetres: 256,
  buildVolumeZMillimetres: 256,
  nozzleDiameterMillimetres: 0.4,
  powerDrawWatts: 95,
  purchaseCost: 339,
  lifetimeHours: 5000,
  depreciationPerHour: 0.0678,
  isArchived: false,
}
const spool = {
  id: '33333333-3333-3333-3333-333333333333',
  materialId: '44444444-4444-4444-4444-444444444444',
  materialName: 'PLA Basic',
  supplierId: null,
  supplierName: null,
  colorName: 'Jade White',
  colorHex: '#F4F4F0',
  cost: 20,
  costPerGram: 0.02,
  initialWeightGrams: 1000,
  remainingWeightGrams: 750,
  isArchived: false,
}
const job = {
  id: '66666666-6666-6666-6666-666666666666',
  printerId: printer.id,
  printerName: printer.name,
  name: 'Benchy',
  status: 'Done',
  startedAt: '2026-11-26T08:00:00Z',
  finishedAt: '2026-11-26T09:35:00Z',
  durationMinutes: 95,
  notes: 'Perfect surface',
  finishedUnits: 1,
  externalSource: null,
  externalId: null,
  modelId: null,
  totalGrams: 42,
  wasteGrams: 4,
  createdAt: '2026-11-26T07:00:00Z',
  filaments: [{ spoolId: spool.id, materialName: 'PLA Basic', colorName: 'Jade White', colorHex: '#F4F4F0', grams: 42, wasteGrams: 4 }],
}
const baseMocks = {
  'GET /api/printers': () => ({ status: 200, json: [printer] }),
  'GET /api/spools': () => ({ status: 200, json: [spool] }),
}

test('the print log lists jobs with status chips and filters them', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    ...baseMocks,
    'GET /api/print-jobs': () => ({
      status: 200,
      json: [job, { ...job, id: 'x', name: 'Vase', status: 'Failed', durationMinutes: null, totalGrams: 12, filaments: [] }],
    }),
  })

  await page.goto('/prints')

  const benchy = page.getByRole('link', { name: /Benchy/ })
  await expect(benchy).toContainText('Klaar')
  await expect(benchy).toContainText('1 h 35 min')
  await expect(benchy).toContainText('42 g')
  await expect(page.getByRole('link', { name: /Vase/ })).toContainText('Mislukt')

  await page.getByLabel('Status', { exact: true }).selectOption('Failed')
  await expect(benchy).toHaveCount(0)
  await expect(page.getByRole('link', { name: /Vase/ })).toBeVisible()

  await page.getByLabel('Status', { exact: true }).selectOption('')
  await page.getByLabel('Prints zoeken').fill('zzz')
  await expect(page.getByText('Geen prints gevonden voor deze filters.')).toBeVisible()
})

test('an empty print log invites logging the first print', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { ...baseMocks, 'GET /api/print-jobs': () => ({ status: 200, json: [] }) })

  await page.goto('/prints')

  await expect(page.getByText('Nog geen prints. Log je eerste print.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Nieuwe print' })).toBeVisible()
})

test('a failed load offers a retry', async ({ page }) => {
  await seedSignedInSession(page)
  let calls = 0
  await mockApi(page, {
    ...baseMocks,
    'GET /api/print-jobs': () => (++calls <= 2 ? { status: 500 } : { status: 200, json: [job] }),
  })

  await page.goto('/prints')
  await page.getByRole('button', { name: 'Opnieuw proberen' }).click()
  await expect(page.getByRole('link', { name: /Benchy/ })).toBeVisible()
})

test('logging a finished print validates, books filament and opens the detail', async ({ page }) => {
  await seedSignedInSession(page)
  let posted: unknown
  await mockApi(page, {
    ...baseMocks,
    'GET /api/print-jobs': () => ({ status: 200, json: [] }),
    'POST /api/print-jobs': (body) => {
      posted = body
      return { status: 201, json: job }
    },
    [`GET /api/print-jobs/${job.id}`]: () => ({ status: 200, json: job }),
  })

  await page.goto('/print-jobs/new')
  await page.getByLabel('Resultaat').or(page.getByLabel('Status', { exact: true })).selectOption('Done')
  await page.getByRole('button', { name: 'Print opslaan' }).click()
  await expect(page.getByText('Vul Naam in.')).toBeVisible()
  await expect(page.getByLabel('Naam')).toBeFocused()

  await page.getByLabel('Naam').fill('Benchy')
  await page.getByRole('button', { name: 'Spoel toevoegen' }).click()
  await page.getByRole('button', { name: 'Print opslaan' }).click()
  await expect(page.getByText('Vul Spoel in.')).toBeVisible()

  await page.getByLabel('Spoel', { exact: true }).selectOption(spool.id)
  await page.getByLabel('Gebruikt (gram)').fill('42,5')
  await page.getByRole('button', { name: 'Print opslaan' }).click()
  await expect(page.getByText('Gebruikt (gram) moet een geheel getal zijn.')).toBeVisible()

  await page.getByLabel('Gebruikt (gram)').fill('42')
  await page.getByLabel('Waarvan afval (gram)').fill('4')
  await page.getByRole('button', { name: 'Print opslaan' }).click()

  await expect(page).toHaveURL(new RegExp(`/print-jobs/${job.id}$`))
  expect(posted).toMatchObject({
    printerId: printer.id,
    name: 'Benchy',
    status: 'Done',
    filaments: [{ spoolId: spool.id, grams: 42, wasteGrams: 4 }],
  })
  await expect(page.getByRole('heading', { name: 'Filament' })).toBeVisible()
  await expect(page.getByText('4 g afval')).toBeVisible()
})

test('a server stock error shows inline on the spool line', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    ...baseMocks,
    'POST /api/print-jobs': () => ({ status: 400, json: { title: 'Validation', errors: { 'filaments[0].grams': ['The spool only has 750 g left.'] } } }),
  })

  await page.goto('/print-jobs/new')
  await page.getByLabel('Naam').fill('Big one')
  await page.getByLabel('Status', { exact: true }).selectOption('Done')
  await page.getByRole('button', { name: 'Spoel toevoegen' }).click()
  await page.getByLabel('Spoel', { exact: true }).selectOption(spool.id)
  await page.getByLabel('Gebruikt (gram)').fill('900')
  await page.getByRole('button', { name: 'Print opslaan' }).click()

  await expect(page.getByText('Deze spoel heeft nog maar 750 g over.')).toBeVisible()
  await expect(page.getByLabel('Gebruikt (gram)')).toBeFocused()
})

test('a planned job can be started and then finished as failed', async ({ page }) => {
  await seedSignedInSession(page)
  let current: Record<string, unknown> = { ...job, status: 'Planned', startedAt: null, finishedAt: null, durationMinutes: null, finishedUnits: null, notes: null }
  let finishBody: unknown
  await mockApi(page, {
    ...baseMocks,
    [`GET /api/print-jobs/${job.id}`]: () => ({ status: 200, json: current }),
    [`POST /api/print-jobs/${job.id}/start`]: () => {
      current = { ...current, status: 'Printing', startedAt: '2026-11-26T10:00:00Z' }
      return { status: 200, json: current }
    },
    [`POST /api/print-jobs/${job.id}/finish`]: (body) => {
      finishBody = body
      current = { ...current, status: 'Failed', finishedAt: '2026-11-26T10:30:00Z', durationMinutes: 30 }
      return { status: 200, json: current }
    },
  })

  await page.goto(`/print-jobs/${job.id}`)
  await expect(page.getByText('Gepland')).toBeVisible()
  await page.getByRole('button', { name: 'Print starten' }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Print gestart.' })).toBeVisible()
  await expect(page.getByText('Bezig')).toBeVisible()

  await page.getByRole('button', { name: 'Print afronden' }).click()
  await page.getByLabel('Resultaat').selectOption('Failed')
  await page.getByRole('button', { name: 'Print afronden' }).last().click()

  await expect(page.getByRole('status').filter({ hasText: 'Print afgerond en filament verwerkt.' })).toBeVisible()
  await expect(page.getByText('Mislukt')).toBeVisible()
  expect(finishBody).toMatchObject({ status: 'Failed', filaments: [{ spoolId: spool.id, grams: 42, wasteGrams: 4 }] })
})

test('deleting a print asks for confirmation', async ({ page }) => {
  await seedSignedInSession(page)
  let deleted = false
  await mockApi(page, {
    ...baseMocks,
    'GET /api/print-jobs': () => ({ status: 200, json: [] }),
    [`GET /api/print-jobs/${job.id}`]: () => ({ status: 200, json: job }),
    [`DELETE /api/print-jobs/${job.id}`]: () => {
      deleted = true
      return { status: 204 }
    },
  })

  await page.goto(`/print-jobs/${job.id}`)
  await page.getByRole('button', { name: 'Print verwijderen' }).click()
  await expect(page.getByText('Het gebruikte filament gaat terug naar de spoelen.')).toBeVisible()
  await page.getByRole('dialog').getByRole('button', { name: 'Print verwijderen' }).click()

  await expect(page).toHaveURL(/\/prints$/)
  expect(deleted).toBe(true)
})
