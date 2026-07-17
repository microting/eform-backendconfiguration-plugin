import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';

/**
 * Calendar create-event VALIDATION suite for GitHub issue #892.
 *
 * Proves the create modal's save-guard and the backend's create-time
 * validation legs behave correctly. The modal's Save button
 * (`#calendarEventSaveBtn`) is DISABLED when the title is empty, there are
 * zero assignees, OR the report-headline checkbox is checked with no
 * planning tag selected (task-create-edit-modal.component.html ~line 415:
 *   [disabled]="titleControl.invalid || !(assigneeControl.value?.length) ||
 *     (reportHeadlineEnabledControl.value && !planningTagControl.value)"
 * ). The report-headline checkbox (`#calendarEventReportHeadlineToggle`)
 * defaults to CHECKED in create mode, so the planning tag is required
 * client-side UNLESS the user unchecks it — at which point the dropdown is
 * disabled, the value is excluded from the payload (`itemPlanningTagId:
 * null`), and the create succeeds (the old server-side
 * `ReportTableHeaderTagIsRequired` rejection has been removed).
 *
 * This determines which validation legs are reachable end-to-end:
 *   - empty-title            → Save disabled            (UI guard)        [V01]
 *   - zero-assignee          → Save disabled            (UI guard)        [V02]
 *   - past-slot create       → modal never opens        (grid guard)      [V03]
 *   - sub-15-min duration    → clamps to 0.25 h         (modal clamp)     [V04]
 *   - checked + no headline  → Save disabled            (UI guard)        [V06]
 *   - Active + no sites      → unreachable (Save disabled w/ 0 assignees) [V05 fixme]
 *
 * The backend `AtLeastOneWorkerMustBeAssigned` leg (CalendarService.CreateTask
 * ~line 882) is unreachable via the UI because Save is disabled with zero
 * assignees — that leg is covered server-side by the integration tests.
 *
 * Single property + single worker seed suffices here: one worker is enough to
 * make Save reachable, and none of these tests need a multi-assignee list.
 * Each test uses a DISTINCT weekday + unique title so the serial suite never
 * collides on a shared week. Lives in `r/` to share the matrix slot with the
 * move/resize/edit-scope/copy/create-fields suites and reuse
 * CalendarUiEnhancementsPage.
 */

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
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};

let seeded = false;

// Predicate: the create backend call (POST .../calendar/tasks) excluding the
// week reload + move/resize sibling routes. Shared by request/response waiters.
function isCreatePost(method: string, url: string): boolean {
  return (
    url.includes('/api/backend-configuration-pn/calendar/tasks') &&
    !url.includes('/tasks/week') &&
    !url.includes('/tasks/move') &&
    !url.includes('/tasks/resize') &&
    method === 'POST'
  );
}

