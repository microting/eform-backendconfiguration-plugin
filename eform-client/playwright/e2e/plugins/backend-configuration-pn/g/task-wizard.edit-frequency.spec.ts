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
import { clickAndWaitForResponses, getActionButtonForRow } from './helpers/flaky-fix-helpers';

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
  name: 'a',
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
  repeatType: 'Altid',
  repeatEvery: '5',
};

test.describe('Area rules type 1', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
  });

  test('should edit task frequency', async ({ page }) => {
    test.setTimeout(600000);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await propertiesPage.createProperty(property2);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(workerForCreate);
    await propertiesPage.goToProperties();

    await page.locator('#backend-configuration-pn-task-wizard').click();
    await expect(page.locator('#createNewTaskBtn')).toBeEnabled();
    await page.locator('#createNewTaskBtn').click();
    await expect(page.locator('#createProperty')).toBeVisible();

    // fill and create task
    await page.locator('#createProperty').click();
    await clickAndWaitForResponses(
      page,
      () => selectValueInNgSelectorNoSelector(page, `${property.name}`),
      ['/api/backend-configuration-pn/properties/get-folder-dtos?']
    );

    await expect(page.locator('#createFolder mat-select .mat-mdc-select-trigger')).toBeVisible();
    await page.locator('#createFolder mat-select .mat-mdc-select-trigger').click({ force: true });
    await expect(page.locator('mat-tree-node > button')).toBeVisible();
    await page.locator('mat-tree-node > button').click();
    await expect(page.locator('.folder-tree-name').filter({ hasText: '00. Logbøger' }).first()).toBeVisible();
    await page.locator('.folder-tree-name').filter({ hasText: '00. Logbøger' }).first().click();

    await page.locator('#createTableTags').click();
    await selectValueInNgSelectorNoSelector(page, '0. ' + property.name + ' - ' + property.address);
    await page.locator('#createTags').click();
    await selectValueInNgSelectorNoSelector(page, '0. ' + property.name + ' - ' + property.address);

    for (let i = 0; i < task.translations.length; i++) {
      await page.locator(`[for='createName${i}']`).scrollIntoViewIfNeeded();
      await expect(page.locator(`[for='createName${i}']`)).toBeVisible();
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

    await clickAndWaitForResponses(
      page,
      () => page.locator('#createTaskBtn').click(),
      ['/api/backend-configuration-pn/task-wizard']
    );

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

    // edit task — create new folder first
    await page.locator('#advanced').click();
    await page.locator('#folders').click();
    const treeNode = page.locator('mat-tree-node').filter({ hasText: property.name }).first();
    await treeNode.locator('button[mat-icon-button]').filter({ hasText: 'more_vert' }).click();
    await expect(page.locator('#createFolderChildBtn')).toBeVisible();
    await page.locator('#createFolderChildBtn').click();
    const newFolderName = generateRandmString(10);
    await expect(page.locator('#createFolderNameTranslation_0')).toBeVisible();
    await page.locator('#createFolderNameTranslation_0').fill(newFolderName);
    await page.locator('#createFolderDescriptionTranslation_0 .NgxEditor__Content').fill(generateRandmString());

    await clickAndWaitForResponses(
      page,
      () => page.locator('#folderSaveBtn').click(),
      ['/api/backend-configuration-pn']
    );

    await page.locator('#backend-configuration-pn-task-wizard').scrollIntoViewIfNeeded();
    await page.locator('#backend-configuration-pn-task-wizard').click();
    await expect(page.locator('.cdk-row')).toHaveCount(1, { timeout: 30000 });

    // Open action menu and click Edit Task
    const actionMenuBtn1 = page.locator('[id^=action-items]').first().locator('#actionMenu');
    await expect(actionMenuBtn1).toBeVisible();
    await actionMenuBtn1.click({ force: true });
    const editTaskOverlayBtn1 = await getActionButtonForRow(page, '', 'editTaskBtn', true);
    await expect(editTaskOverlayBtn1).toBeVisible();
    await editTaskOverlayBtn1.click({ force: true });

    // change task status (toggle)
    await expect(page.locator('#updateTaskStatusToggle')).toBeVisible();
    await page.locator('#updateTaskStatusToggle').click();
    await page.locator('#updateTaskBtn').click();

    // check table - status should be inactive now
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
    await expect(page.locator('.cdk-row .cdk-column-status mat-chip span.mat-mdc-chip-action-label')).toHaveText('Ikke aktiv');
    await expect(page.locator('.cdk-row .cdk-column-assignedTo mat-chip span.mat-mdc-chip-action-label')).toHaveText(
      `${workerForCreate.name} ${workerForCreate.surname}`
    );

    // Open action menu and click Edit Task again
    const actionMenuBtn2 = page.locator('[id^=action-items]').first().locator('#actionMenu');
    await expect(actionMenuBtn2).toBeVisible();
    await actionMenuBtn2.click({ force: true });
    const editTaskOverlayBtn2 = await getActionButtonForRow(page, '', 'editTaskBtn', true);
    await expect(editTaskOverlayBtn2).toBeVisible();
    await editTaskOverlayBtn2.click({ force: true });

    await expect(page.locator('#updateFolder mat-select .mat-mdc-select-trigger')).toBeVisible({ timeout: 30000 });
    await page.locator('#updateFolder mat-select .mat-mdc-select-trigger').click({ force: true });
    await expect(page.locator('mat-tree-node > button')).toBeVisible();
    await page.locator('mat-tree-node > button').click();
    await expect(page.locator('.folder-tree-name').filter({ hasText: newFolderName })).toBeVisible();
    await page.locator('.folder-tree-name').filter({ hasText: newFolderName }).click();

    for (let i = 0; i < editedTask.translations.length; i++) {
      await page.locator(`#updateName${i}`).clear();
      await page.locator(`#updateName${i}`).fill(editedTask.translations[i]);
    }

    await selectValueInNgSelector(page, '#updateTemplateSelector', editedTask.eformName, true);
    await page.locator('#updateStartFrom').click();
    await selectDateOnNewDatePicker(page, editedTask.startFrom.year, editedTask.startFrom.month, editedTask.startFrom.day);
    await selectValueInNgSelector(page, '#updateRepeatType', editedTask.repeatType, true);
    // cy.get('#updateRepeatEvery') steps are commented out in Cypress source — repeat every not changed for "Altid" type

    // enable task
    await page.locator('#updateTaskStatusToggle').click();

    await clickAndWaitForResponses(
      page,
      () => page.locator('#updateTaskBtn').click(),
      ['/api/backend-configuration-pn/task-wizard']
    );

    // check table after edit
    await expect(page.locator('.cdk-row')).toHaveCount(1);
    await expect(page.locator('.cdk-row .cdk-column-property span')).toHaveText(task.property);
    await expect(page.locator('.cdk-row .cdk-column-folder span')).toHaveText(newFolderName);
    await expect(page.locator('.cdk-row .cdk-column-taskName span')).toHaveText(editedTask.translations[0]);
    await expect(page.locator('.cdk-row .cdk-column-eform span')).toHaveText(new RegExp(`${editedTask.eformName} \\(\\d+\\)`));
    await expect(page.locator('.cdk-row .cdk-column-startDate span')).toHaveText(
      `${editedTask.startFrom.day}.${editedTask.startFrom.month >= 10 ? '' : '0'}${editedTask.startFrom.month}.${editedTask.startFrom.year}`
    );
    await expect(page.locator('.cdk-row .cdk-column-repeat mat-chip span.mat-mdc-chip-action-label')).toHaveText(
      `${editedTask.repeatType}`
    );
    await expect(page.locator('.cdk-row .cdk-column-status mat-chip span.mat-mdc-chip-action-label')).toHaveText('Aktiv');
    await expect(page.locator('.cdk-row .cdk-column-assignedTo mat-chip span.mat-mdc-chip-action-label')).toHaveText(
      `${workerForCreate.name} ${workerForCreate.surname}`
    );
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
