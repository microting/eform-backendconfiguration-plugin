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

// Exercises the calendar event modal's tags + report tag (Rapportoverskrift)
// round-trip with edit. Covers:
//   1. Inline-create two planning tags via the new "+ Create new" addTag
//      affordance on the Rapportoverskrift dropdown.
//   2. Select tagA as Rapportoverskrift, add BOTH tagA and tagB to the
//      Set tags multi-select, and save.
//   3. Verify the POST body sends a populated tagIds array (previously a bug
//      made it always empty) and a numeric itemPlanningTagId.
//   4. Reopen via preview → Edit pencil and assert the form rehydrates with
//      the saved planning tag + set tags.
//   5. Edit — swap report tag from tagA → tagB, remove tagA chip — save
//      (PUT) and re-open to confirm the edit round-trips too.

// Unique names so the test doesn't collide with the other l/ spec, which
// creates its own property/worker/event in the same matrix slot.
const rand = generateRandmString(5);

const property: PropertyCreateUpdate = {
  name: `PropRT-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// Calendar events require at least one assignee (backend enforces), so
// create a worker assigned to the property before saving any event.
const worker: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};

const title = `EventRT-${rand}`;
const tagA = `RT-A-${rand}`;
const tagB = `RT-B-${rand}`;

test.describe('Calendar tags + report tag round-trip', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(3000);
  });

  test('event tags + report tag round-trip with edit', async ({ page }) => {
    test.setTimeout(900000);

    const calendarPage = new CalendarPage(page);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    // ------------------------------------------------------------------
    // Step 1: property + worker setup
    // ------------------------------------------------------------------
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    // ------------------------------------------------------------------
    // Step 2: open the calendar, select property, advance to next week
    // ------------------------------------------------------------------
    await calendarPage.goToCalendar();
    await page.waitForTimeout(2000);

    const folderResponsePromise = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
      { timeout: 60000 }
    );
    await calendarPage.selectProperty(property.name as string);
    await folderResponsePromise;
    await page.waitForTimeout(2000);

    // Next week so every day is in the future (past slots are rejected).
    await page.locator('mat-icon:has-text("chevron_right")').first().click();
    await page.waitForTimeout(1500);

    // Click Monday 10:00.
    await calendarPage.clickTimeSlot(0, 10);
    await page.waitForTimeout(1000);

    // ------------------------------------------------------------------
    // Step 3: fill title + inline-create two planning tags
    // ------------------------------------------------------------------
    await page
      .locator('#calendarEventTitle')
      .waitFor({ state: 'visible', timeout: 15000 });
    await page.locator('#calendarEventTitle').fill(title);

    // Inline-create tagA. The addTag affordance POSTs /api/items-planning-pn/tags
    // and selects the returned tag in the planning-tag control.
    const createTagAResponse = page.waitForResponse(
      r =>
        r.url().includes('/api/items-planning-pn/tags') &&
        r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await calendarPage.inlineCreatePlanningTag(tagA);
    await createTagAResponse;

    await expect(
      page.locator('#calendarEventPlanningTag .ng-value-label').first(),
      'After inline-create A, planning tag should display tagA'
    ).toHaveText(tagA, { timeout: 5000 });
    console.log(`Planning tag after inline-create A: OK (${tagA})`);

    const createTagBResponse = page.waitForResponse(
      r =>
        r.url().includes('/api/items-planning-pn/tags') &&
        r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await calendarPage.inlineCreatePlanningTag(tagB);
    await createTagBResponse;

    await expect(
      page.locator('#calendarEventPlanningTag .ng-value-label').first(),
      'After inline-create B, planning tag should display tagB'
    ).toHaveText(tagB, { timeout: 5000 });
    console.log(`Planning tag after inline-create B: OK (${tagB})`);

    // ------------------------------------------------------------------
    // Step 4: set Rapportoverskrift back to tagA
    // ------------------------------------------------------------------
    // After inlineCreatePlanningTag(tagB), tagB is the current selection.
    // Switch it back to tagA so we can verify the chosen report tag
    // survives the round-trip.
    await calendarPage.selectExistingPlanningTag(tagA);

    await expect(
      page.locator('#calendarEventPlanningTag .ng-value-label').first(),
      'After switching back, planning tag should display tagA'
    ).toHaveText(tagA, { timeout: 5000 });
    console.log(`Planning tag after switch back: OK (${tagA})`);

    // ------------------------------------------------------------------
    // Step 5: add both tags to the Set tags multi-select
    // ------------------------------------------------------------------
    await calendarPage.addExistingTag(tagA);
    await calendarPage.addExistingTag(tagB);

    // Fill in the description. Round-trip this through save/edit/reopen to
    // catch cases where the field fails to persist or rehydrate.
    const description = `Description-${rand}`;
    await page.locator('#calendarEventDescription').fill(description);

    // ------------------------------------------------------------------
    // Step 6: pick first assignee (required). eForm left as default.
    // ------------------------------------------------------------------
    const assigneeSelect = page.locator('#calendarEventAssignee');
    await assigneeSelect.click();
    await page
      .locator('.ng-dropdown-panel')
      .waitFor({ state: 'visible', timeout: 10000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.locator('#calendarEventTitle').click(); // close dropdown
    await page.waitForTimeout(300);

    // ------------------------------------------------------------------
    // Step 7: save — capture POST body, assert tagIds + itemPlanningTagId
    // ------------------------------------------------------------------
    const createRequestPromise = page.waitForRequest(
      r =>
        r.url().includes('/api/backend-configuration-pn/calendar/tasks') &&
        r.method() === 'POST',
      { timeout: 30000 }
    );
    const createResponsePromise = page.waitForResponse(
      r =>
        r.url().includes('/api/backend-configuration-pn/calendar/tasks') &&
        r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await calendarPage.saveModal();
    const createRequest = await createRequestPromise;
    const createResponse = await createResponsePromise;

    const reqBody = createRequest.postDataJSON();
    const resBody = await createResponse.json().catch(() => null);
    console.log(
      `POST /calendar/tasks: status=${createResponse.status()}, success=${resBody?.success}, ` +
      `tagIds=${JSON.stringify(reqBody?.tagIds)}, itemPlanningTagId=${reqBody?.itemPlanningTagId}`
    );

    expect(createResponse.status()).toBe(200);
    expect(resBody?.success).toBeTruthy();
    expect(Array.isArray(reqBody.tagIds)).toBeTruthy();
    expect(reqBody.tagIds.length).toBe(2);
    for (const id of reqBody.tagIds) {
      expect(typeof id).toBe('number');
    }
    expect(
      typeof reqBody.itemPlanningTagId,
      `POST itemPlanningTagId should be a number; got ${JSON.stringify(reqBody.itemPlanningTagId)} (full body: ${JSON.stringify(reqBody)})`
    ).toBe('number');
    expect(
      reqBody.itemPlanningTagId,
      'POST itemPlanningTagId should be > 0 (an actual tag id)'
    ).toBeGreaterThan(0);
    expect(
      reqBody.descriptionHtml,
      `POST descriptionHtml should be "${description}"; got ${JSON.stringify(reqBody.descriptionHtml)}`
    ).toBe(description);

    // ------------------------------------------------------------------
    // Step 8: chip rendered on calendar
    // ------------------------------------------------------------------
    const visible = await calendarPage.waitForEvent(title, 20000);
    expect(visible, `Event "${title}" was not visible within 20s`).toBeTruthy();

    // ------------------------------------------------------------------
    // Step 9: preview → Edit
    // ------------------------------------------------------------------
    await calendarPage.openEventPreview(title);
    await page.waitForTimeout(500);
    await calendarPage.clickEditInPreview();

    // ------------------------------------------------------------------
    // Step 10: assert round-trip (the critical assertion)
    // ------------------------------------------------------------------
    const roundTripTitle = await calendarPage.getCreateModalTitle();
    expect(roundTripTitle).toBe(title);

    const roundTripPlanningTag = await calendarPage.getSelectValue('#calendarEventPlanningTag');
    console.log(`Round-trip planning tag: "${roundTripPlanningTag}"`);
    expect(roundTripPlanningTag.trim()).toBe(tagA);

    const roundTripTags = (
      await calendarPage.getMultiSelectValues('#calendarEventTags')
    ).map(s => s.trim()).sort();
    console.log(`Round-trip Set tags: ${JSON.stringify(roundTripTags)}`);
    expect(roundTripTags).toEqual([tagA, tagB].sort());

    const roundTripDescription = await page.locator('#calendarEventDescription').inputValue();
    console.log(`Round-trip description: "${roundTripDescription}"`);
    expect(roundTripDescription).toBe(description);

    // ------------------------------------------------------------------
    // Step 11: edit — new title, swap report tag to tagB, remove tagA chip
    // ------------------------------------------------------------------
    const editedTitle = `${title}-edited`;
    await page.locator('#calendarEventTitle').fill(editedTitle);

    await calendarPage.selectExistingPlanningTag(tagB);
    await calendarPage.removeTagChip(tagA);

    const editedDescription = `${description}-edited`;
    await page.locator('#calendarEventDescription').fill(editedDescription);

    // ------------------------------------------------------------------
    // Step 12: save edit (PUT) and assert request/response
    // ------------------------------------------------------------------
    const updateRequestPromise = page.waitForRequest(
      r =>
        r.url().includes('/api/backend-configuration-pn/calendar/tasks') &&
        r.method() === 'PUT',
      { timeout: 30000 }
    );
    const updateResponsePromise = page.waitForResponse(
      r =>
        r.url().includes('/api/backend-configuration-pn/calendar/tasks') &&
        r.request().method() === 'PUT',
      { timeout: 30000 }
    );
    // Modal close triggers loadTasks(), which POSTs /calendar/tasks/week.
    // Wait for that response before checking for the edited chip, otherwise
    // we race the refetch + Angular re-render and the old title can still
    // be on screen for a beat.
    const weekTasksAfterPutPromise = page.waitForResponse(
      r =>
        r.url().includes('/api/backend-configuration-pn/calendar/tasks/week') &&
        r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await calendarPage.saveModal();
    const updateRequest = await updateRequestPromise;
    const updateResponse = await updateResponsePromise;
    await weekTasksAfterPutPromise;
    await page.waitForTimeout(500); // let weekGrid re-render after response

    const putBody = updateRequest.postDataJSON();
    const putResBody = await updateResponse.json().catch(() => null);
    console.log(
      `PUT /calendar/tasks: status=${updateResponse.status()}, success=${putResBody?.success}, ` +
      `tagIds=${JSON.stringify(putBody?.tagIds)}, itemPlanningTagId=${putBody?.itemPlanningTagId}`
    );

    expect(updateResponse.status()).toBe(200);
    expect(putResBody?.success).toBeTruthy();
    expect(Array.isArray(putBody.tagIds)).toBeTruthy();
    expect(putBody.tagIds.length).toBe(1);
    expect(typeof putBody.tagIds[0]).toBe('number');
    expect(
      typeof putBody.itemPlanningTagId,
      `PUT itemPlanningTagId should be a number; got ${JSON.stringify(putBody.itemPlanningTagId)} (full body: ${JSON.stringify(putBody)})`
    ).toBe('number');
    expect(
      putBody.itemPlanningTagId,
      'PUT itemPlanningTagId should be > 0 (an actual tag id)'
    ).toBeGreaterThan(0);
    expect(
      putBody.descriptionHtml,
      `PUT descriptionHtml should be "${editedDescription}"; got ${JSON.stringify(putBody.descriptionHtml)}`
    ).toBe(editedDescription);

    // ------------------------------------------------------------------
    // Step 13: reopen edited event and confirm edited state persists
    // ------------------------------------------------------------------
    const editedVisible = await calendarPage.waitForEvent(editedTitle, 20000);
    expect(
      editedVisible,
      `Edited event "${editedTitle}" was not visible within 20s`
    ).toBeTruthy();

    await calendarPage.openEventPreview(editedTitle);
    await page.waitForTimeout(500);
    await calendarPage.clickEditInPreview();

    const editedRoundTripTitle = await calendarPage.getCreateModalTitle();
    expect(editedRoundTripTitle).toBe(editedTitle);

    const editedPlanningTag = await calendarPage.getSelectValue('#calendarEventPlanningTag');
    console.log(`Edited round-trip planning tag: "${editedPlanningTag}"`);
    expect(editedPlanningTag.trim()).toBe(tagB);

    const editedTags = (
      await calendarPage.getMultiSelectValues('#calendarEventTags')
    ).map(s => s.trim()).sort();
    console.log(`Edited round-trip Set tags: ${JSON.stringify(editedTags)}`);
    expect(editedTags).toEqual([tagB]);

    const editedRoundTripDescription = await page.locator('#calendarEventDescription').inputValue();
    console.log(`Edited round-trip description: "${editedRoundTripDescription}"`);
    expect(editedRoundTripDescription).toBe(editedDescription);
  });

  // Cleanup is best-effort. Each matrix slot runs against an ephemeral DB,
  // so leftover rows don't contaminate other jobs. Keep the whole block in
  // a single try/catch and cap with a race timeout so a hung action-menu
  // in cleanup never fails the suite.
  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage();
    const cleanup = async () => {
      await page.goto('http://localhost:4200');
      await new LoginPage(page).login();

      const workersPage = new BackendConfigurationPropertyWorkersPage(page);
      await workersPage.goToPropertyWorkers();
      await page.waitForTimeout(1000);
      await workersPage.clearTable();

      const propertiesPage = new BackendConfigurationPropertiesPage(page);
      await propertiesPage.goToProperties();
      await page.waitForTimeout(1000);
      await propertiesPage.clearTable();
    };
    try {
      await Promise.race([
        cleanup(),
        new Promise(resolve => setTimeout(resolve, 60000)),
      ]);
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    }
    try { await page.close(); } catch {}
  });
});
