import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import { BackendConfigurationPropertyWorkersPage, PropertyWorker } from '../BackendConfigurationPropertyWorkers.page';
import { BackendConfigurationChemicalsPage } from '../BackendConfigurationChemicals.page';
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';

/**
 * W4 "Medarbejdere & rettigheder" (flutter-chemistry spec §8, §11). Shard d runs on
 * a fresh database: the spec seeds two properties and one worker on both via the UI,
 * then grants, persists, implies (Admin), undoes, copies and guards unsaved edits.
 * Every test ends with a clean screen so no beforeunload prompt is left pending.
 */
const rand = generateRandmString(6);
const main: PropertyCreateUpdate = { name: `Kemi-perm-A-${rand}`, chrNumber: generateRandmString(5), address: generateRandmString(5), cvrNumber: '1111111' };
const other: PropertyCreateUpdate = { name: `Kemi-perm-B-${rand}`, chrNumber: generateRandmString(5), address: generateRandmString(5), cvrNumber: '1111111' };
const worker: PropertyWorker = {
  name: `Kemi${generateRandmString(5)}`,
  surname: 'Arbejder',
  language: 'Dansk',
  properties: [main.name, other.name],
  workerEmail: `${generateRandmString(6)}@test.com`,
};
const workerName = `${worker.name} ${worker.surname}`;
const impliedFlags = ['view', 'register', 'remove', 'stock', 'manageLocations'];

async function openPermissions(page: Page, propertyName: string): Promise<BackendConfigurationChemicalsPage> {
  await page.goto('http://localhost:4200');
  await new LoginPage(page).login();
  const kemi = new BackendConfigurationChemicalsPage(page);
  await kemi.gotoPermissions();
  await kemi.selectProperty(propertyName);
  await kemi.waitForWorkerRow(workerName);
  return kemi;
}