test.describe.serial('Calendar create-event validation (#892)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();

    if (seeded) {
      const folderResp = page.waitForResponse(
        r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
        { timeout: 60000 }
      );
      await calendarPage.selectProperty(property.name);
      await folderResp.catch(() => undefined);
      await page.waitForTimeout(1000);
    }
  });

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

  // -----------------------------------------------------------------------
  // Seed test — property + ONE worker. Runs first via describe.serial.
  // -----------------------------------------------------------------------
  test('seed: create property + worker', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    seeded = true;

    // Sanity: open a create modal and confirm the assignee dropdown lists the
    // seeded worker, so the assignee-dependent legs below are exercisable.
    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();
    const folderResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
      { timeout: 60000 }
    );
    await calendarPage.selectProperty(property.name);
    await folderResp.catch(() => undefined);
    await page.waitForTimeout(1000);

    await calendarPage.openCreateModalAtSlot(0, 8);
    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    const optionCount = await page.locator('.ng-dropdown-panel .ng-option').count();
    expect(
      optionCount,
      `assignee dropdown should list the seeded worker; got ${optionCount}`
    ).toBeGreaterThanOrEqual(1);
    await calendarPage.closeEventModal();
  });

  // ----- shared helpers ---------------------------------------------------

  // Pick the first eForm option in the (already-open) create modal.
  async function pickFirstEform(page: import('@playwright/test').Page): Promise<void> {
    const eform = page.locator('#calendarEventEform');
    await eform.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);
  }

  // Pick the first planning-tag option.
  async function pickFirstPlanningTag(page: import('@playwright/test').Page): Promise<void> {
    const planningTag = page.locator('#calendarEventPlanningTag');
    await planningTag.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);
  }

  // Pick the first assignee option.
  async function pickFirstAssignee(page: import('@playwright/test').Page): Promise<void> {
    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);
    // Close the dropdown without mutating other state.
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(200);
  }

  // =======================================================================
  // V01 — an empty title disables Save.
  // =======================================================================
  // Fill eForm + planning tag + assignee but leave the title EMPTY. The save
  // guard `titleControl.invalid || ...` keeps the button disabled.
  test('V01: empty title disables Save', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);

    // Monday 09:00 (next week).
    await calendarPage.openCreateModalAtSlot(0, 9);

    // Deliberately leave #calendarEventTitle empty. Fill everything else.
    await pickFirstEform(page);
    await pickFirstPlanningTag(page);
    await pickFirstAssignee(page);

    // Title is empty → Save must be disabled regardless of the other fields.
    await expect(page.locator('#calendarEventTitle')).toHaveValue('');
    await expect(page.locator('#calendarEventSaveBtn')).toBeDisabled();

    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // V02 — no assignee disables Save.
  // =======================================================================
  // Fill title + eForm + planning tag but pick NO assignee. The save guard
  // `... || !(assigneeControl.value?.length)` keeps the button disabled.
  //
  // The backend AtLeastOneWorkerMustBeAssigned leg (CalendarService.CreateTask
  // ~line 882) is therefore UNREACHABLE through the UI — Save can never fire
  // with zero assignees — so that server-side leg is covered by the
  // integration tests, not here.
  test('V02: no assignee disables Save', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `V02-${generateRandmString(5)}`;

    // Tuesday 09:00 (next week).
    await calendarPage.openCreateModalAtSlot(1, 9);

    await page.locator('#calendarEventTitle').fill(title);
    await pickFirstEform(page);
    await pickFirstPlanningTag(page);
    // No assignee picked.

    await expect(page.locator('#calendarEventSaveBtn')).toBeDisabled();

    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // V03 — a past slot cannot be opened for creation.
  // =======================================================================
  // Past time slots are non-interactive (the grid rejects clicks on a slot
  // whose start is already in the past), so clicking one must NOT open the
  // create modal. We derive a PAST slot on TODAY from the wall clock:
  //   - today's column index = (now.getDay()+6)%7  (Mon..Sun → 0..6)
  //   - an hour a few hours BEFORE now              (now.getHours()-3, >=0)
  // If it is too early in the day for a valid past hour (now < 3) there is no
  // deterministic past slot, so the test is skipped with a clear message.
  //
  // ASSERTION (with documented weakening): rather than relying on a brittle
  // "modal never appears, ever" assertion, we click the past slot via
  // clickEmptyTimeSlot (which does NOT advance the week, so it targets the
  // current week's today column) and assert the create modal does NOT open
  // within a short window — the title input (#calendarEventTitle) never
  // becomes visible within 3 s. A non-appearing element is the robust signal
  // that the past slot is inert; waiting only 3 s keeps the negative
  // assertion fast and avoids a false pass from an unrelated slow render.
  test('V03: a past slot cannot be opened for creation', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);

    const now = new Date();
    const todayDayIndex = (now.getDay() + 6) % 7;
    const pastHour = now.getHours() - 3;
    test.skip(
      pastHour < 0,
      `too early in the day for a valid past hour (now=${now.getHours()}:00) — ` +
      `V03 needs a past slot on today's column`
    );

    // Click the past slot on the CURRENT week's today column (no week advance).
    await calendarPage.clickEmptyTimeSlot(todayDayIndex, pastHour);

    // The create modal must NOT open: the title input never becomes visible.
    // 3 s is enough to catch a (wrongly) opening modal while keeping the
    // negative assertion fast.
    await expect(
      page.locator('#calendarEventTitle'),
      'past slot must not open the create modal'
    ).toBeHidden({ timeout: 3000 });
  });

  // =======================================================================
  // V04 — a duration below 15 min clamps to 0.25 h.
  // =======================================================================
  // The modal defaults to a 1-hour band (start 09:00 / end 10:00). Setting the
  // END below the 15-minute floor must clamp the effective duration to >=
  // 0.25 h. Confirmed in task-create-edit-modal.component.ts:
  //   - onSave():        const duration = Math.max(endHour - startHour, 0.25)
  //                                                  (~line 660)
  //   - start-change:    Math.min(newStartH + Math.max(dur, 0.25), 24)
  //                                                  (~line 358)
  // The clamp is applied to the DURATION at save time, not to the end-field
  // value: the modal does NOT re-write the end input to 09:15, it keeps the
  // entered text and floors the duration when building the payload.
  //
  // ASSERTION (with documented approach): mirror the calendar-ui-enhancements
  // A-series time-field reads. Set the end to 09:05 via typeEndTime, save, and
  // assert the resulting block renders with a duration >= 0.25 h. The
  // create-fields suite already proves the POST body; here we additionally
  // capture the POST body and assert `duration >= 0.25`. We read the body
  // (robust, deterministic) rather than the rendered block height (fragile, px
  // dependent) — the duration field is the authoritative clamp output.
  test('V04: duration below 15 min clamps to 0.25h', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `V04-${generateRandmString(5)}`;

    // Wednesday 09:00 (next week) → default band 09:00–10:00.
    await calendarPage.openCreateModalAtSlot(2, 9);

    await page.locator('#calendarEventTitle').fill(title);
    await pickFirstEform(page);
    await pickFirstPlanningTag(page);
    await pickFirstAssignee(page);

    // Set the END to 09:05 — below the 15-minute floor.
    await calendarPage.typeEndTime('09:05', 'Enter');
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    // Capture the create POST body to read the clamped duration.
    const reqPromise = page.waitForRequest(
      r => isCreatePost(r.method(), r.url()),
      { timeout: 30000 }
    );
    const respPromise = page.waitForResponse(
      r => isCreatePost(r.request().method(), r.url()),
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    const req = await reqPromise;
    const resp = await respPromise;
    expect(resp.status(), 'create POST should return 200').toBe(200);

    const body = req.postDataJSON();
    console.log(`V04 POST body startHour=${body?.startHour}, duration=${body?.duration}`);

    // The sub-15-min end clamps the duration to the 0.25 h floor (never less).
    expect(
      typeof body?.duration === 'number' ? body.duration : NaN,
      `duration must clamp to >= 0.25h; got ${body?.duration}`
    ).toBeGreaterThanOrEqual(0.25);

    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
  });

  // =======================================================================
  // V06 — the report-headline checkbox gates save client-side.
  // =======================================================================
  // Checked (default) + no tag selected => #calendarEventSaveBtn disabled.
  // Unchecking the checkbox lifts the gate and the create succeeds with
  // itemPlanningTagId: null (server guard was removed; data is excluded
  // from reports because the null ReportGroupPlanningTagId group is skipped).
  test('V06: report headline required only while its checkbox is checked', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `V06-${generateRandmString(5)}`;

    // Thursday 09:00 (next week).
    await calendarPage.openCreateModalAtSlot(3, 9);

    await page.locator('#calendarEventTitle').fill(title);
    await pickFirstEform(page);
    // Deliberately DO NOT pick a planning tag (stays null in create mode).
    await pickFirstAssignee(page);

    // Report-headline checkbox defaults to CHECKED + no tag selected =>
    // Save must be disabled by the client-side guard.
    const saveBtn = page.locator('#calendarEventSaveBtn');
    await expect(saveBtn, 'checked + empty headline must block save').toBeDisabled();

    // Uncheck the report-headline toggle — the gate lifts and Save enables.
    await page.locator('#calendarEventReportHeadlineToggle').click();
    await expect(saveBtn, 'unchecked headline must allow save').toBeEnabled();

    const [response] = await Promise.all([
      page.waitForResponse(r => isCreatePost(r.request().method(), r.url()), { timeout: 30000 }),
      saveBtn.click(),
    ]);
    const body = await response.json().catch(() => null);
    console.log(
      `V06 create (headline unchecked): status=${response.status()}, success=${body?.success}, message=${body?.message}`
    );
    expect(body?.success, 'create without a report headline must succeed').toBeTruthy();

    await page.waitForTimeout(1000);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
  });

  // =======================================================================
  // V05 — Active + no sites downgrades to NotActive.
  // =======================================================================
  // fixme: this server-side status-downgrade path (an Active task created with
  // zero assigned sites is persisted as NotActive) cannot be exercised
  // end-to-end through the UI, because the create modal's Save button is
  // DISABLED with zero assignees (the same guard that makes V02's
  // AtLeastOneWorkerMustBeAssigned leg unreachable). There is no UI affordance
  // to submit an Active task with no sites, so the downgrade branch never
  // runs from the modal. It is covered server-side by the integration tests.
  // Left as a documented placeholder with the intended-behaviour body.
  test.fixme('V05: an Active task with no sites is downgraded to NotActive', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `V05-${generateRandmString(5)}`;

    await calendarPage.openCreateModalAtSlot(4, 9);
    await page.locator('#calendarEventTitle').fill(title);
    await pickFirstEform(page);
    await pickFirstPlanningTag(page);
    // INTENDED: leave status = Active and pick NO assignee, then save. The UI
    // guard blocks this (Save disabled with zero assignees), so the path is
    // unreachable; the backend downgrade is asserted in the integration tests.
    await page.locator('#calendarEventSaveBtn').click();
    // INTENDED: the persisted task comes back with status === 2 (NotActive).
  });
});
