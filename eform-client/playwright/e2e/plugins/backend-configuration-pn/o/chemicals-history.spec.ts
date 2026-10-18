import { test, expect, Page } from '@playwright/test';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationChemicalsPage, kemiProperty } from '../BackendConfigurationChemicals.page';
import { seedRegisterChemical } from '../chemical-register-seed';
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';

/**
 * W6 "Historik" (flutter-chemistry spec §5.1, §11): what the admin did shows up in the immutable log, the
 * action filter narrows it on the server, and the log exports to Excel. Shard o runs on a fresh database;
 * the register is seeded by SQL, everything else through the UI.
 */
const rand = generateRandmString(6);
const property = kemiProperty('Kemi-hist', rand);
const product = `KemiHist${rand}`;
const location = `Lade-${rand}`;
/** ChemicalHistoryAction (chemical-reporting.model.ts). */
const REGISTERED = 1;
const LOCATION_CREATED = 8;
/** The "Registered" option in either UI language the CI admin may have. */
const REGISTERED_LABEL = /^\s*(Registreret|Registered)\s*$/;

async function openHistory(page: Page): Promise<BackendConfigurationChemicalsPage> {
  const kemi = await BackendConfigurationChemicalsPage.openLoggedIn(page, 'history', property.name);
  await kemi.waitForHistoryLoaded();
  return kemi;
}

test.describe.serial('Kemi W6 - history', () => {
  test('seed: a register product, a property, a location and one registered placement', async ({ page }) => {
    test.setTimeout(180000);
    await seedRegisterChemical({ name: product, registrationNo: `3-${rand}`, status: 5 });
    const kemi = await BackendConfigurationChemicalsPage.login(page);
    await kemi.createPropertyWithLocations(property, [location]);
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

    const filtered = kemi.waitForChemicalsCall('POST', /^properties\/\d+\/history$/,
      (r) => r.request().postDataJSON()?.action === REGISTERED);
    await kemi.selectMtxOption('#chemicalHistoryActionFilter', REGISTERED_LABEL);
    await filtered;
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
