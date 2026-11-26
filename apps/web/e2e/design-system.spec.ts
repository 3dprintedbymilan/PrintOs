import { expect, test } from '@playwright/test'

test('theme switch changes the theme and keeps it after reload', async ({ page }) => {
  await page.goto('/design')
  const html = page.locator('html')

  await page.getByRole('radio', { name: 'Licht' }).check({ force: true })
  await expect(html).toHaveAttribute('data-theme', 'light')

  await page.reload()
  await expect(html).toHaveAttribute('data-theme', 'light')

  await page.getByRole('radio', { name: 'Donker' }).check({ force: true })
  await expect(html).toHaveAttribute('data-theme', 'dark')
})

test('dialog opens, is modal and closes with the close button', async ({ page }) => {
  await page.goto('/design')
  await page.getByRole('button', { name: 'Open dialog' }).click()
  const dialog = page.getByRole('dialog', { name: 'Adjust stock' })
  await expect(dialog).toBeVisible()
  await dialog.getByRole('button', { name: 'Sluiten' }).click()
  await expect(dialog).toBeHidden()
})

test('showcase has no horizontal scroll', async ({ page }) => {
  await page.goto('/design')
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(overflow).toBe(false)
})
