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
  name: 'a',
  surname: 'x',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};
const workerForCreate2: PropertyWorker = {
  name: 'b',
  surname: 'x',
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

const editedTask = {
  property: property.name,
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

  test('should edit task', async ({ page }) => {
    test.setTimeout(600000);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await page.waitForTimeout(500);
    await workersPage.create(workerForCreate);
    await page.waitForTimeout(1000);
    await workersPage.create(workerForCreate2);
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
    await page.waitForTimeout(500);
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

    //selectValueInNgSelector(page, '#createTemplateSelector', task.eformName, true);
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
    await expect(page.locator('.cdk-row .cdk-column-eform span')).toHaveText(new RegExp(`${task.eformName} \\(\\d+\\)`));
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

    /* ==== Generated with Cypress Studio ==== */
    await page.locator('#mat-expansion-panel-header-2 > .mat-content').click();

    await propertiesPage.goToPlanningPage();
    await page.locator('.planningAssignmentBtn.mat-accent').click();

    // Check worker1 is checked
    const worker1Row = page.locator('#pairingModalTableBody > div > div > div > table > tbody > .mat-mdc-row')
      .filter({ hasText: workerForCreate.name });
    await worker1Row.scrollIntoViewIfNeeded();
    await expect(worker1Row.locator('mat-checkbox')).toHaveClass(/mat-mdc-checkbox-checked/);
    await page.locator('#changeAssignmentsCancel > .mdc-button__label').click();

    await page.locator('#backend-configuration-pn-task-wizard').click();
    await page.waitForTimeout(3000);
    await expect(page.locator('.cdk-row')).toHaveCount(1, { timeout: 30000 });

    await expect(page.locator('[id^=action-items]').first().locator('#actionMenu')).toBeVisible();
    await page.locator('[id^=action-items]').first().locator('#actionMenu').click({ force: true });

    await expect(page.locator('.cdk-overlay-container').locator('[id^=editTaskBtn]').first()).toBeVisible();
    await page.locator('.cdk-overlay-container').locator('[id^=editTaskBtn]').first().click({ force: true });
    await page.waitForTimeout(3000);

    await page.locator('#checkboxUpdateAssignment1-input').check();

    const updateTaskResponse = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/task-wizard') && r.request().method() === 'PUT',
      { timeout: 60000 }
    );
    await page.locator('#updateTaskBtn').click();
    await updateTaskResponse;
    await page.waitForTimeout(500);

    await propertiesPage.goToPlanningPage();
    await page.locator('.planningAssignmentBtn.mat-accent').click();

    // Check both workers are now checked
    const worker1Row2 = page.locator('#pairingModalTableBody > div > div > div > table > tbody > .mat-mdc-row')
      .filter({ hasText: workerForCreate.name });
    await worker1Row2.scrollIntoViewIfNeeded();
    await expect(worker1Row2.locator('mat-checkbox')).toHaveClass(/mat-mdc-checkbox-checked/);

    const worker2Row = page.locator('#pairingModalTableBody > div > div > div > table > tbody > .mat-mdc-row')
      .filter({ hasText: workerForCreate2.name });
    await worker2Row.scrollIntoViewIfNeeded();
    await expect(worker2Row.locator('mat-checkbox')).toHaveClass(/mat-mdc-checkbox-checked/);
    await page.locator('#changeAssignmentsCancel').click();

    await page.locator('#backend-configuration-pn-task-wizard').click();
    await expect(page.locator('.cdk-row')).toHaveCount(1, { timeout: 30000 });

    await expect(page.locator('[id^=action-items]').first().locator('#actionMenu')).toBeVisible();
    await page.locator('[id^=action-items]').first().locator('#actionMenu').click({ force: true });

    await expect(page.locator('.cdk-overlay-container').locator('[id^=editTaskBtn]').first()).toBeVisible();

    const getFoldersResponse3 = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos?'),
      { timeout: 60000 }
    );
    const getTemplatesResponse2 = page.waitForResponse(
      r => r.url().includes('/api/templates/index') && r.request().method() === 'POST',
      { timeout: 60000 }
    );
    await page.locator('.cdk-overlay-container').locator('[id^=editTaskBtn]').first().click({ force: true });
    await getFoldersResponse3;
    await getTemplatesResponse2;

    await page.locator('#checkboxUpdateAssignment0-input').uncheck();

    const updateTaskResponse2 = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/task-wizard') && r.request().method() === 'PUT',
      { timeout: 60000 }
    );
    await page.locator('#updateTaskBtn').click();
    await updateTaskResponse2;
    await page.waitForTimeout(500);

    await propertiesPage.goToPlanningPage();
    await page.locator('.planningAssignmentBtn.mat-accent').click();

    // Check worker1 is unchecked, worker2 is checked
    const worker1Row3 = page.locator('#pairingModalTableBody > div > div > div > table > tbody > .mat-mdc-row')
      .filter({ hasText: workerForCreate.name });
    await worker1Row3.scrollIntoViewIfNeeded();
    await expect(worker1Row3.locator('mat-checkbox')).not.toHaveClass(/mat-mdc-checkbox-checked/);

    const worker2Row2 = page.locator('#pairingModalTableBody > div > div > div > table > tbody > .mat-mdc-row')
      .filter({ hasText: workerForCreate2.name });
    await worker2Row2.scrollIntoViewIfNeeded();
    await expect(worker2Row2.locator('mat-checkbox')).toHaveClass(/mat-mdc-checkbox-checked/);
    await page.locator('#changeAssignmentsCancel > .mdc-button__label').click();
    /* ==== End Cypress Studio ==== */
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
