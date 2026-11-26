import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const entry = {
  id: '33333333-3333-3333-3333-333333333333',
  createdAt: '2026-11-26T10:00:00Z',
  actorUserId: '44444444-4444-4444-4444-444444444444',
  actorEmail: 'owner@example.com',
  entityType: 'Workspace',
  entityId: '11111111-1111-1111-1111-111111111111',
  action: 'Updated',
  changes: { electricityPricePerKwh: { old: 0.3, new: 0.45 } },
}

test('the audit log lists who changed what, with before and after values', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/audit': () => ({ status: 200, json: { items: [entry], page: 1, pageSize: 25, total: 1 } }),
    'GET /api/audit/entity-types': () => ({ status: 200, json: ['Workspace'] }),
  })

  await page.goto('/audit')

  await expect(page.getByRole('heading', { level: 1, name: 'Auditlog' })).toBeVisible()
  await expect(page.getByText('owner@example.com')).toBeVisible()
  await expect(page.getByText('0.3 → 0.45')).toBeVisible()
  await expect(page.getByLabel('Filter op onderdeel')).toBeVisible()
})

test('the audit log shows an empty state', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/audit': () => ({ status: 200, json: { items: [], page: 1, pageSize: 25, total: 0 } }),
    'GET /api/audit/entity-types': () => ({ status: 200, json: [] }),
  })

  await page.goto('/audit')

  await expect(page.getByText('Er zijn nog geen wijzigingen vastgelegd.')).toBeVisible()
})

test('the audit log shows an error with retry', async ({ page }) => {
  await seedSignedInSession(page)
  let calls = 0
  await mockApi(page, {
    'GET /api/audit': () => {
      calls += 1
      return calls <= 2 ? { status: 500 } : { status: 200, json: { items: [entry], page: 1, pageSize: 25, total: 1 } }
    },
    'GET /api/audit/entity-types': () => ({ status: 200, json: ['Workspace'] }),
  })

  await page.goto('/audit')
  await expect(page.getByRole('alert')).toBeVisible()
  await page.getByRole('button', { name: /opnieuw/i }).click()

  await expect(page.getByText('owner@example.com')).toBeVisible()
})

test('the business tab links to the audit log', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/audit': () => ({ status: 200, json: { items: [], page: 1, pageSize: 25, total: 0 } }),
    'GET /api/audit/entity-types': () => ({ status: 200, json: [] }),
  })

  await page.goto('/business')
  await page.getByRole('link', { name: /Auditlog/ }).click()

  await expect(page).toHaveURL(/\/audit$/)
})
