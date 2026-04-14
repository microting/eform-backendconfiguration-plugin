import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarPage } from './calendar.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';

const testProperty: PropertyCreateUpdate = {
  name: `CalTest-${generateRandmString(5)}`,
  cvrNumber: '1234567',
  chrNumber: `CHR-${generateRandmString(5)}`,
  address: 'Calendar Test Address 1',
};

const testWorker: PropertyWorker = {
  name: 'CalendarTest',
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [testProperty.name as string],
  workerEmail: generateRandmString(5) + '@test.com',
};

const testEvent = {
  title: `Event-${Date.now()}`,
};

test.describe('Calendar E2E Tests', () => {
  let calendarPage: CalendarPage;
  let loginPage: LoginPage;

  test.beforeEach(async ({ page }) => {
    calendarPage = new CalendarPage(page);
    loginPage = new LoginPage(page);
    await page.goto('http://localhost:4200');
    await loginPage.login();
  });

  test('should set up test data and create calendar event', async ({ page }) => {
    test.setTimeout(300000); // 5 min

    // Step 1: Create property
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(testProperty);

    // Step 2: Create worker and assign to property
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(testWorker);

    // Step 3: Navigate to calendar
    await calendarPage.goToCalendar();
    await page.waitForTimeout(2000);

    // Step 4: Select the test property
    await calendarPage.selectProperty(testProperty.name as string);
    await page.waitForTimeout(2000);

    // Step 5: Click a future time slot.
    // Pick a day index within the visible week that's safely in the future.
    const today = new Date();
    const dayOfWeek = today.getDay(); // 0=Sun, 1=Mon...
    const targetDayIndex = dayOfWeek === 0 ? 1 : dayOfWeek < 5 ? dayOfWeek : 1;
    await calendarPage.clickTimeSlot(targetDayIndex, 10);
    await page.waitForTimeout(1000);

    // Step 6: Fill the create modal
    await calendarPage.fillCreateModal({
      title: testEvent.title,
    });

    // Step 7: Save
    await calendarPage.saveModal();
    await page.waitForTimeout(3000);

    // Step 8: Verify event appears on calendar
    const eventExists = await calendarPage.verifyEventExists(testEvent.title);
    expect(eventExists).toBeTruthy();
  });

  test('should drag event to a different time', async ({ page }) => {
    test.setTimeout(120000);

    await calendarPage.goToCalendar();
    await page.waitForTimeout(2000);

    await calendarPage.selectProperty(testProperty.name as string);
    await page.waitForTimeout(2000);

    const eventBefore = await calendarPage.verifyEventExists(testEvent.title);
    if (!eventBefore) {
      test.skip();
      return;
    }

    const today = new Date();
    const dayOfWeek = today.getDay();
    const targetDayIndex = dayOfWeek === 0 ? 2 : dayOfWeek < 4 ? dayOfWeek + 1 : 2;
    await calendarPage.dragEvent(testEvent.title, targetDayIndex, 14);
    await page.waitForTimeout(2000);

    const eventAfter = await calendarPage.verifyEventExists(testEvent.title);
    expect(eventAfter).toBeTruthy();
  });

  test.afterAll(async ({ browser }) => {
    // Cleanup: delete workers and the test property
    const page = await browser.newPage();
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await workersPage.goToPropertyWorkers();
    await page.waitForTimeout(1000);
    await workersPage.clearTable();

    await propertiesPage.goToProperties();
    await page.waitForTimeout(1000);
    await propertiesPage.clearTable();

    await page.close();
  });
});
