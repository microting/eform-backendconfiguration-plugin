import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import { BackendConfigurationPropertyWorkersPage, PropertyWorker } from '../BackendConfigurationPropertyWorkers.page';
import {
  selectValueInNgSelector,
  generateRandmString,
  selectValueInNgSelectorNoSelector,
  selectDateOnNewDatePicker,
} from '../../../helper-functions';

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
    await page.waitForTimeout(3000);
    await expect(page.locator('#createNewTaskBtn')).toBeEnabled();
    await page.locator('#createNewTaskBtn').click();
    await page.waitForTimeout(500);

    const getFoldersResponse = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos?'),
      { timeout: 60000 }
    );
    await page.locator('#createProperty').click();
    await selectValueInNgSelectorNoSelector(page, `${property.name}`);
    await getFoldersResponse;
    await page.waitForTimeout(500);

    await expect(page.locator('#createFolder mat-select .mat-mdc-select-trigger')).toBeVisible();
    await page.locator('#createFolder mat-select .mat-mdc-select-trigger').click({ force: true });
    await page.waitForTimeout(500);
    await page.locator('mat-tree-node > button').click();
    await page.waitForTimeout(500);
    await page.locator('.folder-tree-name').filter({ hasText: bindArea }).first().click();
    await page.waitForTimeout(500);

    await page.locator('#createTableTags').click();
    await page.waitForTimeout(500);
    await selectValueInNgSelectorNoSelector(page, '0. ' + property.name + ' - ' + property.address);
    await page.locator('#createTags').click();
    await page.waitForTimeout(500);
    await selectValueInNgSelectorNoSelector(page, '0. ' + property.name + ' - ' + property.address);
    await page.waitForTimeout(500);

    for (let i = 0; i < task.translations.length; i++) {
      await page.locator(`[for='createName${i}']`).scrollIntoViewIfNeeded();
      await expect(page.locator(`[for='createName${i}']`)).toBeVisible();
      await page.waitForTimeout(500);
      await page.locator(`[for='createName${i}']`).fill(task.translations[i]);
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
    await page.waitForTimeout(500);

    await expect(page.locator('.cdk-row')).toHaveCount(1);

    // Open the action menu
    await expect(page.locator('.task-actions').first().locator('#actionMenu')).toBeVisible();
    await page.locator('.task-actions').first().locator('#actionMenu').click({ force: true });

    // Click the Delete Task button inside the opened menu
    await expect(page.locator('.cdk-overlay-container').locator('[id^=deleteTaskBtn]').first()).toBeVisible();
    await page.locator('.cdk-overlay-container').locator('[id^=deleteTaskBtn]').first().click({ force: true });

    // Wait for delete confirmation dialog to appear
    await expect(page.locator('#taskWizardDeleteCancelBtn')).toBeVisible({ timeout: 30000 });
    await page.locator('#taskWizardDeleteCancelBtn').click();
    // Wait for dialog/overlay to fully close before re-opening menu
    await expect(page.locator('#taskWizardDeleteCancelBtn')).toBeHidden({ timeout: 10000 });
    await page.waitForTimeout(500);
    await expect(page.locator('.cdk-row')).toHaveCount(1);

    // Open the action menu again
    await expect(page.locator('.task-actions').first().locator('#actionMenu')).toBeVisible();
    await page.locator('.task-actions').first().locator('#actionMenu').click({ force: true });
    await page.waitForTimeout(400);

    // Click the Delete Task button inside the opened menu
    await expect(page.locator('.cdk-overlay-container').locator('[id^=deleteTaskBtn]').first()).toBeVisible();
    await page.locator('.cdk-overlay-container').locator('[id^=deleteTaskBtn]').first().click({ force: true });

    await expect(page.locator('#taskWizardDeleteDeleteBtn')).toBeVisible({ timeout: 30000 });
    await page.locator('#taskWizardDeleteDeleteBtn').click();
    await page.waitForTimeout(500);
    await expect(page.locator('.cdk-row')).toHaveCount(0);
  });

  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage();
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await propertiesPage.goToProperties();
    await page.waitForTimeout(500);
    await propertiesPage.clearTable();
    await workersPage.goToPropertyWorkers();
    await workersPage.clearTable();
    await page.close();
  });
});
