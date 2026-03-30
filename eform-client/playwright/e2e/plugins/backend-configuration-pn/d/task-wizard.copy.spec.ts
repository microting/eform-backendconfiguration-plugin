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
const property2: PropertyCreateUpdate = {
  name: generateRandmString(5),
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const workerForCreate: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name, property2.name],
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

const editedTask = {
  property: property2.name,
  translations: [
    generateRandmString(12),
    generateRandmString(12),
    generateRandmString(12),
  ],
  eformName: 'Kontrol flydelag',
  startFrom: {
    year: 2022,
    month: 6,
    day: 24,
  },
  repeatType: 'Uge',
  repeatEvery: '5',
};

test.describe('Area rules type 1', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
  });

  test('should copy task', async ({ page }) => {
    test.setTimeout(300000);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await propertiesPage.createProperty(property2);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(workerForCreate);
    await propertiesPage.goToProperties();

    await page.locator('#backend-configuration-pn-task-wizard').click();
    await page.waitForTimeout(3000);
    await expect(page.locator('#createNewTaskBtn')).toBeEnabled();
    await page.locator('#createNewTaskBtn').click();
    await page.waitForTimeout(500);

    // fill and create task
    await page.locator('#createProperty').click();
    const getFoldersResponse = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos?'),
      { timeout: 60000 }
    );
    await selectValueInNgSelectorNoSelector(page, `${property.name}`);
    await getFoldersResponse;
    await page.waitForTimeout(1000);

    await expect(page.locator('#createFolder mat-select .mat-mdc-select-trigger')).toBeVisible();
    await page.locator('#createFolder mat-select .mat-mdc-select-trigger').click({ force: true });
    await page.waitForTimeout(1000);
    await page.locator('mat-tree-node > button').click();
    await page.waitForTimeout(500);
    await page.locator('.folder-tree-name').filter({ hasText: '00. Logbøger' }).first().click();
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

    // check table
    await expect(page.locator('.cdk-row')).toHaveCount(1);
    await expect(page.locator('.cdk-row .cdk-column-property span')).toHaveText(task.property);
    await expect(page.locator('.cdk-row .cdk-column-folder span')).toHaveText('00. Logbøger');
    await expect(page.locator('.cdk-row .cdk-column-taskName span')).toHaveText(task.translations[0]);
    await expect(page.locator('.cdk-row .cdk-column-eform span')).toHaveText(task.eformName + ' (11)');
    await expect(page.locator('.cdk-row .cdk-column-startDate span')).toHaveText(
      `${task.startFrom.day}.${task.startFrom.month >= 10 ? '' : '0'}${task.startFrom.month}.${task.startFrom.year}`
    );
    await expect(page.locator('.cdk-row .cdk-column-repeat mat-chip span.mat-mdc-chip-action-label')).toHaveText(
      `${task.repeatEvery} ${task.repeatType}`
    );
    await expect(page.locator('.cdk-row .cdk-column-status mat-chip span.mat-mdc-chip-action-label')).toHaveText('Aktiv');
    await expect(page.locator('.cdk-row .cdk-column-assignedTo mat-chip span.mat-mdc-chip-action-label')).toHaveText(
      `${workerForCreate.name} ${workerForCreate.surname}`
    );

    // Copy task
    await expect(page.locator('.task-actions').first().locator('#actionMenu')).toBeVisible();
    await page.locator('.task-actions').first().locator('#actionMenu').click({ force: true });

    // Now click the Copy Task button inside the opened menu
    await expect(page.locator('.cdk-overlay-container').locator('[id^=copyTaskBtn]').first()).toBeVisible();
    await page.locator('.cdk-overlay-container').locator('[id^=copyTaskBtn]').first().click({ force: true });

    const createTaskResponse2 = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/task-wizard') && r.request().method() === 'POST',
      { timeout: 60000 }
    );
    await page.locator('#createTaskBtn').click();
    await createTaskResponse2;
    await page.waitForTimeout(500);

    // check table after first copy
    await expect(page.locator('.cdk-row')).toHaveCount(2);
    await expect(page.locator('.cdk-row .cdk-column-property span').nth(0)).toHaveText(task.property);
    await expect(page.locator('.cdk-row .cdk-column-property span').nth(1)).toHaveText(task.property);
    await expect(page.locator('.cdk-row .cdk-column-folder span').nth(0)).toHaveText('00. Logbøger');
    await expect(page.locator('.cdk-row .cdk-column-folder span').nth(1)).toHaveText('00. Logbøger');
    await expect(page.locator('.cdk-row .cdk-column-taskName span').nth(0)).toHaveText(task.translations[0]);
    await expect(page.locator('.cdk-row .cdk-column-taskName span').nth(1)).toHaveText(task.translations[0]);
    await expect(page.locator('.cdk-row .cdk-column-eform span').nth(0)).toHaveText(task.eformName + ' (11)');
    await expect(page.locator('.cdk-row .cdk-column-eform span').nth(1)).toHaveText(task.eformName + ' (11)');
    await expect(page.locator('.cdk-row .cdk-column-startDate span').nth(0)).toHaveText(
      `${task.startFrom.day}.${task.startFrom.month >= 10 ? '' : '0'}${task.startFrom.month}.${task.startFrom.year}`
    );
    await expect(page.locator('.cdk-row .cdk-column-startDate span').nth(1)).toHaveText(
      `${task.startFrom.day}.${task.startFrom.month >= 10 ? '' : '0'}${task.startFrom.month}.${task.startFrom.year}`
    );
    await expect(page.locator('.cdk-row .cdk-column-repeat mat-chip span.mat-mdc-chip-action-label').nth(0)).toHaveText(
      `${task.repeatEvery} ${task.repeatType}`
    );
    await expect(page.locator('.cdk-row .cdk-column-repeat mat-chip span.mat-mdc-chip-action-label').nth(1)).toHaveText(
      `${task.repeatEvery} ${task.repeatType}`
    );
    await expect(page.locator('.cdk-row .cdk-column-status mat-chip span.mat-mdc-chip-action-label').nth(0)).toHaveText('Aktiv');
    await expect(page.locator('.cdk-row .cdk-column-status mat-chip span.mat-mdc-chip-action-label').nth(1)).toHaveText('Aktiv');
    await expect(page.locator('.cdk-row .cdk-column-assignedTo mat-chip span.mat-mdc-chip-action-label').nth(0)).toHaveText(
      `${workerForCreate.name} ${workerForCreate.surname}`
    );
    await expect(page.locator('.cdk-row .cdk-column-assignedTo mat-chip span.mat-mdc-chip-action-label').nth(1)).toHaveText(
      `${workerForCreate.name} ${workerForCreate.surname}`
    );

    // Copy and set new eform
    await expect(page.locator('.task-actions').first().locator('#actionMenu')).toBeVisible();
    await page.locator('.task-actions').first().locator('#actionMenu').click({ force: true });

    // Now click the Copy Task button inside the opened menu
    await expect(page.locator('.cdk-overlay-container').locator('[id^=copyTaskBtn]').first()).toBeVisible();
    await page.locator('.cdk-overlay-container').locator('[id^=copyTaskBtn]').first().click({ force: true });

    const createTaskResponse3 = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/task-wizard') && r.request().method() === 'POST',
      { timeout: 60000 }
    );
    await selectValueInNgSelector(page, '#createTemplateSelector', editedTask.eformName, true);
    await page.locator('#createTaskBtn').click();
    await createTaskResponse3;
    await page.waitForTimeout(500);

    // check table after second copy
    await expect(page.locator('.cdk-row')).toHaveCount(3);
    await expect(page.locator('.cdk-row .cdk-column-property span').nth(0)).toHaveText(task.property);
    await expect(page.locator('.cdk-row .cdk-column-property span').nth(1)).toHaveText(task.property);
    await expect(page.locator('.cdk-row .cdk-column-property span').nth(2)).toHaveText(task.property);
    await expect(page.locator('.cdk-row .cdk-column-folder span').nth(0)).toHaveText('00. Logbøger');
    await expect(page.locator('.cdk-row .cdk-column-folder span').nth(1)).toHaveText('00. Logbøger');
    await expect(page.locator('.cdk-row .cdk-column-folder span').nth(2)).toHaveText('00. Logbøger');
    await expect(page.locator('.cdk-row .cdk-column-taskName span').nth(0)).toHaveText(task.translations[0]);
    await expect(page.locator('.cdk-row .cdk-column-taskName span').nth(1)).toHaveText(task.translations[0]);
    await expect(page.locator('.cdk-row .cdk-column-taskName span').nth(2)).toHaveText(task.translations[0]);
    await expect(page.locator('.cdk-row .cdk-column-eform span').nth(0)).toHaveText(task.eformName + ' (11)');
    await expect(page.locator('.cdk-row .cdk-column-eform span').nth(1)).toHaveText(task.eformName + ' (11)');
    await expect(page.locator('.cdk-row .cdk-column-eform span').nth(2)).toHaveText(editedTask.eformName + ' (3)');
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
