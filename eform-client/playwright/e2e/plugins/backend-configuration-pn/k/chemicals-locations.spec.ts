import { test, expect, Page } from '@playwright/test';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationChemicalsPage, kemiProperty } from '../BackendConfigurationChemicals.page';
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';

/**
 * W3 "Lokationer" (flutter-chemistry spec §11): create, reorder by drag and by the up/down buttons (the
 * order is saved; the app uses it), archive after a confirm, "show archived". Shard k runs on a fresh
 * database; the spec seeds one property and three locations through the UI.
 */
const rand = generateRandmString(6);
const property = kemiProperty('Kemi-loc', rand);
const [a, b, c] = [`A-${rand}`, `B-${rand}`, `C-${rand}`];

const openLocations = (page: Page) => BackendConfigurationChemicalsPage.openLoggedIn(page, 'locations', property.name);

test.describe.serial('Kemi W3 - locations', () => {
  test('seed: a property and three locations, listed in creation order', async ({ page }) => {
    test.setTimeout(150000);
    const kemi = await BackendConfigurationChemicalsPage.login(page);
    await kemi.createPropertyWithLocations(property, [a, b, c]);
    await expect(kemi.activeLocationNames()).toHaveText([a, b, c], { timeout: UI_TIMEOUT });
  });

  test('dragging C to the top is saved and survives a reload', async ({ page }) => {
    test.setTimeout(90000);
    const kemi = await openLocations(page);
    await expect(kemi.activeLocationNames()).toHaveText([a, b, c], { timeout: API_TIMEOUT });
    const handle = kemi.locationRow(c).locator('[id^="chemicalLocationDrag-"]');
    const from = await handle.boundingBox({ timeout: UI_TIMEOUT });
    const to = await kemi.locationRow(a).boundingBox({ timeout: UI_TIMEOUT });
    if (!from || !to) {
      throw new Error('the drag handle of C or the row of A is not rendered');
    }

    const saved = kemi.waitForLocationOrderSaved();
    const x = from.x + from.width / 2;
    await page.mouse.move(x, from.y + from.height / 2);
    await page.mouse.down();
    // Past the CDK drag threshold (5 px) first, then over the top of A in small steps so the list sorts.
    await page.mouse.move(x, from.y + from.height / 2 - 10, { steps: 2 });
    await page.mouse.move(x, to.y + 2, { steps: 15 });
    await page.mouse.up();
    await saved;
    await expect(kemi.activeLocationNames()).toHaveText([c, a, b], { timeout: UI_TIMEOUT });

    await kemi.reloadScreenFor('locations', property.name);
    await expect(kemi.activeLocationNames()).toHaveText([c, a, b], { timeout: API_TIMEOUT });
  });

  test('the down button moves a location one place and the order is saved', async ({ page }) => {
    test.setTimeout(90000);
    const kemi = await openLocations(page);
    await expect(kemi.activeLocationNames()).toHaveText([c, a, b], { timeout: API_TIMEOUT });
    const cId = await kemi.locationId(c);
    await expect(page.locator(`#chemicalLocationUp-${cId}`)).toBeDisabled({ timeout: UI_TIMEOUT });

    const saved = kemi.waitForLocationOrderSaved();
    await page.locator(`#chemicalLocationDown-${cId}`).click({ timeout: UI_TIMEOUT });
    await saved;
    await expect(kemi.activeLocationNames()).toHaveText([a, c, b], { timeout: UI_TIMEOUT });

    await kemi.reloadScreenFor('locations', property.name);
    await expect(kemi.activeLocationNames()).toHaveText([a, c, b], { timeout: API_TIMEOUT });
  });

  test('archive (after the confirm) hides the location until "show archived"', async ({ page }) => {
    test.setTimeout(90000);
    const kemi = await openLocations(page);
    await kemi.locationRow(b).waitFor({ state: 'visible', timeout: API_TIMEOUT });

    await kemi.locationRow(b).locator('[id^="chemicalLocationArchive-"]').click({ timeout: UI_TIMEOUT });
    const archived = kemi.waitForChemicalsCall('POST', /^locations\/\d+\/archive$/);
    await page.locator('#chemicalLocationArchiveConfirm').click({ timeout: UI_TIMEOUT });
    await archived;
    await expect(kemi.locationRow(b)).toHaveCount(0, { timeout: API_TIMEOUT });
    await expect(kemi.activeLocationNames()).toHaveText([a, c], { timeout: UI_TIMEOUT });

    await page.locator('#chemicalLocationsShowArchived input[type="checkbox"]').check({ timeout: UI_TIMEOUT });
    await expect(kemi.locationRow(b)).toHaveClass(/chemicals-location-row--archived/, { timeout: UI_TIMEOUT });
    // An archived location is read-only: no drag handle, no reorder buttons, no archive.
    await expect(kemi.locationRow(b).locator('[id^="chemicalLocationDrag-"]')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(kemi.locationRow(b).locator('[id^="chemicalLocationArchive-"]')).toBeDisabled({ timeout: UI_TIMEOUT });
  });
});
