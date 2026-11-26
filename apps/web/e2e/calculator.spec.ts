import { expect, test } from '@playwright/test'
import { mockApi, seedSignedInSession } from './support'

const printer = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'A1 desk',
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
  colorHex: null,
  cost: 20,
  costPerGram: 0.02,
  initialWeightGrams: 1000,
  remainingWeightGrams: 850,
  isArchived: false,
}

function breakdownFor(body: { grams: number, marginPercent: number }) {
  const material = Math.round(body.grams * 1.05 * 0.02 * 100) / 100
  const total = Math.round((material + 0.06 + 2.14 + 5) * 100) / 100
  const price = Math.round((total / (1 - body.marginPercent / 100)) * 100) / 100
  return {
    currency: 'EUR',
    printerName: printer.name,
    materialLabel: 'PLA Basic - Jade White',
    lines: [
      { kind: 'material', label: 'material', quantity: body.grams * 1.05, unit: 'g', unitCost: 0.02, amount: material },
      { kind: 'electricity', label: 'electricity', quantity: 0.19, unit: 'kWh', unitCost: 0.3, amount: 0.06 },
      { kind: 'machine', label: 'machine', quantity: 2, unit: 'h', unitCost: 1.0678, amount: 2.14 },
      { kind: 'labour', label: 'labour', quantity: 0.25, unit: 'h', unitCost: 20, amount: 5 },
    ],
    totalCost: total,
    marginPercent: body.marginPercent,
    feePercent: 0,
    suggestedPrice: price,
    feeAmount: 0,
    profit: Math.round((price - total) * 100) / 100,
  }
}

test('the calculator shows the live breakdown, price and profit', async ({ page }) => {
  await seedSignedInSession(page)
  const requests: { grams: number, printMinutes: number, marginPercent: number }[] = []
  await mockApi(page, {
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
    'GET /api/spools': () => ({ status: 200, json: [spool] }),
    'POST /api/costing/calculate': (body) => {
      const request = body as { grams: number, printMinutes: number, marginPercent: number }
      requests.push(request)
      return { status: 200, json: breakdownFor(request) }
    },
  })

  await page.goto('/calculator')
  await expect(page.getByRole('heading', { level: 1, name: 'Kostencalculator' })).toBeVisible()
  await expect(page.getByText('Vul de gram en printtijd in')).toBeVisible()

  await page.getByLabel('Gebruikt filament (gram)').fill('100')
  await page.getByLabel('Printtijd (uren)').fill('2')
  await page.getByLabel('Arbeid (minuten)').fill('15')

  await expect(page.getByLabel('Kostenopbouw')).toContainText('Materiaal (incl. verlies)')
  await expect(page.getByLabel('Kostenopbouw')).toContainText('Elektriciteit')
  await expect(page.getByRole('term').filter({ hasText: 'Totale kosten' })).toBeVisible()
  await expect(page.getByRole('status').first()).toContainText('€ 15,50')
  await expect(page.getByRole('status').first()).toContainText('€ 6,20')
  expect(requests.at(-1)).toMatchObject({ grams: 100, printMinutes: 120, marginPercent: 40 })

  await page.getByLabel('Gewenste marge (%)').fill('50')
  await expect(page.getByRole('status').first()).toContainText('€ 18,60')
})

test('invalid margins and numbers show inline errors', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
    'GET /api/spools': () => ({ status: 200, json: [spool] }),
    'POST /api/costing/calculate': body => ({ status: 200, json: breakdownFor(body as { grams: number, marginPercent: number }) }),
  })

  await page.goto('/calculator')
  await page.getByLabel('Gebruikt filament (gram)').fill('abc')
  await expect(page.getByText('Gebruikt filament (gram) moet een getal zijn, bijvoorbeeld 12,5.')).toBeVisible()
  await page.getByLabel('Gebruikt filament (gram)').fill('100')
  await page.getByLabel('Printtijd (uren)').fill('1')
  await page.getByLabel('Gewenste marge (%)').fill('100')
  await expect(page.getByText('Gewenste marge (%) moet onder 100% blijven.')).toBeVisible()
  await expect(page.getByText('Vul de gram en printtijd in')).toBeVisible()
})

test('a calculation can be saved as an immutable snapshot', async ({ page }) => {
  await seedSignedInSession(page)
  let saved: unknown
  const snapshotBreakdown = breakdownFor({ grams: 100, marginPercent: 40 })
  await mockApi(page, {
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
    'GET /api/spools': () => ({ status: 200, json: [spool] }),
    'POST /api/costing/calculate': body => ({ status: 200, json: breakdownFor(body as { grams: number, marginPercent: number }) }),
    'POST /api/costing/snapshots': (body) => {
      saved = body
      return { status: 201, json: { id: 'snap-1', name: (body as { name: string }).name, createdAt: '2026-11-26T10:00:00Z', calculation: (body as { calculation: unknown }).calculation, breakdown: snapshotBreakdown } }
    },
    'GET /api/costing/snapshots/snap-1': () => ({
      status: 200,
      json: { id: 'snap-1', name: 'Vase', createdAt: '2026-11-26T10:00:00Z', calculation: {}, breakdown: snapshotBreakdown },
    }),
  })

  await page.goto('/calculator')
  await page.getByLabel('Gebruikt filament (gram)').fill('100')
  await page.getByLabel('Printtijd (uren)').fill('2')
  await page.getByLabel('Naam', { exact: true }).fill('Vase')
  await page.getByRole('button', { name: 'Berekening opslaan' }).click()

  await expect(page.getByText('Opgeslagen als "Vase".')).toBeVisible()
  expect(saved).toMatchObject({ name: 'Vase', calculation: { grams: 100, printMinutes: 120, printerId: printer.id, spoolId: spool.id } })

  await page.getByRole('link', { name: 'Bekijken' }).click()
  await expect(page).toHaveURL(/\/calculator\/snap-1$/)
  await expect(page.getByRole('heading', { name: 'Vase' })).toBeVisible()
  await expect(page.getByText('verandert nooit')).toBeVisible()
})

test('saving without valid input focuses the first invalid field', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/printers': () => ({ status: 200, json: [printer] }),
    'GET /api/spools': () => ({ status: 200, json: [spool] }),
  })

  await page.goto('/calculator')
  await page.getByRole('button', { name: 'Berekening opslaan' }).click()

  await expect(page.getByLabel('Gebruikt filament (gram)')).toBeFocused()
  await expect(page.getByText('Vul Gebruikt filament (gram) in.')).toBeVisible()
})

test('the calculator asks for a printer and spool first when none exist', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/printers': () => ({ status: 200, json: [] }),
    'GET /api/spools': () => ({ status: 200, json: [] }),
  })

  await page.goto('/calculator')

  await expect(page.getByText('Voeg minstens één printer')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Spoel toevoegen' })).toBeVisible()
})

test('saved calculations are listed and the business tab links to the calculator', async ({ page }) => {
  await seedSignedInSession(page)
  await mockApi(page, {
    'GET /api/costing/snapshots': () => ({
      status: 200,
      json: [{ id: 'snap-1', name: 'Vase', printerName: 'A1 desk', materialLabel: 'PLA Basic - Jade White', currency: 'EUR', totalCost: 9.3, suggestedPrice: 15.5, profit: 6.2, createdAt: '2026-11-26T10:00:00Z' }],
    }),
  })

  await page.goto('/business')
  await page.getByRole('link', { name: /Kostencalculator/ }).click()
  await expect(page).toHaveURL(/\/calculator$/)

  await page.goto('/calculator/saved')
  await expect(page.getByRole('link', { name: /Vase/ })).toContainText('€ 15,50')
})
