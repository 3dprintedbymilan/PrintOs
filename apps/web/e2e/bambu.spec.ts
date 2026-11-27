import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const printerId = '22222222-2222-2222-2222-222222222222'
const printer = {
  id: printerId, name: 'Workshop A1', model: 'Bambu Lab A1', buildVolumeXMillimetres: 256, buildVolumeYMillimetres: 256, buildVolumeZMillimetres: 256,
  nozzleDiameterMillimetres: 0.4, powerDrawWatts: 95, purchaseCost: 339, lifetimeHours: 5000, depreciationPerHour: 0.0678, isArchived: false,
}
const device = { serial: 'TEST-A1-0001', name: 'Workshop A1', model: 'A1', online: true, printerId: null, printerName: null }
const disconnected = { connected: false, region: null, status: null, accountName: null, connectedAt: null, lastSyncAt: null, flaggedImports: 0, devices: [] }
const printingStatus = {
  connected: true,
  connectionStatus: 'connected',
  printers: [{
    printerId, printerName: 'Workshop A1', serial: 'TEST-A1-0001', state: 'printing', jobName: 'Benchy', progressPercent: 42, remainingMinutes: 83,
    layer: 120, totalLayers: 340, nozzleTemperature: 219.6, bedTemperature: 60.2, filamentType: 'PETG', filamentColor: '#1A1A1A', lastSeenAt: '2026-11-26T10:00:00Z',
  }],
}

test('connect flow: guide, token validation, device list and linking', async ({ page }) => {
  await seedSignedInSession(page)
  let connected = false
  let linked = false
  let sentToken: unknown
  let linkBody: unknown
  await mockApi(page, {
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
    'GET /api/bambu/connection': () => ({ status: 200, json: connected ? { ...disconnected, connected: true, status: 'connected', accountName: 'Test Owner', devices: [linked ? { ...device, printerId, printerName: 'Workshop A1' } : device] } : disconnected }),
    'PUT /api/bambu/connection': (body) => {
      sentToken = body
      connected = true
      return { status: 200, json: { ...disconnected, connected: true, status: 'connected', accountName: 'Test Owner', devices: [device] } }
    },
    'POST /api/bambu/devices/TEST-A1-0001/link': (body) => {
      linkBody = body
      linked = true
      return { status: 200, json: { ...disconnected, connected: true, status: 'connected', devices: [{ ...device, printerId, printerName: 'Workshop A1' }] } }
    },
  })

  await page.goto('/printers/bambu')
  await page.getByText('Waar vind ik het toegangstoken?').click()
  await expect(page.getByText('Kies https://bambulab.com')).toBeVisible()

  await page.getByRole('button', { name: 'Verbinden' }).click()
  await expect(page.getByText('Plak je toegangstoken.')).toBeVisible()
  await expect(page.getByLabel('Toegangstoken')).toBeFocused()

  const field = page.getByLabel('Toegangstoken')
  await field.fill('fake-access-token-for-tests')
  await expect(field).toHaveAttribute('type', 'password')
  await page.getByRole('button', { name: 'Token tonen' }).click()
  await expect(field).toHaveAttribute('type', 'text')

  await page.getByRole('button', { name: 'Verbinden' }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Verbonden met Bambu Lab.' })).toBeVisible()
  expect(sentToken).toEqual({ accessToken: 'fake-access-token-for-tests', region: 'global' })
  await expect(page.getByText('fake-access-token-for-tests')).toHaveCount(0)

  await expect(page.getByText('A1 · TEST-A1-0001')).toBeVisible()
  await page.getByRole('button', { name: 'Printer koppelen' }).click()
  await expect(page.getByText('Gekoppeld aan Workshop A1')).toBeVisible()
  expect(linkBody).toEqual({ createPrinter: true })
})

test('rejected token shows an inline error', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/printers': () => ({ status: 200, json: [] }),
    'GET /api/bambu/connection': () => ({ status: 200, json: disconnected }),
    'PUT /api/bambu/connection': () => ({ status: 400, json: { title: 'Validation', errors: { accessToken: ['rejected'] } } }),
  })

  await page.goto('/printers/bambu')
  await page.getByLabel('Toegangstoken').fill('fake-access-token-for-tests')
  await page.getByRole('button', { name: 'Verbinden' }).click()
  await expect(page.getByText('Bambu Lab accepteert dit token niet.')).toBeVisible()
})

test('expired token shows a reconnect state and disconnect asks for confirmation', async ({ page }) => {
  await seedSignedInSession(page)
  let deleted = false
  await mockApi(page, {
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
    'GET /api/bambu/connection': () => ({
      status: 200,
      json: { ...disconnected, connected: true, status: 'tokenExpired', devices: [{ ...device, printerId, printerName: 'Workshop A1' }] },
    }),
    'DELETE /api/bambu/connection': () => {
      deleted = true
      return { status: 204 }
    },
  })

  await page.goto('/printers/bambu')
  await expect(page.getByRole('heading', { name: 'Je token is verlopen' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Opnieuw verbinden' })).toBeVisible()

  await page.getByRole('button', { name: 'Bambu Lab ontkoppelen' }).click()
  await expect(page.getByRole('dialog')).toContainText('Bambu Lab ontkoppelen?')
  await page.getByRole('dialog').getByRole('button', { name: 'Annuleren' }).click()
  expect(deleted).toBe(false)

  await page.getByRole('button', { name: 'Bambu Lab ontkoppelen' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Ontkoppelen' }).click()
  await expect(page.getByRole('status').filter({ hasText: 'Losgekoppeld van Bambu Lab.' })).toBeVisible()
  expect(deleted).toBe(true)
})

test('printers list shows the live chip and progress, Home shows what is printing', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
    'GET /api/print-jobs': () => ({ status: 200, json: [] }),
    'GET /api/bambu/status': () => ({ status: 200, json: printingStatus }),
    'GET /api/inventory/alerts': () => ({ status: 200, json: [] }),
  })

  await page.goto('/printers')
  await expect(page.getByText('Bezig met printen')).toBeVisible()
  await expect(page.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '42')
  await expect(page.getByText('Nog 1 h 23 min')).toBeVisible()

  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Wat print ik nu' })).toBeVisible()
  await expect(page.getByText('Benchy')).toBeVisible()
  await expect(page.getByText('Laag 120 van 340')).toBeVisible()
  await expect(page.getByText('Alle materialen zitten boven hun grens.')).toBeVisible()
})

test('Home invites to connect when Bambu Lab is not connected', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/bambu/status': () => ({ status: 200, json: { connected: false, connectionStatus: null, printers: [] } }),
    'GET /api/inventory/alerts': () => ({ status: 200, json: [] }),
  })

  await page.goto('/')
  await expect(page.getByText('Verbind je Bambu Lab-account om hier live prints te zien.')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Bambu Lab verbinden' })).toBeVisible()
})
