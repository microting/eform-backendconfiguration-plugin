import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import { BackendConfigurationPropertyWorkersPage, PropertyWorker } from '../BackendConfigurationPropertyWorkers.page';
import {
  selectValueInNgSelector,
  generateRandmString,
  selectValueInNgSelectorNoSelector,
  selectDateOnNewDatePicker,
} from '../../../helper-functions';

// Opens the row action menu then clicks Delete inside it.
// Two sequential CDK overlays: the menu, then (after delete click) the
// confirmation dialog. Waiting on visibility each step avoids force-clicks
// on transitional DOM where the menu has already begun to close.
async function openActionMenuAndClickDelete(page: Page): Promise<void> {
  const actionMenu = page.locator('.task-actions').first().locator('#actionMenu');
  await expect(actionMenu).toBeVisible({ timeout: 10000 });
  await actionMenu.click();

  const deleteBtn = page.locator('.cdk-overlay-container').locator('[id^=deleteTaskBtn]').first();
  await expect(deleteBtn).toBeVisible({ timeout: 10000 });
  await deleteBtn.click();
}

const property: PropertyCreateUpdate = {
  name: generateRandmString(5),
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const workerForCreate: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};

const task = {
  property: property.name,
  translations: [
    generateRandmString(12),
    generateRandmString(12),
    generateRandmString(12),
  ],
  eformName: 'Kvittering',
  startFrom: {
    year: 2023,
    month: 7,
    day: 21,
  },
  repeatType: 'Dag',
  repeatEvery: '2',
};

test.describe('Area rules type 1', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
  });

  // TODO: Fix this
  test('should create task', async ({ page }) => {
    test.setTimeout(600000);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(workerForCreate);
    await propertiesPage.goToProperties();
    const bindArea = '00. Logbøger';

    await page.locator('#backend-configuration-pn-task-wizard').click();
    await expect(page.locator('#createNewTaskBtn')).toBeEnabled({ timeout: 30000 });
    await page.locator('#createNewTaskBtn').click();
    await expect(page.locator('#createProperty')).toBeVisible({ timeout: 10000 });

    const getFoldersResponse = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos?'),
      { timeout: 60000 }
    );
    await page.locator('#createProperty').click();
    await selectValueInNgSelectorNoSelector(page, `${property.name}`);
    await getFoldersResponse;

    const folderSelect = page.locator('#createFolder mat-select .mat-mdc-select-trigger');
    await expect(folderSelect).toBeVisible({ timeout: 10000 });
    await folderSelect.click({ force: true });
    const treeNodeButton = page.locator('mat-tree-node > button');
    await expect(treeNodeButton).toBeVisible({ timeout: 10000 });
    await treeNodeButton.click();
    const folderLeaf = page.locator('.folder-tree-name').filter({ hasText: bindArea }).first();
    await expect(folderLeaf).toBeVisible({ timeout: 10000 });
    await folderLeaf.click();
    await expect(folderSelect).toContainText(bindArea, { timeout: 10000 });

    await page.locator('#createTableTags').click();
    await expect(page.locator('.ng-dropdown-panel')).toBeVisible({ timeout: 10000 });
    await selectValueInNgSelectorNoSelector(page, '0. ' + property.name + ' - ' + property.address);
    // Ensure the first dropdown fully closes before opening the second —
    // otherwise the toBeVisible check below can match the stale closing panel.
    await expect(page.locator('.ng-dropdown-panel')).toHaveCount(0, { timeout: 5000 });
    await page.locator('#createTags').click();
    await expect(page.locator('.ng-dropdown-panel')).toBeVisible({ timeout: 10000 });
    await selectValueInNgSelectorNoSelector(page, '0. ' + property.name + ' - ' + property.address);
    await expect(page.locator('.ng-dropdown-panel')).toHaveCount(0, { timeout: 5000 });

    for (let i = 0; i < task.translations.length; i++) {
      const nameField = page.locator(`[for='createName${i}']`);
      await nameField.scrollIntoViewIfNeeded();
      await expect(nameField).toBeVisible({ timeout: 10000 });
      await nameField.fill(task.translations[i]);
    }

    await selectValueInNgSelector(page, '#createTemplateSelector', task.eformName, true);
    await page.locator('#createStartFrom').click();
    await selectDateOnNewDatePicker(page, task.startFrom.year, task.startFrom.month, task.startFrom.day);
    await selectValueInNgSelector(page, '#createRepeatType', task.repeatType, true);

    await expect(page.locator('#createRepeatEvery')).toBeVisible();
    await expect(page.locator('#createRepeatEvery input')).toBeVisible();
    await page.locator('#createRepeatEvery input').clear();
    await page.locator('#createRepeatEvery input').fill(task.repeatEvery);
    await expect(page.locator('.ng-option').first()).toHaveText(task.repeatEvery);
    await expect(page.locator('.ng-option').first()).toBeVisible();
    await page.locator('.ng-option').first().click();

    await page.locator('mat-checkbox#checkboxCreateAssignment0').click();

    const createTaskResponse = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/task-wizard') && r.request().method() === 'POST',
      { timeout: 60000 }
    );
    await page.locator('#createTaskBtn').click();
    await createTaskResponse;

    await expect(page.locator('.cdk-row')).toHaveCount(1, { timeout: 10000 });

    // First round: open action menu, click delete, then cancel the confirmation
    await openActionMenuAndClickDelete(page);
    await expect(page.locator('#taskWizardDeleteCancelBtn')).toBeVisible({ timeout: 15000 });
    await page.locator('#taskWizardDeleteCancelBtn').click();
    // Wait for dialog/overlay to fully close before re-opening menu
    await expect(page.locator('#taskWizardDeleteCancelBtn')).toBeHidden({ timeout: 10000 });
    await expect(page.locator('.cdk-overlay-backdrop')).toHaveCount(0, { timeout: 10000 });
    await expect(page.locator('.cdk-row')).toHaveCount(1);

    // Second round: confirm delete
    await openActionMenuAndClickDelete(page);
    await expect(page.locator('#taskWizardDeleteDeleteBtn')).toBeVisible({ timeout: 15000 });
    const deleteResponse = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/task-wizard') && r.request().method() === 'DELETE',
      { timeout: 30000 }
    );
    await page.locator('#taskWizardDeleteDeleteBtn').click();
    await deleteResponse;
    await expect(page.locator('.cdk-row')).toHaveCount(0, { timeout: 10000 });
  });

  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage();
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.clearTable();
    await workersPage.goToPropertyWorkers();
    await workersPage.clearTable();
    await page.close();
  });
});
