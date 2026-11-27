import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const assessment = (status: string, extra: Record<string, unknown> = {}) => ({
  status,
  allowsCommercialSale: status === 'Valid' || status === 'ExpiringSoon',
  attributionRequired: false,
  daysUntilExpiry: null,
  unitsUsed: null,
  unitsRemaining: null,
  limitNearlyReached: false,
  ...extra,
})

const license = (id: string, name: string, type: string, status: string, extra: Record<string, unknown> = {}, assessmentExtra: Record<string, unknown> = {}) => ({
  id,
  name,
  source: 'Printables',
  type,
  attributionRequired: false,
  attributionText: null,
  unitsLimit: null,
  validFrom: null,
  validUntil: null,
  notes: null,
  proofUrl: null,
  hasProofFile: false,
  proofFileName: null,
  modelCount: 0,
  assessment: assessment(status, assessmentExtra),
  createdAt: '2026-11-20T10:00:00Z',
  updatedAt: '2026-11-20T10:00:00Z',
  ...extra,
})

const valid = license('aaaaaaaa-0000-0000-0000-000000000001', 'Dragon commercial', 'Commercial', 'Valid')
const soon = license('aaaaaaaa-0000-0000-0000-000000000002', 'Vase licence', 'Commercial', 'ExpiringSoon', { validUntil: '2026-12-10' }, { daysUntilExpiry: 12 })
const expired = license('aaaaaaaa-0000-0000-0000-000000000003', 'Old pack', 'Commercial', 'Expired', { validUntil: '2026-10-01' }, { daysUntilExpiry: -5 })
const personal = license('aaaaaaaa-0000-0000-0000-000000000004', 'Hobby only', 'Personal', 'PersonalOnly')

test('the business tab links to the licenses screen, which lists status chips', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { 'GET /api/licenses': () => ({ status: 200, json: [valid, soon, expired, personal] }) })

  await page.goto('/business')
  await page.locator('main').getByRole('link', { name: /Licenties/ }).click()

  await expect(page).toHaveURL(/\/licenses$/)
  await expect(page.getByRole('link', { name: /Dragon commercial/ })).toContainText('Geldig')
  await expect(page.getByRole('link', { name: /Vase licence/ })).toContainText('Verloopt binnenkort')
  await expect(page.getByRole('link', { name: /Vase licence/ })).toContainText('Verloopt over 12 dagen')
  await expect(page.getByRole('link', { name: /Old pack/ })).toContainText('Verlopen')
  await expect(page.getByRole('link', { name: /Hobby only/ })).toContainText('Alleen persoonlijk')
  await expect(page.getByRole('status')).toContainText('2 licenties hebben aandacht nodig')
})

test('an empty list invites the first license', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, { 'GET /api/licenses': () => ({ status: 200, json: [] }) })

  await page.goto('/licenses')

  await expect(page.getByText('Nog geen licenties.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Licentie toevoegen' })).toBeVisible()
})

test('adding a license validates inline, then posts and opens the detail', async ({ page }) => {
  let posted: Record<string, unknown> | null = null
  await seedSignedInSession(page)
  await mockApi(page, {
    'POST /api/licenses': (body) => {
      posted = body as Record<string, unknown>
      return { status: 201, json: valid }
    },
    [`GET /api/licenses/${valid.id}`]: () => ({ status: 200, json: { license: valid, models: [] } }),
  })

  await page.goto('/licenses/new')
  await page.getByRole('button', { name: 'Licentie opslaan' }).click()

  await expect(page.getByText('Naam is verplicht (maximaal 100 tekens).')).toBeVisible()
  await expect(page.getByLabel('Naam', { exact: true })).toBeFocused()

  await page.getByLabel('Naam', { exact: true }).fill('Dragon commercial')
  await page.getByLabel('Type licentie').selectOption('CommercialWithLimits')
  await page.getByLabel('Maximum aantal stuks').fill('250')
  await page.getByLabel('Naamsvermelding verplicht').check()
  await page.getByLabel('Tekst voor naamsvermelding').fill('By Creator')
  await page.getByRole('button', { name: 'Licentie opslaan' }).click()

  await expect(page).toHaveURL(new RegExp(`/licenses/${valid.id}$`))
  expect(posted).toMatchObject({ name: 'Dragon commercial', type: 'CommercialWithLimits', unitsLimit: 250, attributionRequired: true, attributionText: 'By Creator' })
})

test('the detail shows the verdict, proof and linked models', async ({ page }) => {
  const detailed = license(valid.id, 'Dragon commercial', 'CommercialWithLimits', 'Valid', {
    unitsLimit: 100,
    attributionRequired: true,
    attributionText: 'By Creator',
    proofUrl: 'https://example.com/terms',
    hasProofFile: true,
    proofFileName: 'terms.pdf',
    modelCount: 1,
  }, { unitsUsed: 95, unitsRemaining: 5, limitNearlyReached: true })
  await seedSignedInSession(page)
  await mockApi(page, {
    [`GET /api/licenses/${valid.id}`]: () => ({ status: 200, json: { license: detailed, models: [{ id: 'm1', name: 'Dragon' }] } }),
  })

  await page.goto(`/licenses/${valid.id}`)

  await expect(page.getByText('Verkoop is toegestaan')).toBeVisible()
  await expect(page.getByText('95 van 100 stuks gebruikt')).toBeVisible()
  await expect(page.getByText('Bijna aan je limiet: nog 5 stuks.')).toBeVisible()
  await expect(page.getByText('terms.pdf')).toBeVisible()
  await expect(page.getByRole('link', { name: /Dragon/ })).toBeVisible()
})

test('the model detail page shows the license chip and links another license', async ({ page }) => {
  const version = { id: 'v1', versionNumber: 1, fileName: 'dragon.stl', format: 'stl', sizeBytes: 1000, notes: null, printProfile: { layerHeightMillimetres: null, infillPercent: null, supportsRequired: null, materialType: null, printProfileNotes: null }, createdAt: '2026-11-20T10:00:00Z' }
  const model = { id: '55555555-5555-5555-5555-555555555555', name: 'Dragon', description: null, sourceUrl: null, tags: [], thumbnailUrl: null, versions: [version], createdAt: '2026-11-20T10:00:00Z', updatedAt: '2026-11-20T10:00:00Z', licenseId: null, license: null }
  let linked: unknown = null
  await seedSignedInSession(page)
  await page.route('**/api/model-files/**', route => route.fulfill({ status: 404, headers: { 'access-control-allow-origin': '*' } }))
  await mockApi(page, {
    [`GET /api/models/${model.id}`]: () => ({ status: 200, json: model }),
    'GET /api/licenses': () => ({ status: 200, json: [valid, personal] }),
    [`PUT /api/models/${model.id}/license`]: (body) => {
      linked = body
      return { status: 200, json: { ...model, licenseId: personal.id, license: { id: personal.id, name: personal.name, source: 'Printables', type: 'Personal', attributionRequired: false, attributionText: null, validUntil: null, assessment: personal.assessment } } }
    },
  })

  await page.goto(`/models/${model.id}`)
  await expect(page.getByRole('heading', { name: 'Licentie' })).toBeVisible()
  await expect(page.getByText('Geen licentie').first()).toBeVisible()

  await page.getByLabel('Licentie van dit model').selectOption({ label: 'Hobby only (Alleen persoonlijk)' })

  await expect(page.getByRole('status').filter({ hasText: 'Licentie gekoppeld.' })).toBeVisible()
  await expect(page.getByText('Niet voor verkoop geschikt')).toBeVisible()
  expect(linked).toEqual({ licenseId: personal.id })
})
