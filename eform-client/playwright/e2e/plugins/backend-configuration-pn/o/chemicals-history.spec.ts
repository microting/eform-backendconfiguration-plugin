import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import { BackendConfigurationChemicalsPage } from '../BackendConfigurationChemicals.page';
import { seedRegisterChemical } from '../chemical-register-seed';
import { API_TIMEOUT, UI_TIMEOUT, waitForApiResponse } from '../wait-helpers';

/**
 * W6 "Historik" (flutter-chemistry spec §5.1, §11): what the admin did shows up in the immutable log, the
 * action filter narrows it on the server, and the log exports to Excel. Shard o runs on a fresh database;
 * the register is seeded by SQL, everything else through the UI.
 */
const rand = generateRandmString(6);
const property: PropertyCreateUpdate = {
  name: `Kemi-hist-${rand}`, chrNumber: generateRandmString(5), address: generateRandmString(5), cvrNumber: '1111111',
};
const product = `KemiHist${rand}`;
const location = `Lade-${rand}`;
/** ChemicalHistoryAction (chemical-reporting.model.ts). */
const REGISTERED = 1;
const LOCATION_CREATED = 8;
/** The "Registered" option in either UI language the CI admin may have. */
const REGISTERED_LABEL = /^\s*(Registreret|Registered)\s*$/;

async function openHistory(page: Page): Promise<BackendConfigurationChemicalsPage> {
  await page.goto('http://localhost:4200', { timeout: API_TIMEOUT });
  await new LoginPage(page).login();
  const kemi = new BackendConfigurationChemicalsPage(page);
  await kemi.openScreenFor('history', property.name);
  await kemi.waitForHistoryLoaded();
  return kemi;
}

test.describe.serial('Kemi W6 - history', () => {
  test('seed: a register product, a property, a location and one registered placement', async ({ page }) => {
    test.setTimeout(180000);
    await seedRegisterChemical({ name: product, registrationNo: `3-${rand}`, status: 5 });
    await page.goto('http://localhost:4200', { timeout: API_TIMEOUT });
    await new LoginPage(page).login();
    const properties = new BackendConfigurationPropertiesPage(page);
    await properties.goToProperties();
    await properties.createProperty(property);
    const kemi = new BackendConfigurationChemicalsPage(page);
    await kemi.openScreenFor('locations', property.name);
    await kemi.createLocation(location);
    await kemi.openScreenFor('inventory', property.name);
    await kemi.registerPlacement(product, location, 'Shelf 2');
  });

  test('the log lists the location and the registration; the action filter narrows it', async ({ page }) => {
    test.setTimeout(90000);
    const kemi = await openHistory(page);

    await expect(kemi.historyRows().filter({ hasText: location }).first()).toBeVisible({ timeout: API_TIMEOUT });
    await expect(kemi.historyRows().filter({ hasText: product })).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(kemi.historyRows().and(page.locator(`[data-action="${LOCATION_CREATED}"]`))).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(kemi.historyRows().and(page.locator(`[data-action="${REGISTERED}"]`))).toHaveCount(1, { timeout: UI_TIMEOUT });

    const filtered = waitForApiResponse(page, 'POST history (action = Registered)',
      (r) => /\/chemicals\/properties\/\d+\/history$/.test(r.url()) && r.request().postDataJSON()?.action === REGISTERED,
      API_TIMEOUT);
    await kemi.selectMtxOption('#chemicalHistoryActionFilter', REGISTERED_LABEL);
    expect((await (await filtered).json()).success).toBe(true);
    await kemi.waitForHistoryLoaded();
    await expect(kemi.historyRows()).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(kemi.historyRows().first()).toHaveAttribute('data-action', String(REGISTERED), { timeout: UI_TIMEOUT });
    await expect(kemi.historyRows().first()).toContainText(product, { timeout: UI_TIMEOUT });
  });

  test('exports the log to Excel', async ({ page }) => {
    test.setTimeout(90000);
    const kemi = await openHistory(page);
    await kemi.historyRows().first().waitFor({ state: 'visible', timeout: API_TIMEOUT });

    await expect(page.locator('#chemicalHistoryExportBtn')).toBeEnabled({ timeout: UI_TIMEOUT });
    expect(await kemi.download('#chemicalHistoryExportBtn')).toMatch(new RegExp(`${property.name} - \\d{4}-\\d{2}-\\d{2}\\.xlsx$`));
  });
});
