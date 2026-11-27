import { expect, test } from '@playwright/test'
import { seedSignedInSession } from './support'

test.beforeEach(async ({ page }) => {
  await seedSignedInSession(page)
})

const primary = (page: import('@playwright/test').Page) => page.getByRole('navigation', { name: 'Hoofdnavigatie' })
const more = (page: import('@playwright/test').Page) => page.getByRole('navigation', { name: 'Meer secties' })

test.describe('phone', () => {
  test.use({ viewport: { width: 375, height: 812 } })

  test('keeps the bottom bar and hides the secondary navigation', async ({ page }) => {
    await page.goto('/')
    await expect(primary(page).getByRole('link')).toHaveCount(4)
    expect(await primary(page).evaluate(el => getComputedStyle(el).position)).toBe('fixed')
    await expect(more(page)).toBeHidden()
    await expect(page.getByRole('button', { name: /Menu (in|uit)klappen/ })).toBeHidden()
  })

  test('Business stays highlighted on nested routes', async ({ page }) => {
    await page.goto('/printers')
    await expect(primary(page).getByRole('link', { name: 'Bedrijf' })).toHaveAttribute('aria-current', 'page')
  })
})

test.describe('tablet', () => {
  test.use({ viewport: { width: 768, height: 1024 } })

  test('shows a rail that expands over the content and collapses with Escape', async ({ page }) => {
    await page.goto('/')
    const nav = primary(page)
    await expect(nav.getByRole('link')).toHaveCount(4)
    await expect.poll(async () => (await page.locator('aside').boundingBox())!.width).toBeLessThan(100)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)

    const toggle = page.getByRole('button', { name: 'Menu uitklappen' })
    await expect(toggle).toHaveAttribute('aria-expanded', 'false')
    await toggle.click()
    await expect(page.getByRole('button', { name: 'Menu inklappen' })).toHaveAttribute('aria-expanded', 'true')
    await expect(more(page).getByRole('link', { name: 'Printers' })).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('button', { name: 'Menu uitklappen' })).toBeFocused()
  })

  test('rail secondary links navigate and collapse the overlay', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('button', { name: 'Menu uitklappen' }).click()
    await more(page).getByRole('link', { name: 'Printers', exact: true }).click()
    await expect(page).toHaveURL(/\/printers$/)
    await expect(page.getByRole('button', { name: 'Menu uitklappen' })).toBeVisible()
    await expect(more(page).getByRole('link', { name: 'Printers', exact: true })).toHaveAttribute('aria-current', 'page')
  })
})

test.describe('desktop', () => {
  test.use({ viewport: { width: 1440, height: 900 } })

  test('shows a full sidebar with the secondary destinations and active states', async ({ page }) => {
    await page.goto('/printers/bambu')
    await expect(primary(page).getByRole('link')).toHaveCount(4)
    await expect(primary(page).getByRole('link', { name: 'Bedrijf' })).toHaveAttribute('aria-current', 'page')
    const bambu = more(page).getByRole('link', { name: 'Bambu Lab' })
    await expect(bambu).toHaveAttribute('aria-current', 'page')
    await expect(more(page).getByRole('link', { name: 'Printers', exact: true })).not.toHaveAttribute('aria-current', 'page')
    for (const name of ['Modellen', 'Licenties', 'Producten', 'Kostencalculator', 'Instellingen', 'Auditlog']) {
      await expect(more(page).getByRole('link', { name })).toBeVisible()
    }
    const content = await page.locator('main').boundingBox()
    expect(content!.width).toBeLessThanOrEqual(1041)
    expect(content!.x).toBeGreaterThan(240)
  })

  test('the sidebar can be collapsed and the choice is remembered', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('button', { name: 'Menu inklappen' }).click()
    await expect.poll(async () => (await page.locator('aside').boundingBox())!.width).toBeLessThan(100)
    await page.reload()
    await expect(page.getByRole('button', { name: 'Menu uitklappen' })).toBeVisible()
  })

  test('skip link is the first tab stop and moves focus to the content', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    await page.keyboard.press('Tab')
    const skip = page.getByRole('link', { name: 'Naar de inhoud' })
    await expect(skip).toBeFocused()
    await page.keyboard.press('Enter')
    await expect(page.getByRole('heading', { level: 1 })).toBeFocused()
  })

  test('primary links are reachable by keyboard in order', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    await page.keyboard.press('Tab')
    await page.keyboard.press('Tab')
    await expect(primary(page).getByRole('link', { name: 'Home' })).toBeFocused()
    await page.keyboard.press('Tab')
    await expect(primary(page).getByRole('link', { name: 'Prints' })).toBeFocused()
  })
})
