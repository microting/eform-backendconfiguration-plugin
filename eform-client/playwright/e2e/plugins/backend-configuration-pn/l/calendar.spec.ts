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

    // Step 3: Select the test property and wait for the folder lookup
    // — create-event needs the auto-resolved Logbøger folder.
    const folderResponsePromise = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
      { timeout: 60000 }
    );
    await calendarPage.selectProperty(property.name as string);
    await folderResponsePromise;
    await page.waitForTimeout(2000);

    // Step 4: Click a future time slot. Visible week is Mon-Sun (0-6).
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

    // Step 5: Fill and save
    await calendarPage.fillCreateModal({ title: testEvent.title });
    await page.waitForTimeout(500);

    // Step 5b: Verify the eForm preview is collapsed by default but the toggle is present
    const previewToggle = page.locator('#calendarEformPreviewToggle');
    await previewToggle.waitFor({ state: 'visible', timeout: 10000 });
    expect(await calendarPage.isEformPreviewExpanded()).toBeFalsy();

    // Click toggle to expand
    await calendarPage.toggleEformPreview();
    expect(await calendarPage.isEformPreviewExpanded()).toBeTruthy();

    // Log the field count for diagnostics — count varies with the seeded
    // template definition. The seed Kvittering may have 0 fields, in which
    // case the empty-state message renders instead of field rows. Either is
    // acceptable; the assertion is just that the body opens.
    const fieldCount = await calendarPage.countEformPreviewFields();
    console.log(`eForm preview field count: ${fieldCount}`);

    // Collapse again so it doesn't affect subsequent steps visually
    await calendarPage.toggleEformPreview();
    expect(await calendarPage.isEformPreviewExpanded()).toBeFalsy();

    const createResponsePromise = page.waitForResponse(
      r =>
        r.url().includes('/api/backend-configuration-pn/calendar/tasks') &&
        r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await calendarPage.saveModal();
    const createResponse = await createResponsePromise;
    const resBody = await createResponse.json().catch(() => null);
    console.log(`calendar create task: status=${createResponse.status()}, success=${resBody?.success}, message=${resBody?.message}`);
    expect(createResponse.status()).toBe(200);
    expect(resBody?.success).toBeTruthy();
  });

  test('should copy an event and preserve eForm + planning tag', async ({ page }) => {
    test.setTimeout(600000);

    const calendarPage = new CalendarPage(page);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);

    // Navigate to calendar (property was created by the previous test)
    await calendarPage.goToCalendar();
    await page.waitForTimeout(2000);

    // Select the property. Also wait for the week-tasks POST so we know
    // the calendar has finished fetching events before asserting visibility.
    const folderResponsePromise = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
      { timeout: 60000 }
    );
    const weekTasksResponsePromise = page.waitForResponse(
      r =>
        r.url().includes('/api/backend-configuration-pn/calendar/tasks/week') &&
        r.request().method() === 'POST',
      { timeout: 60000 }
    );
    await calendarPage.selectProperty(property.name as string);
    await folderResponsePromise;
    const weekTasksResponse = await weekTasksResponsePromise;

    // E1 diagnostic: log the captured request (URL + body) and response
    // body so we can distinguish a stale pre-selection capture from the
    // real post-selection fetch, and see exactly what the server returned.
    const weekTasksRequest = weekTasksResponse.request();
    const weekTasksReqBody = weekTasksRequest.postData();
    const weekTasksRawBody = await weekTasksResponse.text().catch(() => '');
    console.log(`week-tasks REQUEST url: ${weekTasksRequest.url()}`);
    console.log(`week-tasks REQUEST body: ${weekTasksReqBody}`);
    console.log(`week-tasks RESPONSE body (first 800): ${weekTasksRawBody.slice(0, 800)}`);
    const weekTasksBody = (() => {
      try { return JSON.parse(weekTasksRawBody); } catch { return null; }
    })();
    console.log(
      `week-tasks payload: count=${weekTasksBody?.model?.length ?? 0}, titles=${
        weekTasksBody?.model?.map((t: any) => t.title).join(', ') ?? 'none'
      }`
    );

    // E2: bypass the UI entirely — fetch /tasks/week directly with the
    // property from the captured request and wide-open filters. This
    // proves whether the event is actually persisted, independent of
    // any frontend state or board/site filter.
    const directProbe = await page.evaluate(async (capturedBodyStr: string | null) => {
      try {
        const auth = JSON.parse(localStorage.getItem('auth') || '{}');
        const token = auth?.token?.accessToken;
        const captured = capturedBodyStr ? JSON.parse(capturedBodyStr) : {};
        const body = {
          propertyId: captured.propertyId,
          weekStart: captured.weekStart,
          weekEnd: captured.weekEnd,
          boardIds: [],
          tagNames: [],
          siteIds: [],
        };
        const res = await fetch('/api/backend-configuration-pn/calendar/tasks/week', {
          method: 'POST',
          headers: {
            'Accept': 'application/json',
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${token}`,
          },
          body: JSON.stringify(body),
        });
        const txt = await res.text();
        return { status: res.status, bodyUsed: body, response: txt.slice(0, 1200) };
      } catch (e: any) {
        return { error: String(e?.message ?? e) };
      }
    }, weekTasksReqBody ?? null);
    console.log(`direct fetch RESULT: ${JSON.stringify(directProbe)}`);

    // The event from the previous test should be visible. 30s gives
    // generous headroom over the 10s default for slow CI renders.
    const eventVisible = await calendarPage.waitForEvent(testEvent.title, 30000);
    expect(eventVisible, `Event "${testEvent.title}" was not visible within 30s`).toBeTruthy();

    // Open preview and click Copy
    await calendarPage.openEventPreview(testEvent.title);
    await page.waitForTimeout(1000);
    await calendarPage.clickCopyInPreview();
    await page.waitForTimeout(1500);

    // Verify copy modal is open with "Copy of" prefix
    const copyTitle = await calendarPage.getCreateModalTitle();
    console.log(`Copy modal title: "${copyTitle}"`);
    expect(copyTitle).toContain('Copy of');
    expect(copyTitle).toContain(testEvent.title);

    // Verify eForm is still selected (not empty)
    const eformValue = await calendarPage.getSelectValue('#calendarEventEform');
    console.log(`Copy modal eForm: "${eformValue}"`);
    expect(eformValue).toBeTruthy();

    // Verify planning tag is still selected
    const planningTagValue = await calendarPage.getSelectValue('#calendarEventPlanningTag');
    console.log(`Copy modal planning tag: "${planningTagValue}"`);
    expect(planningTagValue).toBeTruthy();

    // Save the copy
    const createResponsePromise = page.waitForResponse(
      r =>
        r.url().includes('/api/backend-configuration-pn/calendar/tasks') &&
        r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await calendarPage.saveModal();
    const createResponse = await createResponsePromise;
    const resBody = await createResponse.json().catch(() => null);
    console.log(`calendar copy task: status=${createResponse.status()}, success=${resBody?.success}, message=${resBody?.message}`);
    expect(createResponse.status()).toBe(200);
    expect(resBody?.success).toBeTruthy();

    // Verify copied event appears on the calendar
    const copiedTitle = `Copy of ${testEvent.title}`;
    const copiedVisible = await calendarPage.waitForEvent(copiedTitle);
    expect(copiedVisible, `Event "${copiedTitle}" was not visible within 10s`).toBeTruthy();
  });

  test.afterAll(async ({ browser }) => {
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
