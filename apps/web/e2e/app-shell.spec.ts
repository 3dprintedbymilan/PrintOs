import { expect, test } from '@playwright/test'

test('bottom navigation has four tabs and navigates between sections', async ({ page }) => {
  await page.goto('/')
  const nav = page.getByRole('navigation', { name: 'Hoofdnavigatie' })
  await expect(nav.getByRole('link')).toHaveCount(4)
  await expect(nav.getByRole('link', { name: 'Home' })).toHaveAttribute('aria-current', 'page')

  await nav.getByRole('link', { name: 'Prints' }).click()
  await expect(page).toHaveURL(/\/prints$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Prints' })).toBeVisible()
  await expect(nav.getByRole('link', { name: 'Prints' })).toHaveAttribute('aria-current', 'page')

  await nav.getByRole('link', { name: 'Voorraad' }).click()
  await expect(page).toHaveURL(/\/inventory$/)

  await nav.getByRole('link', { name: 'Bedrijf' }).click()
  await expect(page).toHaveURL(/\/business$/)
  await expect(page.getByText('Winstmarges')).toBeVisible()
})

for (const route of ['/', '/prints', '/inventory', '/business']) {
  test(`${route} has no horizontal scroll and content is not hidden behind the nav`, async ({ page }) => {
    await page.goto(route)
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
    expect(overflow).toBe(false)

    const navTop = await page.getByRole('navigation', { name: 'Hoofdnavigatie' }).evaluate(el => el.getBoundingClientRect().top)
    await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight))
    const lastBottom = await page.locator('main').evaluate((el) => {
      const last = el.lastElementChild ?? el
      return last.getBoundingClientRect().bottom
    })
    expect(lastBottom).toBeLessThanOrEqual(navTop + 1)
  })
}