test.describe.serial('Kemi W4 - worker permissions', () => {
  test('seed: two properties and one worker on both', async ({ page }) => {
    // Two SDK-backed worker saves plus two property creates; SLOW_API_TIMEOUT-sized steps.
    test.setTimeout(300000);
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    const properties = new BackendConfigurationPropertiesPage(page);
    await properties.goToProperties();
    await properties.createProperty(main);
    await properties.createProperty(other);
    const workers = new BackendConfigurationPropertyWorkersPage(page);
    await workers.goToPropertyWorkers();
    await workers.create(worker);
    await expect(workers.workerRow(workerName)).toHaveCount(1, { timeout: API_TIMEOUT });
  });

  test('the Kemi menu group opens Rettigheder', async ({ page }) => {
    test.setTimeout(120000);
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    const kemi = new BackendConfigurationChemicalsPage(page);

    await kemi.goToPermissionsViaMenu();

    await expect(page).toHaveURL(/\/plugins\/backend-configuration-pn\/chemicals\/permissions/, { timeout: UI_TIMEOUT });
    await expect(kemi.title()).toBeVisible({ timeout: UI_TIMEOUT });
  });

  test('grant View + Register, save, and see it after a reload', async ({ page }) => {
    test.setTimeout(120000);
    const kemi = await openPermissions(page, main.name);

    await kemi.permissionCheckbox('view', workerName).check({ timeout: UI_TIMEOUT });
    await kemi.permissionCheckbox('register', workerName).check({ timeout: UI_TIMEOUT });
    // The save bar counts changed workers: one worker, two flags.
    await expect(kemi.saveBar()).toBeVisible({ timeout: UI_TIMEOUT });
    await kemi.savePermissions();

    await page.reload();
    await kemi.permissionsPage().waitFor({ state: 'visible', timeout: API_TIMEOUT });
    await kemi.waitForWorkerRow(workerName);
    await expect(kemi.permissionCheckbox('view', workerName)).toBeChecked({ timeout: UI_TIMEOUT });
    await expect(kemi.permissionCheckbox('register', workerName)).toBeChecked({ timeout: UI_TIMEOUT });
    await expect(kemi.permissionCheckbox('stock', workerName)).not.toBeChecked({ timeout: UI_TIMEOUT });
  });

  test('Admin implies and locks every other flag; unticking Admin keeps them; undo restores', async ({ page }) => {
    test.setTimeout(120000);
    const kemi = await openPermissions(page, main.name);

    await kemi.permissionCheckbox('admin', workerName).check({ timeout: UI_TIMEOUT });

    for (const flag of impliedFlags) {
      await expect(kemi.permissionCheckbox(flag, workerName)).toBeChecked({ timeout: UI_TIMEOUT });
      await expect(kemi.permissionCheckbox(flag, workerName)).toBeDisabled({ timeout: UI_TIMEOUT });
      await expect(kemi.permissionCell(flag, workerName)).toHaveClass(/flag-checkbox--implied/, { timeout: UI_TIMEOUT });
    }
    await expect(kemi.permissionCell('admin', workerName)).not.toHaveClass(/flag-checkbox--implied/, { timeout: UI_TIMEOUT });

    // Owner ruling I4: unticking Admin leaves the flags it implied ticked, and now editable.
    await kemi.permissionCheckbox('admin', workerName).uncheck({ timeout: UI_TIMEOUT });
    for (const flag of impliedFlags) {
      await expect(kemi.permissionCheckbox(flag, workerName)).toBeChecked({ timeout: UI_TIMEOUT });
      await expect(kemi.permissionCheckbox(flag, workerName)).toBeEnabled({ timeout: UI_TIMEOUT });
      await expect(kemi.permissionCell(flag, workerName)).not.toHaveClass(/flag-checkbox--implied/, { timeout: UI_TIMEOUT });
    }

    await kemi.undoButton().click({ timeout: UI_TIMEOUT });
    await kemi.saveBar().waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    await expect(kemi.permissionCheckbox('stock', workerName)).not.toBeChecked({ timeout: UI_TIMEOUT });
    await expect(kemi.permissionCheckbox('stock', workerName)).toBeEnabled({ timeout: UI_TIMEOUT });
  });

  test('copy from another property lands as unsaved changes and saves', async ({ page }) => {
    test.setTimeout(120000);
    const kemi = await openPermissions(page, other.name);
    await expect(kemi.permissionCheckbox('view', workerName)).not.toBeChecked({ timeout: UI_TIMEOUT });

    await kemi.copyButton().click({ timeout: UI_TIMEOUT });
    await kemi.copyFromItem(main.name).click({ timeout: UI_TIMEOUT });

    await expect(kemi.permissionCheckbox('view', workerName)).toBeChecked({ timeout: API_TIMEOUT });
    await expect(kemi.permissionCheckbox('register', workerName)).toBeChecked({ timeout: UI_TIMEOUT });
    await expect(kemi.saveBar()).toBeVisible({ timeout: UI_TIMEOUT });
    await kemi.savePermissions();
  });

  test('switching property with unsaved edits asks first: keep editing, then discard', async ({ page }) => {
    test.setTimeout(120000);
    const kemi = await openPermissions(page, main.name);
    await kemi.permissionCheckbox('stock', workerName).check({ timeout: UI_TIMEOUT });
    await expect(kemi.saveBar()).toBeVisible({ timeout: UI_TIMEOUT });

    // Keep editing: the picker, the edit and the save bar all stay.
    await kemi.pickProperty(other.name);
    await kemi.keepEditingButton().click({ timeout: UI_TIMEOUT });
    await kemi.keepEditingButton().waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    await expect(kemi.propertySelect()).toContainText(main.name, { timeout: UI_TIMEOUT });
    await expect(kemi.propertySelect()).not.toContainText(other.name, { timeout: UI_TIMEOUT });
    await expect(kemi.permissionCheckbox('stock', workerName)).toBeChecked({ timeout: UI_TIMEOUT });
    await expect(kemi.saveBar()).toBeVisible({ timeout: UI_TIMEOUT });

    // Discard: the switch goes through and the edit is gone.
    await kemi.pickProperty(other.name);
    await kemi.discardConfirmButton().click({ timeout: UI_TIMEOUT });
    await kemi.discardConfirmButton().waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    await expect(kemi.propertySelect()).toContainText(other.name, { timeout: UI_TIMEOUT });
    await kemi.waitForWorkerRow(workerName);
    await expect(kemi.saveBar()).toBeHidden({ timeout: UI_TIMEOUT });

    // Back on the first property (clean, so no prompt) the discarded edit is not there.
    await kemi.selectProperty(main.name);
    await kemi.waitForWorkerRow(workerName);
    await expect(kemi.permissionCheckbox('stock', workerName)).not.toBeChecked({ timeout: UI_TIMEOUT });
    await expect(kemi.saveBar()).toBeHidden({ timeout: UI_TIMEOUT });
  });

  test('the footnote names Admin or Fjerne as push recipients (spec §17)', async ({ page }) => {
    test.setTimeout(60000);
    const kemi = await openPermissions(page, main.name);

    await expect(kemi.pushNote()).toContainText('Admin eller Fjerne', { timeout: UI_TIMEOUT });
    await expect(kemi.scopeNote()).toContainText(main.name, { timeout: UI_TIMEOUT });
  });
});
