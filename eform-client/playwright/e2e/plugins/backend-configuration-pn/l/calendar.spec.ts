import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarPage } from './calendar.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';

const property: PropertyCreateUpdate = {
  name: generateRandmString(5),
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const testEvent = {
  title: `Event-${generateRandmString(5)}`,
};

test.describe('Calendar E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(3000);
  });

  test('should create a calendar event for a property', async ({ page }) => {
    test.setTimeout(600000);

    const calendarPage = new CalendarPage(page);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);

    // Step 1: Create property
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    // Step 2: Navigate to calendar (direct URL — no sidebar entry)
    await calendarPage.goToCalendar();
    await page.waitForTimeout(2000);

    // Step 3: Select the test property in sidebar
    await calendarPage.selectProperty(property.name as string);
    await page.waitForTimeout(2000);

    // Step 4: Click a future time slot.
    // Visible week is Mon-Sun (indices 0-6). Pick a day safely in the future.
    const today = new Date();
    const dayOfWeek = today.getDay(); // 0=Sun, 1=Mon...
    let targetDayIndex: number;
    if (dayOfWeek === 0) {
      // Sunday — next day (Monday) is actually next week's view, so pick today
      targetDayIndex = 6;
    } else if (dayOfWeek === 6) {
      // Saturday — pick today (Saturday)
      targetDayIndex = 5;
    } else {
      // Weekday — pick tomorrow. Mon (1) -> index 1 (Tuesday)
      targetDayIndex = dayOfWeek;
    }
    await calendarPage.clickTimeSlot(targetDayIndex, 10);
    await page.waitForTimeout(1000);

    // Step 5: Fill the create modal
    await calendarPage.fillCreateModal({
      title: testEvent.title,
    });
    await page.waitForTimeout(500);

    // Step 6: Save
    await calendarPage.saveModal();
    await page.waitForTimeout(3000);

    // Step 7: Verify the event appears on the calendar
    const eventExists = await calendarPage.verifyEventExists(testEvent.title);
    expect(eventExists).toBeTruthy();
  });

  test.afterAll(async ({ browser }) => {
    // Cleanup: delete the test property
    const page = await browser.newPage();
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();

    const propertiesPage = new BackendConfigurationPropertiesPage(page);

    await propertiesPage.goToProperties();
    await page.waitForTimeout(1000);
    await propertiesPage.clearTable();

    await page.close();
  });
});
