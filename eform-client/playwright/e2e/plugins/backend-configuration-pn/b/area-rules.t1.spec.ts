import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import { BackendConfigurationPropertyWorkersPage, PropertyWorker } from '../BackendConfigurationPropertyWorkers.page';
import { generateRandmString } from '../../../helper-functions';

const property: PropertyCreateUpdate = {
  name: generateRandmString(),
  chrNumber: generateRandmString(),
  address: generateRandmString(),
  cvrNumber: '1111111',
};

const workerForCreate: PropertyWorker = {
  name: generateRandmString(),
  surname: generateRandmString(),
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString() + '@test.com',
};

const nameArea: string = '00. Logbøger';

test.describe('Area rules type 1', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
  });

  test('should sorting table', async ({ page }) => {
    test.setTimeout(300000);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await expect(page.locator('app-properties-table')).toBeVisible();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(workerForCreate);
    await propertiesPage.goToProperties();
    const propertyInTable = await propertiesPage.getRowObjectByName(property.name);
    await page.waitForTimeout(1000);
    await propertyInTable.goToAreas();
    // propertyInTable.bindAreasByName([nameArea]);
    await page.waitForTimeout(500);
    await propertyInTable.goToPropertyAreaByName(nameArea);
    // TODO add rules before test
    // testSorting('.mat-header-cell.mat-column-id', '.mat-cell.mat-column-id', 'ID');
    // testSorting('.mat-header-cell.mat-column-translatedName', '.mat-cell.mat-column-translatedName', 'Name');
    // testSorting('.mat-header-cell.mat-column-eformName', '.mat-cell.mat-column-eformName', 'eForm');
    // testSorting('.mat-header-cell.mat-column-startDate', '.mat-cell.mat-column-startDate', 'start date');
    // testSorting('.mat-header-cell.mat-column-repeatType', '.mat-cell.mat-column-repeatType', 'Repeat type');
    // testSorting('.mat-header-cell.mat-column-repeatEvery', '.mat-cell.mat-column-repeatEvery', 'Repeat every');
    // testSorting('.mat-header-cell.mat-column-planningStatus', '.mat-cell.mat-column-planningStatus', 'Status');
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
