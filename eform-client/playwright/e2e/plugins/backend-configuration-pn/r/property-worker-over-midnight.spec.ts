import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';

// "Shifts across midnight" (overMidnight) in the property-worker edit modal.
//
// The AssignedSite.OverMidnight flag was already persisted by the
// time-planning assigned-site dialog; this spec covers the same toggle in the
// backend-configuration "edit worker" modal:
// 1. It only renders as a sub-option of punch-clock mode.
// 2. Saving persists it through both save calls (update-device-user POST and
//    time-planning assigned-site PUT) and it survives a modal reopen.
// 3. Leaving punch-clock mode hides AND clears it (no stale hidden value).

const property: PropertyCreateUpdate = {
  name: 'OverMidnight ' + generateRandmString(4),
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const worker: PropertyWorker = {
  name: `Om${generateRandmString(4)}`,
  surname: 'Midnight',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
  timeRegistrationEnabled: true,
};
const workerFullName = `${worker.name} ${worker.surname}`;

async function openEditModal(page: Page): Promise<void> {
  const row = page.locator('.mat-mdc-row').filter({ hasText: workerFullName }).first();
  const editBtn = page.locator('[id^=editDeviceUserBtn]');
  // A table refresh can re-render the row and close a just-opened action
  // menu, so retry the menu open until the edit item is actually visible.
  for (let attempt = 0; attempt < 3; attempt++) {
    await row.locator('#actionMenu').click();
    try {
      await editBtn.waitFor({ state: 'visible', timeout: 5000 });
      break;
    } catch {
      await page.keyboard.press('Escape');
      await page.waitForTimeout(1000);
    }
  }
  await editBtn.click();
  await expect(page.locator('form[data-form-ready]')).toHaveAttribute(
    'data-form-ready',
    'true',
    { timeout: 30000 }
  );
}

async function openTimeRegistrationTab(page: Page): Promise<void> {
  await page
    .locator('mat-dialog-container .mat-mdc-tab')
    .filter({ hasText: 'Timeregistrering' })
    .first()
    .click();
  await page.waitForTimeout(500);
}

test.describe.serial('Property-worker overMidnight toggle', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(3000);
  });

  test('seed: property and time-registration worker', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);
    await expect(
      page.locator('.mat-mdc-row').filter({ hasText: workerFullName })
    ).toHaveCount(1);
  });

  test('overMidnight renders under punch clock only, persists, and clears on mode change', async ({ page }) => {
    test.setTimeout(600000);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await openEditModal(page);
    await openTimeRegistrationTab(page);

    // Turn on mobile time registration; default entry method is manual, so
    // the punch-clock sub-options must not be rendered yet.
    await page.locator('#allowPersonalTimeRegistration input').check();
    await page.waitForTimeout(500);
    await expect(page.locator('#overMidnight')).toHaveCount(0);

    // Punch clock mode reveals the sub-option, unchecked by default.
    await page.locator('#entryMethodPunchClock input').check();
    await page.waitForTimeout(500);
    await expect(page.locator('#overMidnight')).toBeVisible();
    await expect(page.locator('#overMidnight input')).not.toBeChecked();

    // Check it and save; both persistence calls must succeed.
    await page.locator('#overMidnight input').check();
    const updateDeviceUserPromise = page.waitForResponse(
      r =>
        r.url().includes('/api/backend-configuration-pn/properties/assignment/update-device-user') &&
        r.request().method() === 'POST'
    );
    const assignedSitePromise = page.waitForResponse(
      r =>
        r.url().includes('/api/time-planning-pn/settings/assigned-site') &&
        r.request().method() === 'PUT'
    );
    const indexPromise = page.waitForResponse(
      r =>
        r.url().includes('/api/backend-configuration-pn/properties/assignment/index-device-user') &&
        r.request().method() === 'POST'
    );
    await page.locator('#saveEditBtn').click();
    const updateDeviceUserResponse = await updateDeviceUserPromise;
    const assignedSiteResponse = await assignedSitePromise;
    expect(updateDeviceUserResponse.status()).toBe(200);
    expect(assignedSiteResponse.status()).toBe(200);
    const updateBody = JSON.parse(updateDeviceUserResponse.request().postData() || '{}');
    expect(updateBody.overMidnight).toBe(true);
    const putBody = JSON.parse(assignedSiteResponse.request().postData() || '{}');
    expect(putBody.overMidnight).toBe(true);
    // Wait for the post-save table refresh to finish before touching the row
    // again — reopening the menu mid-refresh detaches it.
    await indexPromise;
    await workersPage.newDeviceUserBtn().waitFor({ state: 'visible' });
    await page.waitForTimeout(1000);

    // Round-trip: reopen and the value must have come back from the DB.
    await openEditModal(page);
    await openTimeRegistrationTab(page);
    await expect(page.locator('#entryMethodPunchClock input')).toBeChecked();
    await expect(page.locator('#overMidnight input')).toBeChecked();

    // Leaving punch clock hides the sub-option; returning shows it cleared.
    await page.locator('#entryMethodManual input').check();
    await page.waitForTimeout(500);
    await expect(page.locator('#overMidnight')).toHaveCount(0);
    await page.locator('#entryMethodAcceptPlanned input').check();
    await page.waitForTimeout(500);
    await expect(page.locator('#overMidnight')).toHaveCount(0);
    await page.locator('#entryMethodPunchClock input').check();
    await page.waitForTimeout(500);
    await expect(page.locator('#overMidnight input')).not.toBeChecked();

    await page.locator('#cancelEditBtn').click();
    await workersPage.newDeviceUserBtn().waitFor({ state: 'visible' });
  });
});
