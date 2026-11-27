import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

function note(overrides: Record<string, unknown>) {
  return {
    id: 'n-1',
    type: 'PrintFailed',
    severity: 'danger',
    parameters: { job: 'Vase', printer: 'A1' },
    linkPath: '/print-jobs/job-1',
    createdAt: '2027-03-01T08:00:00Z',
    isRead: false,
    ...overrides,
  }
}

test('the badge shows the unread count and the centre lists, reads and deletes', async ({ page }) => {
  await seedSignedInSession(page)
  let items = [
    note({}),
    note({ id: 'n-2', type: 'LowStock', severity: 'warning', parameters: { material: 'PLA Wit', grams: '90' }, linkPath: '/inventory' }),
    note({ id: 'n-3', type: 'LicenseExpiring', severity: 'warning', parameters: { license: 'Dragon', date: '2027-03-10', state: 'expired' }, linkPath: '/licenses', isRead: true }),
  ]
  const unread = () => items.filter(item => !item.isRead).length
  await mockApi(page, {
    'GET /api/notifications/unread-count': () => ({ status: 200, json: { count: unread() } }),
    'GET /api/notifications': () => ({ status: 200, json: { items, unreadCount: unread(), hasMore: false } }),
    'POST /api/notifications/n-1/read': () => {
      items = items.map(item => (item.id === 'n-1' ? { ...item, isRead: true } : item))
      return { status: 200, json: items[0] }
    },
    'DELETE /api/notifications/n-2': () => {
      items = items.filter(item => item.id !== 'n-2')
      return { status: 204 }
    },
    'POST /api/notifications/read-all': () => {
      items = items.map(item => ({ ...item, isRead: true }))
      return { status: 200, json: { count: 0 } }
    },
  })

  await page.goto('/notifications')
  const main = page.getByRole('main')
  await expect(page.getByRole('link', { name: 'Meldingen, 2 ongelezen' }).first()).toBeAttached()
  await expect(main.getByText('De print Vase op A1 is mislukt.')).toBeVisible()
  await expect(main.getByText('PLA Wit is bijna op: nog 90 g.')).toBeVisible()
  await expect(main.getByText('De licentie Dragon is verlopen op', { exact: false })).toBeVisible()

  await main.getByRole('button', { name: 'Gelezen', exact: true }).first().click()
  await expect(main.getByRole('status').filter({ hasText: 'Als gelezen gemarkeerd' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Meldingen, 1 ongelezen' }).first()).toBeAttached()

  await main.getByRole('button', { name: 'Verwijderen' }).nth(1).click()
  await expect(main.getByText('PLA Wit is bijna op')).toHaveCount(0)

  const filtered = page.waitForRequest(request => request.url().includes('/api/notifications?') && request.url().includes('unreadOnly=true'))
  await main.getByRole('radio', { name: 'Ongelezen' }).click()
  await filtered

  await expect(main.getByRole('button', { name: 'Alles als gelezen markeren' })).toBeDisabled()
})

test('the centre shows an empty state and an error with retry', async ({ page }) => {
  await seedSignedInSession(page)
  let calls = 0
  await mockApi(page, {
    'GET /api/notifications/unread-count': () => ({ status: 200, json: { count: 0 } }),
    'GET /api/notifications': () => {
      calls += 1
      return calls <= 2 ? { status: 500 } : { status: 200, json: { items: [], unreadCount: 0, hasMore: false } }
    },
  })

  await page.goto('/notifications')
  const main = page.getByRole('main')
  await main.getByRole('button', { name: /opnieuw/i }).click()
  await expect(main.getByText('Nog geen meldingen', { exact: false })).toBeVisible()
})

test('preferences are saved per type with quiet hours and the browser offset', async ({ page }) => {
  await seedSignedInSession(page)
  let body: Record<string, unknown> | null = null
  const types = ['LowStock', 'ReorderSoon', 'PrintFailed', 'PrintFinished', 'PrinterOffline', 'MaintenanceDue', 'LicenseExpiring', 'OrderDueSoon', 'OrderOverdue']
  const response = (quiet = false) => ({
    types: types.map(type => ({ type, inApp: true, email: false })),
    quietHours: { enabled: quiet, start: '22:00', end: '07:00', utcOffsetMinutes: 0 },
    channels: { emailAvailable: true, realtime: true, push: false },
  })
  await mockApi(page, {
    'GET /api/notifications/unread-count': () => ({ status: 200, json: { count: 0 } }),
    'GET /api/notifications/preferences': () => ({ status: 200, json: response() }),
    'PUT /api/notifications/preferences': (payload) => {
      body = payload as Record<string, unknown>
      return { status: 200, json: response(true) }
    },
  })

  await page.goto('/notification-settings')
  const main = page.getByRole('main')
  await expect(main.getByText('E-mail: de server kan e-mail versturen.')).toBeVisible()
  const failed = main.getByRole('group', { name: 'Print mislukt' })
  await failed.getByLabel('E-mail').check()
  await main.getByLabel('Stille uren gebruiken').check()
  await main.getByLabel('Van').fill('21:30')
  await main.getByRole('button', { name: 'Voorkeuren opslaan' }).click()
  await expect(main.getByRole('status').filter({ hasText: 'Voorkeuren opgeslagen' })).toBeVisible()
  expect(body).toMatchObject({ quietHours: { enabled: true, start: '21:30', end: '07:00' } })
  const sent = body as unknown as { types: { type: string, email: boolean }[] }
  expect(sent.types.find(entry => entry.type === 'PrintFailed')?.email).toBe(true)
  expect(sent.types.find(entry => entry.type === 'LowStock')?.email).toBe(false)
})

test('email is disabled with an explanation when the server has no SMTP', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/notifications/unread-count': () => ({ status: 200, json: { count: 0 } }),
    'GET /api/notifications/preferences': () => ({
      status: 200,
      json: {
        types: [{ type: 'PrintFailed', inApp: true, email: false }],
        quietHours: { enabled: false, start: '22:00', end: '07:00', utcOffsetMinutes: 0 },
        channels: { emailAvailable: false, realtime: false, push: false },
      },
    }),
  })

  await page.goto('/notification-settings')
  const main = page.getByRole('main')
  await expect(main.getByText('E-mail: niet ingesteld op deze server.')).toBeVisible()
  await expect(main.getByRole('group', { name: 'Print mislukt' }).getByLabel('E-mail')).toBeDisabled()
  await expect(main.getByText('de app controleert elke 30 seconden', { exact: false })).toBeVisible()
})

