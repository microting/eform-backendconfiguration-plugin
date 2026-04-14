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

const property: PropertyCreateUpdate = {
  name: generateRandmString(5),
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const worker: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name as string],
  workerEmail: generateRandmString(5) + '@test.com',
};

const testEvent = {
  title: `Event-${generateRandmString(5)}`,
};

test.describe('Calendar E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
  });

  test('should create property, worker, navigate to calendar and create an event', async ({ page }) => {
    test.setTimeout(600000);

    const calendarPage = new CalendarPage(page);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    // Step 1: Create property
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    // Step 2: Create worker and assign to property
    await workersPage.goToPropertyWorkers();
    await page.waitForTimeout(500);
    await workersPage.create(worker);
    await page.waitForTimeout(1000);

    // Step 3: Navigate to calendar
    await calendarPage.goToCalendar();
    await page.waitForTimeout(2000);

    // Step 4: Select the test property in sidebar
    await calendarPage.selectProperty(property.name as string);
    await page.waitForTimeout(2000);

    // Step 5: Click a future time slot.
    const today = new Date();
    const dayOfWeek = today.getDay(); // 0=Sun, 1=Mon...
    // Pick a day at least one day after today within the visible week (Mon-Sun).
    // Monday is index 0. If today is Sunday (0), target Monday (0).
    // Otherwise, target the day after today: dayOfWeek (which maps Mon->0, Tue->1…)
    // but keep within range 0-6.
    let targetDayIndex: number;
    if (dayOfWeek === 0) {
      // Sunday — pick Monday (start of visible week)
      targetDayIndex = 0;
    } else if (dayOfWeek === 6) {
      // Saturday — stay on Saturday (today)
      targetDayIndex = 5;
    } else {
      // Weekday — pick tomorrow
      targetDayIndex = dayOfWeek; // dayOfWeek 1 (Mon) -> index 1 (Tue)
    }
    await calendarPage.clickTimeSlot(targetDayIndex, 10);
    await page.waitForTimeout(1000);

    // Step 6: Fill the create modal
    await calendarPage.fillCreateModal({
      title: testEvent.title,
    });
    await page.waitForTimeout(500);

    // Step 7: Save
    await calendarPage.saveModal();
    await page.waitForTimeout(3000);

    // Step 8: Verify the event appears on the calendar
    const eventExists = await calendarPage.verifyEventExists(testEvent.title);
    expect(eventExists).toBeTruthy();
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
