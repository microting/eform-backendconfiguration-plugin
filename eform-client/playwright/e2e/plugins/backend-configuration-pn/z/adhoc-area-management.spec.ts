import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationPropertiesPage } from '../BackendConfigurationProperties.page';
import { BackendConfigurationAdhocPage } from '../BackendConfigurationAdhoc.page';

/**
 * Adhoc area management — create/filter/rename/delete flow (Task 5).
 *
 * Shard z runs without the shard-a DB dump, so it starts from a fresh
 * database and seeds its own data via the UI: one property, then creates
 * areas via the toolbar buttons, verifies they appear in filters and drawer,
 * renames one, deletes one, and confirms all state changes persist across
 * modals and filters.
 *
 * Runs as `admin@admin.com` throughout (`LoginPage.login()` default).
 */
const BASE_URL = 'http://localhost:4200';
const rand = generateRandmString(8).toLowerCase();

const property = {
  name: `adhoc-am-prop-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1234567',
};

const areaNames = [`Lade-${rand}`, `Stald-${rand}`];

async function login(page: import('@playwright/test').Page): Promise<void> {
  await page.goto(BASE_URL);
  await new LoginPage(page).login();
}

test.describe.serial('Adhoc area management — create, filter, rename, delete', () => {
  test('enable area buttons after property selection', async ({ page }) => {
    test.setTimeout(180000);
    await login(page);

    // Create one property and navigate to adhoc dashboard
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();

    // Before property selection, area buttons should be disabled
    const createBtn = adhocPage.page.locator('#adhocToolbarAreaCreateBtn');
    const manageBtn = adhocPage.page.locator('#adhocToolbarAreaManageBtn');
    await expect(createBtn).toBeDisabled();
    await expect(manageBtn).toBeDisabled();

    // Select the property in the filter
    await adhocPage.selectPropertyFilter(property.name);

    // After property selection, both buttons should be enabled
    await expect(createBtn).toBeEnabled();
    await expect(manageBtn).toBeEnabled();
  });

  test('create two areas and verify in filter and drawer', async ({ page }) => {
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();
    await adhocPage.selectPropertyFilter(property.name);

    // Create two areas via the modal
    await adhocPage.createAreas(areaNames);

    // Verify both areas appear in the toolbar area filter
    // (selectAreaFilter uses selectValueInNgSelector which opens the dropdown and verifies the option exists)
    for (const areaName of areaNames) {
      await adhocPage.selectAreaFilter(areaName);
    }

    // Verify both areas appear in the task drawer's area dropdown
    await adhocPage.openNewTask();
    await adhocPage.selectDrawerProperty(property.name);

    // Both area names should be selectable in the drawer's area dropdown
    for (const areaName of areaNames) {
      await adhocPage.selectDrawerArea(areaName);
    }

    await adhocPage.closeDrawer();
  });

  test('deduplicate when re-creating with duplicate + new name', async ({ page }) => {
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();
    await adhocPage.selectPropertyFilter(property.name);

    // Re-create with one duplicate (first area name) + one new name
    const newAreaName = `Garage-${rand}`;
    const mixedNames = [areaNames[0], newAreaName]; // Lade (duplicate) + Garage (new)
    await adhocPage.createAreas(mixedNames);

    // Open admin modal and verify exactly three areas (dedupe held)
    await adhocPage.openAreaAdminModal();

    const areaRows = adhocPage.page.locator('.area-row');
    await expect(areaRows).toHaveCount(3);

    // Verify all three expected area names are present
    const row1 = adhocPage.page.locator('.area-row', {hasText: areaNames[0]});
    const row2 = adhocPage.page.locator('.area-row', {hasText: areaNames[1]});
    const row3 = adhocPage.page.locator('.area-row', {hasText: newAreaName});

    await expect(row1).toBeVisible();
    await expect(row2).toBeVisible();
    await expect(row3).toBeVisible();

    await adhocPage.closeAreaAdminModal();
  });

  test('rename area and verify in filter', async ({ page }) => {
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();
    await adhocPage.selectPropertyFilter(property.name);

    // Open admin modal and rename Stald -> Maskinhal
    const newName = `Maskinhal-${rand}`;
    await adhocPage.openAreaAdminModal();

    // Rename it (resolves the area id from its own row, keyed by name)
    await adhocPage.renameAreaInAdminModal(areaNames[1], newName);

    // Verify the row updates (the old name should disappear, new name appears)
    const oldRow = adhocPage.page.locator('.area-row', {hasText: areaNames[1]});
    const newRow = adhocPage.page.locator('.area-row', {hasText: newName});

    await expect(oldRow).toHaveCount(0);
    await expect(newRow).toBeVisible();

    await adhocPage.closeAreaAdminModal();

    // Verify the new name appears in the filter
    await adhocPage.selectAreaFilter(newName);

    // Verify the filter's real option list (ng-select's `ng-dropdown-panel`
    // > `.ng-option`, scraped by areaFilterOptions()) has the renamed area
    // and no longer the old name. The reload after the modal closes is
    // async (afterClosed -> loadAreas -> HTTP -> cache -> component), so
    // poll rather than scrape once.
    await expect.poll(() => adhocPage.areaFilterOptions(), { timeout: 30_000 }).toContain(newName);
    await expect.poll(() => adhocPage.areaFilterOptions(), { timeout: 30_000 }).not.toContain(areaNames[1]);
  });

  test('delete area and verify gone from filter', async ({ page }) => {
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();
    await adhocPage.selectPropertyFilter(property.name);

    // Open admin modal and delete the renamed area (Maskinhal)
    const newName = `Maskinhal-${rand}`;
    await adhocPage.openAreaAdminModal();

    await adhocPage.deleteAreaInAdminModal(newName);

    // Verify its row disappears
    const deletedRow = adhocPage.page.locator('.area-row', {hasText: newName});
    await expect(deletedRow).toHaveCount(0);

    // Remaining row count should be 2 (Lade, Garage)
    const areaRows = adhocPage.page.locator('.area-row');
    await expect(areaRows).toHaveCount(2);

    await adhocPage.closeAreaAdminModal();

    // Verify the deleted name is gone from the filter's real option list
    // while the other areas (Lade) remain. The reload after the modal
    // closes is async (afterClosed -> loadAreas -> HTTP -> cache ->
    // component), so poll rather than scrape once.
    await expect.poll(() => adhocPage.areaFilterOptions(), { timeout: 30_000 }).not.toContain(newName);
    await expect.poll(() => adhocPage.areaFilterOptions(), { timeout: 30_000 }).toContain(areaNames[0]);
  });
});
