import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarPage } from '../l/calendar.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';

// Reproduces the reported bug: after creating a brand-new property + worker
// and assigning that worker to the property, saving a calendar event fails
// silently (modal stays open with no feedback). The backend can return
// success=false for preconditions that only show up on fresh data
// (missing Logbøger folder, worker not yet provisioned in eForm SDK).
//
// Test expectations:
// 1. POST /api/backend-configuration-pn/calendar/tasks returns HTTP 200
// 2. The response body has success=true
//
// If the bug is present, (2) fails and the logged message tells us which
// precondition broke, so we can target the backend fix.

const property: PropertyCreateUpdate = {
  name: 'Den glade gris ' + generateRandmString(4),
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const worker: PropertyWorker = {
  name: 'foo',
  surname: 'bar',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};

const testEvent = {
  title: `Event-${generateRandmString(5)}`,
};

test.describe('Calendar: save event on fresh property with newly-assigned worker', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(3000);
  });

  test('saving a new calendar event should succeed', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    const calendarPage = new CalendarPage(page);

    // Step 1: create the property
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    // Step 2: create the worker and assign them to the new property
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    // Step 3: open the calendar
    await calendarPage.goToCalendar();
    await page.waitForTimeout(2000);

    // Step 4: select the property, wait for the folder lookup to finish
    const folderResponsePromise = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
      { timeout: 60000 }
    );
    await calendarPage.selectProperty(property.name as string);
    await folderResponsePromise;
    await page.waitForTimeout(2000);

    // Step 5: click a future time slot on the visible week
    const today = new Date();
    const dayOfWeek = today.getDay(); // 0=Sun, 1=Mon...
    let targetDayIndex: number;
    if (dayOfWeek === 0) {
      targetDayIndex = 6;
    } else if (dayOfWeek === 6) {
      targetDayIndex = 5;
    } else {
      targetDayIndex = dayOfWeek;
    }
    await calendarPage.clickTimeSlot(targetDayIndex, 10);
    await page.waitForTimeout(1000);

    // Step 6: fill the modal (title + eForm + planning tag via page object)
    await calendarPage.fillCreateModal({ title: testEvent.title });
    await page.waitForTimeout(500);

    // Step 7: save and assert the backend accepted the task
    const createResponsePromise = page.waitForResponse(
      r =>
        r.url().includes('/api/backend-configuration-pn/calendar/tasks') &&
        r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await calendarPage.saveModal();
    const createResponse = await createResponsePromise;
    const resBody = await createResponse.json().catch(() => null);
    console.log(
      `calendar create task (fresh property+worker): status=${createResponse.status()}, success=${resBody?.success}, message=${resBody?.message}`
    );

    expect(createResponse.status()).toBe(200);
    expect(resBody?.success).toBeTruthy();
  });

  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage();
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();

    // Workers reference properties, so delete workers first.
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await page.waitForTimeout(1000);
    await workersPage.clearTable();

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await page.waitForTimeout(1000);
    await propertiesPage.clearTable();
    await page.close();
  });
});
