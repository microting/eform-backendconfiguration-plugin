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

    // Step 3: Select the test property in sidebar and wait for the folder
    // lookup to resolve — the create-event call needs the "Logbøger" folder ID.
    const folderResponsePromise = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
      { timeout: 60000 }
    );
    await calendarPage.selectProperty(property.name as string);
    const folderResponse = await folderResponsePromise;
    const folderBody = await folderResponse.json().catch(() => null);
    console.log(`folders for property: status=${folderResponse.status()}, success=${folderBody?.success}, model=${JSON.stringify(folderBody?.model)}`);
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

    // Step 6: Save and assert the create-task API succeeds
    const createResponsePromise = page.waitForResponse(
      r =>
        r.url().includes('/api/backend-configuration-pn/calendar/tasks') &&
        r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await calendarPage.saveModal();
    const createResponse = await createResponsePromise;
    const resBody = await createResponse.json().catch(() => null);
    const reqBody = createResponse.request().postData();
    console.log(`calendar create task: status=${createResponse.status()}, success=${resBody?.success}, message=${resBody?.message}, reqBody=${reqBody}`);
    expect(createResponse.status()).toBe(200);
    expect(resBody?.success).toBeTruthy();
  });

  test('should copy an existing calendar event', async ({ page }) => {
    test.setTimeout(600000);

    const calendarPage = new CalendarPage(page);

    // Navigate to calendar and select the property from the previous test
    await calendarPage.goToCalendar();
    await page.waitForTimeout(2000);
    await calendarPage.selectProperty(property.name as string);
    await page.waitForTimeout(2000);

    // Find the event from the prior test. The title pattern is Event-<random>.
    // Rather than relying on global state, locate any event block on the grid.
    const firstEvent = page.locator('app-calendar-task-block').first();
    await firstEvent.waitFor({ state: 'visible', timeout: 30000 });
    const originalTitle = (await firstEvent.locator('.task-title').innerText()).trim();

    // Open preview and click Copy
    await firstEvent.click();
    await page.waitForTimeout(1000);
    await calendarPage.clickCopyInPreview();

    // Verify the create modal opened with the source title preserved and a
    // translated prefix. Accept English ("Copy of ") or Danish ("Kopi af ").
    const prefilledTitle = await calendarPage.getCreateModalTitle();
    console.log(`copy prefill: prefilledTitle="${prefilledTitle}", originalTitle="${originalTitle}"`);
    expect(prefilledTitle).toContain(originalTitle);
    expect(prefilledTitle).toMatch(/^(Copy of|Kopi af)\s/);

    // Override title to distinguish the copy
    const copyTitle = `Copy-${generateRandmString(5)}`;
    await calendarPage.overrideTitle(copyTitle);

    // Save and assert the API call succeeded
    const createResponsePromise = page.waitForResponse(
      r =>
        r.url().includes('/api/backend-configuration-pn/calendar/tasks') &&
        r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await calendarPage.saveModal();
    const createResponse = await createResponsePromise;
    const resBody = await createResponse.json().catch(() => null);
    console.log(`copy create: status=${createResponse.status()}, success=${resBody?.success}, message=${resBody?.message}`);
    expect(createResponse.status()).toBe(200);
    expect(resBody?.success).toBeTruthy();

    // Verify both events exist
    await page.waitForTimeout(2000);
    const originalExists = await calendarPage.verifyEventExists(originalTitle);
    const copyExists = await calendarPage.verifyEventExists(copyTitle);
    expect(originalExists).toBeTruthy();
    expect(copyExists).toBeTruthy();
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
