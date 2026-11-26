import { expect, test } from '@playwright/test'
import { seedSignedInSession } from './support'

test.beforeEach(async ({ page }) => {
  await seedSignedInSession(page)
})

test('home page renders the dashboard title', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1, name: 'Productieoverzicht' })).toBeVisible()
})

test('page fits the viewport without horizontal scroll', async ({ page }) => {
  await page.goto('/')
  const hasHorizontalScroll = await page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
  )
  expect(hasHorizontalScroll).toBe(false)
})

test('web manifest is served and installable', async ({ request }) => {
  const response = await request.get('/manifest.webmanifest')
  expect(response.ok()).toBe(true)
  const manifest = await response.json()
  expect(manifest.name).toBe('PrintOS')
  expect(manifest.display).toBe('standalone')
})
