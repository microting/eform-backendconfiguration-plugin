import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import { BackendConfigurationChemicalsPage } from '../BackendConfigurationChemicals.page';
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';

/**
 * W5 "Ejendomsindstillinger - Kemi" (flutter-chemistry spec section 11): stock toggle, digest
 * recipients, fixed thresholds, digest preview. Shard e runs on a fresh database; the spec seeds one
 * property through the UI. The first user (the CI admin) is the only one Kemi is open to until GA.
 */
const property: PropertyCreateUpdate = {
  name: `Kemi-settings-${generateRandmString(6)}`, chrNumber: generateRandmString(5), address: generateRandmString(5), cvrNumber: '1111111',
};

async function openSettings(page: Page): Promise<BackendConfigurationChemicalsPage> {
  await page.goto('http://localhost:4200');
  await new LoginPage(page).login();
  const kemi = new BackendConfigurationChemicalsPage(page);
  await kemi.gotoSettings();
  await kemi.selectProperty(property.name);
  // Edits made before the stored settings arrive would be overwritten: wait for the form, then the preview.
  await kemi.waitForSettingsLoaded();
  await page.locator('#chemicalDigestPreview h4, #chemicalDigestPreviewNothing').first().waitFor({ state: 'visible', timeout: API_TIMEOUT });
  return kemi;
}

test.describe.serial('Kemi W5 - property settings', () => {
  test('seed: one property', async ({ page }) => {
    test.setTimeout(120000);
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    const properties = new BackendConfigurationPropertiesPage(page);
    await properties.goToProperties();
    await properties.createProperty(property);
  });

  test('the Kemi menu group opens Indstillinger', async ({ page }) => {
    test.setTimeout(120000);
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    const kemi = new BackendConfigurationChemicalsPage(page);

    await kemi.goToSettingsViaMenu();

    await expect(page).toHaveURL(/\/plugins\/backend-configuration-pn\/chemicals\/settings/, { timeout: UI_TIMEOUT });
    await expect(kemi.title()).toBeVisible({ timeout: UI_TIMEOUT });
  });

  test('an empty property previews "nothing to act on", names the next e-mail week and shows the fixed thresholds', async ({ page }) => {
    test.setTimeout(90000);
    await openSettings(page);

    await expect(page.locator('#chemicalDigestPreviewNothing')).toBeVisible({ timeout: API_TIMEOUT });
    await expect(page.locator('#chemicalDigestPreviewSubject')).toContainText(/\((uge|week) \d{1,2}\)/, { timeout: UI_TIMEOUT });
    await expect(page.locator('#chemicalSettingsThresholds tbody tr')).toHaveCount(4, { timeout: UI_TIMEOUT });
  });

  test('stock toggle and recipients persist; an invalid address is refused', async ({ page }) => {
    test.setTimeout(90000);
    const kemi = await openSettings(page);

    await page.locator('#chemicalSettingsStockToggle button').click({ timeout: UI_TIMEOUT });
    const input = page.locator('#chemicalSettingsRecipientInput');
    await input.fill('First@Example.com, nope', { timeout: UI_TIMEOUT });
    await input.press('Enter');
    await expect(page.locator('#chemicalSettingsRecipient-0')).toContainText('first@example.com', { timeout: UI_TIMEOUT });
    await expect(page.locator('#chemicalSettingsRecipientError')).toContainText('nope', { timeout: UI_TIMEOUT });
    await expect(input).toHaveAttribute('aria-invalid', 'true', { timeout: UI_TIMEOUT });
    await expect(input).toHaveAttribute('aria-describedby', 'chemicalSettingsRecipientError', { timeout: UI_TIMEOUT });
    await kemi.saveSettings();

    // E-10: a reload empties the store; pick the property again (bounded) before asserting.
    await page.reload();
    await kemi.settingsPage().waitFor({ state: 'visible', timeout: API_TIMEOUT });
    await kemi.selectProperty(property.name);
    await kemi.waitForSettingsLoaded();
    await expect(page.locator('#chemicalSettingsRecipient-0')).toContainText('first@example.com', { timeout: API_TIMEOUT });
    await expect(page.locator('#chemicalSettingsStockToggle button')).toHaveAttribute('aria-checked', 'true', { timeout: UI_TIMEOUT });
  });
});