test('a quiet hour time must be valid', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/notifications/unread-count': () => ({ status: 200, json: { count: 0 } }),
    'GET /api/notifications/preferences': () => ({
      status: 200,
      json: {
        types: [{ type: 'PrintFailed', inApp: true, email: false }],
        quietHours: { enabled: true, start: '22:00', end: '07:00', utcOffsetMinutes: 0 },
        channels: { emailAvailable: true, realtime: false, push: false },
      },
    }),
  })

  await page.goto('/notification-settings')
  const main = page.getByRole('main')
  await main.getByLabel('Van').fill('')
  await main.getByRole('button', { name: 'Voorkeuren opslaan' }).click()
  await expect(main.getByText('Vul een tijd in als uu:mm.')).toBeVisible()
})

test('the printer page saves a maintenance interval and records maintenance done', async ({ page }) => {
  await seedSignedInSession(page)
  const printer = {
    id: 'pr-1', name: 'A1 #1', model: 'Bambu Lab A1', buildVolumeXMillimetres: 256, buildVolumeYMillimetres: 256, buildVolumeZMillimetres: 256,
    nozzleDiameterMillimetres: 0.4, powerDrawWatts: 95, purchaseCost: 339, lifetimeHours: 5000, depreciationPerHour: 0.07, isArchived: false,
  }
  let saved: unknown = null
  let maintenance = { everyHours: null as number | null, everyDays: null as number | null, lastMaintenanceAt: null, since: '2027-01-01T00:00:00Z', printMinutesSince: 300, daysSince: 40, due: false }
  await mockApi(page, {
    'GET /api/notifications/unread-count': () => ({ status: 200, json: { count: 0 } }),
    'GET /api/printers/pr-1': () => ({ status: 200, json: printer }),
    'GET /api/printers/pr-1/maintenance': () => ({ status: 200, json: maintenance }),
    'PUT /api/printers/pr-1/maintenance': (payload) => {
      saved = payload
      maintenance = { ...maintenance, ...(payload as object), due: true }
      return { status: 200, json: maintenance }
    },
    'POST /api/printers/pr-1/maintenance/done': () => {
      maintenance = { ...maintenance, printMinutesSince: 0, due: false }
      return { status: 200, json: maintenance }
    },
  })

  await page.goto('/printers/pr-1')
  const card = page.getByRole('main').locator('section', { has: page.getByRole('heading', { name: 'Onderhoud' }) })
  await card.getByLabel('Elke (printuren)').fill('50')
  await card.getByRole('button', { name: 'Opslaan' }).click()
  await expect(card.getByText('Onderhoudsinstelling opgeslagen.')).toBeVisible()
  expect(saved).toEqual({ everyHours: 50, everyDays: null })
  await expect(card.getByText('Onderhoud nodig')).toBeVisible()

  await card.getByRole('button', { name: 'Onderhoud gedaan' }).click()
  await expect(card.getByText('Onderhoud genoteerd', { exact: false })).toBeVisible()
  await expect(card.getByText('Onderhoud nodig')).toHaveCount(0)

  await card.getByLabel('Elke (printuren)').fill('abc')
  await card.getByRole('button', { name: 'Opslaan' }).click()
  await expect(card.getByText('Geef een heel aantal uren', { exact: false })).toBeVisible()
})
