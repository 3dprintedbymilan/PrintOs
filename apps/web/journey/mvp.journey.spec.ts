import { fileURLToPath } from 'node:url'
import { expect, test } from '@playwright/test'

const stlFixture = fileURLToPath(new URL('./fixtures/tetrahedron.stl', import.meta.url))
const password = 'Journey-Pass-9'

test('the MVP journey works end to end against the real API', async ({ page }, testInfo) => {
  const suffix = `${testInfo.project.name}-${Date.now()}`
  const email = `journey-${suffix}@example.com`
  const business = `Journey ${suffix}`
  const printerName = `A1 journey ${testInfo.project.name}`
  const productName = `Vaas ${testInfo.project.name}`

  await test.step('sign up creates the workspace and lands on the dashboard', async () => {
    await page.goto('/sign-in')
    await page.getByRole('radio', { name: 'Account maken' }).check({ force: true })
    await page.getByLabel('Naam van je bedrijf').fill(business)
    await page.getByLabel('E-mailadres').fill(email)
    await page.getByLabel('Wachtwoord', { exact: true }).fill(password)
    await page.getByRole('button', { name: 'Account maken', exact: true }).last().click()

    await expect(page).toHaveURL(/\/$/)
    await expect(page.getByRole('heading', { level: 1, name: 'Productieoverzicht' })).toBeVisible()
    await expect(page.locator('html')).toHaveAttribute('lang', 'nl-BE')
  })

  await test.step('settings are saved and survive a reload', async () => {
    await page.goto('/business')
    await page.getByRole('link', { name: /Instellingen/ }).click()
    await expect(page.getByLabel('Naam van je bedrijf').first()).toHaveValue(business)

    await page.getByLabel('Elektriciteitsprijs (per kWh)').fill('0,25')
    await page.getByLabel('Uurtarief arbeid').fill('12')
    await page.getByLabel('Uurtarief machine').fill('0,5')
    await page.getByLabel('Afvalmarge (%)').fill('5')
    await page.getByRole('button', { name: 'Instellingen opslaan' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Instellingen opgeslagen.' })).toBeVisible()

    await page.reload()
    await expect(page.getByLabel('Afvalmarge (%)')).toHaveValue('5')
    await expect(page.getByLabel('Elektriciteitsprijs (per kWh)')).toHaveValue('0,25')
  })

  await test.step('a printer is added with the Bambu Lab A1 defaults', async () => {
    await page.goto('/printers/new')
    await expect(page.getByLabel('Model')).toHaveValue('Bambu Lab A1')
    await page.getByLabel('Naam', { exact: true }).fill(printerName)
    await page.getByRole('button', { name: 'Printer opslaan' }).click()

    await expect(page).toHaveURL(/\/printers$/)
    await expect(page.getByRole('link', { name: new RegExp(printerName) })).toBeVisible()
  })

  await test.step('a material and spool are added', async () => {
    await page.goto('/inventory/new')
    await page.getByLabel('Materiaalnaam').fill('PLA Basic')
    await page.getByLabel('Soort materiaal').fill('PLA')
    await page.getByLabel('Kleur', { exact: true }).fill('Jade White')
    await page.getByLabel('Aankoopprijs').fill('20')
    await page.getByRole('button', { name: 'Spoel opslaan' }).click()

    await expect(page).toHaveURL(/\/inventory\/[0-9a-f-]{36}$/)
    await expect(page.getByText('1.000 g over (100%)').or(page.getByText('1000 g over (100%)'))).toBeVisible()
  })

  await test.step('adjusting the stock writes a traceable movement', async () => {
    await page.getByRole('radio', { name: 'Verwijderen' }).check({ force: true })
    await page.getByLabel('Hoeveelheid (gram)').fill('800')
    await page.getByLabel('Reden').fill('Mislukte print')
    await page.getByRole('button', { name: 'Aanpassing opslaan' }).click()

    await expect(page.getByRole('status').filter({ hasText: 'Voorraad aangepast.' })).toBeVisible()
    await expect(page.getByText('200 g over (20%)')).toBeVisible()
    await expect(page.getByRole('region', { name: 'Bewegingen' }).getByText('Mislukte print')).toBeVisible()
  })

  await test.step('a low-stock threshold raises an alert on the dashboard', async () => {
    await page.goto('/inventory/materials')
    await page.getByLabel('Grens lage voorraad (gram)').fill('300')
    await page.getByLabel('Grens bijbestellen (gram)').fill('500')
    await page.getByRole('button', { name: 'Grenzen opslaan' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Grenzen opgeslagen.' })).toBeVisible()

    await page.goto('/')
    const alerts = page.getByRole('list').filter({ hasText: 'PLA Basic' })
    await expect(alerts.getByRole('listitem').first()).toContainText('Lage voorraad')
    await expect(alerts.getByRole('listitem').first()).toContainText('200 g over, grens 300 g')
  })

  await test.step('the cost calculator prices a print and saves an immutable snapshot', async () => {
    await page.goto('/calculator')
    await page.getByLabel('Gebruikt filament (gram)').fill('50')
    await page.getByLabel('Printtijd (uren)').fill('1')
    await expect(page.getByLabel('Kostenopbouw')).toContainText('Materiaal (incl. verlies)')
    await expect(page.getByLabel('Kostenopbouw')).toContainText('1,05')
    await expect(page.getByRole('status').first()).toContainText('€')

    await page.getByLabel('Naam', { exact: true }).fill('Journey berekening')
    await page.getByRole('button', { name: 'Berekening opslaan' }).click()
    await expect(page.getByText('Opgeslagen als "Journey berekening".')).toBeVisible()

    await page.goto('/calculator/saved')
    await expect(page.getByRole('link', { name: /Journey berekening/ })).toBeVisible()
  })

  await test.step('a model is uploaded from an STL file', async () => {
    await page.goto('/models/new')
    await page.getByLabel('Modelbestand').setInputFiles(stlFixture)
    await page.getByLabel('Naam').fill('Tetraëder')
    await page.getByLabel('Tags', { exact: true }).fill('test, journey')
    await page.getByRole('button', { name: 'Uploaden' }).click()

    await expect(page).toHaveURL(/\/models\/[0-9a-f-]{36}$/)
    await expect(page.getByRole('heading', { name: 'Tetraëder' })).toBeVisible()
  })

  await test.step('a personal-use licence is added and linked to the model', async () => {
    await page.goto('/licenses/new')
    await page.getByLabel('Naam', { exact: true }).fill('Journey hobbylicentie')
    await page.getByLabel('Type licentie').selectOption('Personal')
    await page.getByRole('button', { name: 'Licentie opslaan' }).click()
    await expect(page).toHaveURL(/\/licenses\/[0-9a-f-]{36}$/)
    await expect(page.getByText('Niet voor verkoop geschikt').or(page.getByText('Alleen persoonlijk')).first()).toBeVisible()

    await page.goto('/prints')
    await page.getByRole('radio', { name: 'Modellen' }).check()
    await page.getByRole('link', { name: /Tetraëder/ }).click()
    await page.getByLabel('Licentie van dit model').selectOption({ label: 'Journey hobbylicentie (Alleen persoonlijk)' })
    await expect(page.getByRole('status').filter({ hasText: 'Licentie gekoppeld.' })).toBeVisible()
  })

  await test.step('a product variant gets a bill of materials, a cost and a commercial-rights warning', async () => {
    await page.goto('/products/new')
    await page.getByLabel('Naam', { exact: true }).fill(productName)
    await page.getByRole('button', { name: 'Product aanmaken' }).click()
    await expect(page).toHaveURL(/\/products\/[0-9a-f-]{36}$/)

    await page.getByRole('button', { name: 'Variant toevoegen' }).click()
    const variantDialog = page.getByRole('dialog')
    await variantDialog.getByLabel('Variant', { exact: true }).fill('Groot')
    await variantDialog.getByLabel('Verkoopprijs').fill('15')
    await variantDialog.getByRole('button', { name: 'Variant opslaan' }).click()
    await expect(variantDialog).toBeHidden()

    const card = page.getByLabel('Groot', { exact: true })
    await card.getByRole('button', { name: 'Regel toevoegen' }).click()
    const lineDialog = page.getByRole('dialog')
    await lineDialog.getByLabel('Soort regel').selectOption('filament')
    await lineDialog.getByRole('combobox', { name: 'Spoel' }).selectOption({ index: 1 })
    await lineDialog.getByLabel('Gram', { exact: true }).fill('100')
    await lineDialog.getByRole('button', { name: 'Regel opslaan' }).click()
    await expect(lineDialog).toBeHidden()
    await expect(card).toContainText('2,10')

    await card.getByRole('button', { name: 'Regel toevoegen' }).click()
    await lineDialog.getByLabel('Soort regel').selectOption('print')
    await lineDialog.getByRole('combobox', { name: 'Printer' }).selectOption({ index: 1 })
    await lineDialog.getByLabel('Printtijd (minuten)').fill('90')
    await lineDialog.getByLabel('Model uit de bibliotheek (optioneel)').selectOption({ label: 'Tetraëder' })
    await lineDialog.getByRole('button', { name: 'Regel opslaan' }).click()
    await expect(lineDialog).toBeHidden()

    await expect(card.getByRole('alert')).toContainText('Geen geldige commerciële licentie')
    await expect(card.getByRole('button', { name: 'Toch markeren als klaar voor verkoop' })).toBeVisible()
    await expect(card).toContainText('Marge')

    await card.getByRole('button', { name: 'Kostprijs vastleggen' }).click()
    await expect(card.getByRole('status')).toContainText('Kostprijs vastgelegd')
  })

  await test.step('a manual print job consumes filament and shows in the stock ledger', async () => {
    await page.goto('/print-jobs/new')
    await page.getByLabel('Naam').fill('Benchy journey')
    await page.getByLabel('Status', { exact: true }).selectOption('Done')
    await page.getByRole('button', { name: 'Spoel toevoegen' }).click()
    await page.getByLabel('Spoel', { exact: true }).selectOption({ index: 1 })
    await page.getByLabel('Gebruikt (gram)').fill('20')
    await page.getByRole('button', { name: 'Print opslaan' }).click()

    await expect(page).toHaveURL(/\/print-jobs\/[0-9a-f-]{36}$/)
    await expect(page.getByText('Klaar').first()).toBeVisible()

    await page.goto('/inventory')
    await page.getByRole('link', { name: /Jade White/ }).click()
    await expect(page.getByText('180 g over (18%)')).toBeVisible()
    const ledger = page.getByRole('region', { name: 'Bewegingen' })
    await expect(ledger).toContainText('Gebruikt in print')
    await expect(ledger).toContainText('Saldo 180 g')
  })

  await test.step('the dashboard numbers reflect the finished print', async () => {
    await page.goto('/')
    const kpis = page.getByRole('group', { name: 'Kerncijfers' })
    await expect(kpis).toContainText('Prints klaar')
    await expect(kpis).toContainText('1')
    await expect(kpis).toContainText('Filament verbruikt')
    await expect(page.getByRole('link', { name: /Benchy journey/ })).toBeVisible()
  })

  await test.step('the Bambu Lab connection screen is reachable and connects through the fake client', async () => {
    await page.goto('/printers/bambu')
    await expect(page.getByRole('heading', { name: 'Verbind je Bambu Lab-account' })).toBeVisible()
    await page.getByLabel('Toegangstoken').fill('journey-fake-token')
    await page.getByRole('button', { name: 'Verbinden' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Verbonden met Bambu Lab.' })).toBeVisible()
    await expect(page.getByText('A1 · JOURNEY-A1-0001')).toBeVisible()
  })

  await test.step('the account data export downloads a ZIP', async () => {
    await page.goto('/settings')
    await page.getByLabel('Je wachtwoord').first().fill(password)
    const download = page.waitForEvent('download')
    await page.getByRole('button', { name: 'Mijn gegevens exporteren' }).click()
    expect((await download).suggestedFilename()).toMatch(/^printos-export-\d{8}\.zip$/)
    await expect(page.getByRole('status').filter({ hasText: 'Download gestart' })).toBeVisible()
  })

  await test.step('deleting the account removes everything and blocks the next sign-in', async () => {
    await page.getByRole('button', { name: 'Mijn account verwijderen' }).last().click()
    const dialog = page.getByRole('dialog', { name: 'Account verwijderen?' })
    await dialog.getByLabel('Je wachtwoord').fill(password)
    await dialog.getByLabel(/Typ de naam van de werkruimte/).fill(business)
    await dialog.getByRole('button', { name: 'Alles definitief verwijderen' }).click()

    await expect(page).toHaveURL(/\/sign-in/)
    await expect(page.getByRole('status').filter({ hasText: 'zijn verwijderd' })).toBeVisible()

    await page.getByLabel('E-mailadres').fill(email)
    await page.getByLabel('Wachtwoord', { exact: true }).fill(password)
    await page.getByRole('button', { name: 'Aanmelden', exact: true }).last().click()
    await expect(page.getByText('Dit e-mailadres en wachtwoord kloppen niet.')).toBeVisible()
  })
})
