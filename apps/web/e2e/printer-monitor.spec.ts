import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const window24 = { hours: 24, printingPercent: 40, printingMinutes: 576, observedMinutes: 1440, offlineMinutes: 0, coveragePercent: 100 }
const window168 = { hours: 168, printingPercent: 25, printingMinutes: 2520, observedMinutes: 10080, offlineMinutes: 300, coveragePercent: 100 }
const window720 = { hours: 720, printingPercent: null, printingMinutes: 0, observedMinutes: 0, offlineMinutes: 0, coveragePercent: 0 }

const base = {
  model: 'A1', serial: 'TEST-A1-0001', layer: null, totalLayers: null, filamentType: null, filamentColor: null, printErrorCode: null, hmsCodes: [], trays: [],
  utilization: [window24, window168, window720], timeline: [], jobName: null, progressPercent: null, remainingMinutes: null, nozzleTemperature: null, bedTemperature: null,
}

const printing = {
  ...base, printerId: '1', printerName: 'Workshop A1', state: 'printing', lastKnownState: 'printing', online: true, stale: false, lastSeenAt: new Date().toISOString(),
  jobName: 'Benchy', progressPercent: 42, remainingMinutes: 83, layer: 120, totalLayers: 340, nozzleTemperature: 219.6, bedTemperature: 60.2, filamentType: 'PETG', filamentColor: '#1A1A1A',
  printErrorCode: '0500-8011', hmsCodes: ['0300-0100-0001-0002'],
  trays: [{ id: '1', type: 'PETG', color: '#1A1A1A', current: true }],
  timeline: [
    { state: 'printing', jobName: 'Benchy', at: '2026-11-27T10:00:00Z', durationMinutes: 50 },
    { state: 'idle', jobName: null, at: '2026-11-27T08:00:00Z', durationMinutes: 120 },
  ],
}

const offline = {
  ...base, printerId: '2', printerName: 'Garage A1', serial: 'TEST-A1-0002', state: 'offline', lastKnownState: 'printing', online: false, stale: true,
  lastSeenAt: '2026-11-26T10:00:00Z', jobName: 'Vase', progressPercent: 60, remainingMinutes: 30, nozzleTemperature: 200, bedTemperature: 55,
}

function monitor(printers: unknown[], extra: Record<string, unknown> = {}) {
  return { connected: true, connectionStatus: 'connected', serverTime: new Date().toISOString(), printers, ...extra }
}

test('shows status, utilization per window, health, timeline and the honest camera state', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { 'GET /api/bambu/monitor': () => ({ status: 200, json: monitor([printing, offline]) }) })

  await page.goto('/printers/monitor')
  await expect(page.getByRole('heading', { name: 'Workshop A1' })).toBeVisible()
  await expect(page.getByText('Benchy').first()).toBeVisible()
  await expect(page.getByText('40%').first()).toBeVisible()
  await expect(page.getByText('Foutcode 0500-8011')).toBeVisible()
  await expect(page.getByText('HMS-melding 0300-0100-0001-0002')).toBeVisible()
  await expect(page.getByText('Camera niet beschikbaar via de cloud').first()).toBeVisible()
  await expect(page.getByRole('link', { name: 'Lees waarom' }).first()).toHaveAttribute('href', /bambu-spike\.md/)

  await expect(page.getByText(/Printer offline\. Laatst bekende status: .*printen/)).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Garage A1' })).toBeVisible()

  await page.getByRole('radio', { name: '7 dagen' }).check({ force: true })
  await expect(page.getByText('25%').first()).toBeVisible()
  await page.getByRole('radio', { name: '30 dagen' }).check({ force: true })
  await expect(page.getByText('Nog geen gegevens voor deze periode.').first()).toBeVisible()
})

test('not connected and token expired states', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { 'GET /api/bambu/monitor': () => ({ status: 200, json: monitor([], { connected: false, connectionStatus: null }) }) })
  await page.goto('/printers/monitor')
  await expect(page.getByText('Verbind je Bambu Lab-account om je printers live te volgen.')).toBeVisible()

  await mockApi(page, { 'GET /api/bambu/monitor': () => ({ status: 200, json: monitor([offline], { connectionStatus: 'tokenExpired' }) }) })
  await page.goto('/printers/monitor')
  await expect(page.getByRole('alert').filter({ hasText: 'Opnieuw verbinden' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Garage A1' })).toBeVisible()
})

test('a failing monitor request shows an error with retry, never a endless spinner', async ({ page }) => {
  await seedSignedInSession(page)
  let calls = 0
  await mockApi(page, {
    'GET /api/bambu/monitor': () => {
      calls++
      return calls <= 2 ? { status: 500, json: { title: 'boom' } } : { status: 200, json: monitor([printing]) }
    },
  })
  await page.goto('/printers/monitor')
  await expect(page.getByRole('alert').filter({ hasText: 'Opnieuw proberen' })).toBeVisible()
  await page.getByRole('button', { name: 'Opnieuw proberen' }).click()
  await expect(page.getByRole('heading', { name: 'Workshop A1' })).toBeVisible()
})

test('is reachable from the printers list', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/printers': () => ({ status: 200, json: [] }),
    'GET /api/bambu/status': () => ({ status: 200, json: { connected: false, connectionStatus: null, printers: [] } }),
    'GET /api/bambu/monitor': () => ({ status: 200, json: monitor([printing]) }),
  })
  await page.goto('/printers')
  await page.getByRole('main').getByRole('link', { name: /Printermonitor/ }).click()
  await expect(page).toHaveURL(/\/printers\/monitor$/)
})
