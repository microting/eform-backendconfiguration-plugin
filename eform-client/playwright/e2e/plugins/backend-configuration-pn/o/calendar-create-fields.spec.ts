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
 * Calendar create-event field-combination suite for GitHub issue #890.
 *
 * Proves the create modal wires the optional/secondary fields correctly into
 * the create POST (.../calendar/tasks) request body. Where the rendered grid
 * state is also observable (inactive dimming) we assert that too, but the
 * primary assertions are on the POST body — that is robust against cross-week
 * rendering races. Each test uses a DISTINCT weekday + hour + unique title so
 * the serial suite never collides on a shared week.
 *
 * The create payload field names are taken from the modal's buildPayload()
 * (task-create-edit-modal.component.ts ~line 711) which mirrors the backend
 * CalendarTaskCreateRequestModel:
 *   - sites         : number[]  (the assignee site ids — backend field)
 *   - assigneeIds   : number[]  (UI/compat duplicate of sites)
 *   - status        : 1 (active) | 2 (inactive)
 *   - complianceEnabled : boolean (overdue-shown toggle; defaults true)
 *   - boardId       : number     (selected board)
 *   - color         : string     (derived from the selected board)
 *
 * Matrix coverage (C04, C25–C34):
 *   C04 — multiple assignees sent in the create payload.                 [here]
 *   C25 — all-day event.                                       [fixme — modal]
 *   C26 — status=Inactive persists + renders dimmed block.               [here]
 *   C27 — compliance defaults to enabled.                                [here]
 *   C28 — compliance can be disabled.                                    [here]
 *   C29 — non-default board.                       [fixme — single seed board]
 *   C30 — property switch mid-form.                       [fixme — 1 property]
 *   C33 — create at a future time today succeeds.                        [here]
 *   C34 — edge-of-month monthly clamp.            [fixme — multi-month nav]
 *
 * Lives in `r/` to share the matrix slot with the move/resize/edit-scope/
 * copy suites; reuses CalendarUiEnhancementsPage and the same property/worker
 * seed pattern as calendar-move.spec.ts. This suite seeds TWO workers on the
 * property so C04 (multi-assignee) has 2 options to pick.
 */

const property: PropertyCreateUpdate = {
  name: generateRandmString(5),
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// Two distinct workers, both assigned to the property, so the assignee
// multi-select lists 2 options (required by C04).
const workerA: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};

const workerB: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};

let seeded = false;

// Predicate: the create backend call (POST .../calendar/tasks) excluding the
// week reload + move/resize sibling routes. Shared by the request- and
// response-waiters below.
function isCreatePost(method: string, url: string): boolean {
  return (
    url.includes('/api/backend-configuration-pn/calendar/tasks') &&
    !url.includes('/tasks/week') &&
    !url.includes('/tasks/move') &&
    !url.includes('/tasks/resize') &&
    method === 'POST'
  );
}

test.describe.serial('Calendar create-event field combinations (#890)', () => {
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
  // Seed test — property + TWO workers. Runs first via describe.serial.
  // -----------------------------------------------------------------------
  test('seed: create property + two workers', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(workerA);
    await workersPage.create(workerB);

    seeded = true;

    // Confirm the assignee dropdown lists 2 options. Open the calendar,
    // select the property, open a create modal and count .ng-option entries
    // in the assignee panel.
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
    // Create mode places the cursor in Titel so the user can type
    // immediately (2026-07-17 design; edit/copy modes do NOT autofocus).
    await expect(page.locator('#calendarEventTitle')).toBeFocused();
    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    const optionCount = await page.locator('.ng-dropdown-panel .ng-option').count();
    expect(
      optionCount,
      `assignee dropdown should list both seeded workers; got ${optionCount}`
    ).toBeGreaterThanOrEqual(2);
    await calendarPage.closeEventModal();
  });

  // Fill the required fields (title + first eForm + first planning tag) in the
  // already-open create modal. Leaves the assignee EMPTY so callers control
  // assignee selection (single vs multi). Does NOT save.
  async function fillRequiredExceptAssignee(
    page: import('@playwright/test').Page,
    title: string,
  ): Promise<void> {
    await page.locator('#calendarEventTitle').fill(title);

    const eform = page.locator('#calendarEventEform');
    await eform.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);

    const planningTag = page.locator('#calendarEventPlanningTag');
    await planningTag.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);
  }

  // Pick the Nth assignee option (0-based) in the multi-select. Opens the
  // panel each call (a multi-select keeps the panel after a pick, but
  // re-opening is harmless and makes the call self-contained).
  async function pickAssignee(
    page: import('@playwright/test').Page,
    index: number,
  ): Promise<void> {
    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').nth(index).click();
    await page.waitForTimeout(300);
  }

  // Capture the create POST request body, click save, and assert a 200
  // response. Returns the parsed request body for field assertions. Registers
  // both waiters BEFORE clicking save so neither can miss the round-trip.
  async function saveAndCaptureCreate(
    page: import('@playwright/test').Page,
  ): Promise<any> {
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
    return req.postDataJSON();
  }

  // =======================================================================
  // C04 — multiple assignees are all sent in the create payload.
  // =======================================================================
  test('C04: multiple assignees are all sent in the create payload', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `C04-${generateRandmString(5)}`;

    // Monday 09:00 (next week).
    await calendarPage.openCreateModalAtSlot(0, 9);
    await fillRequiredExceptAssignee(page, title);

    // Pick TWO assignees from the multi-select.
    await pickAssignee(page, 0);
    await pickAssignee(page, 1);
    // Close the dropdown by clicking the title input.
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    const body = await saveAndCaptureCreate(page);
    console.log(`C04 POST body sites=${JSON.stringify(body?.sites)}, assigneeIds=${JSON.stringify(body?.assigneeIds)}`);

    // Backend field is `sites`; the modal also ships `assigneeIds` as a
    // UI/compat duplicate. Assert at least one of them carries >= 2 ids.
    const sites = Array.isArray(body?.sites) ? body.sites : [];
    const assigneeIds = Array.isArray(body?.assigneeIds) ? body.assigneeIds : [];
    expect(
      Math.max(sites.length, assigneeIds.length),
      `expected >= 2 assignees; sites=${JSON.stringify(sites)} assigneeIds=${JSON.stringify(assigneeIds)}`
    ).toBeGreaterThanOrEqual(2);

    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
  });

  // =======================================================================
  // C26 — status=Inactive persists and renders a dimmed block.
  // =======================================================================
  test('C26: status=Inactive persists and renders a dimmed block', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `C26-${generateRandmString(5)}`;

    // Tuesday 09:00 (next week).
    await calendarPage.openCreateModalAtSlot(1, 9);
    await fillRequiredExceptAssignee(page, title);
    await pickAssignee(page, 0);
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    // Flip the status toggle to Inactive.
    await page.locator('#calendarEventStatusInactive').click();
    await page.waitForTimeout(300);

    const body = await saveAndCaptureCreate(page);
    console.log(`C26 POST body status=${body?.status}`);

    // status === 2 is "inactive" (statusControl.value ? 1 : 2 in buildPayload).
    expect(body?.status, 'inactive status should serialize as 2').toBe(2);

    // The rendered block carries .gcal-task--inactive (bound to
    // task.status === false).
    await page.waitForTimeout(1500);
    const block = calendarPage.findEventBlock(title);
    await block.waitFor({ state: 'visible', timeout: 10000 });
    // The class lives on the .task-block element itself.
    await expect(block).toHaveClass(/gcal-task--inactive/);
  });

  // =======================================================================
  // C27 — compliance defaults to enabled.
  // =======================================================================
  test('C27: compliance defaults to enabled', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `C27-${generateRandmString(5)}`;

    // Wednesday 09:00 (next week). Do NOT touch the compliance toggles.
    await calendarPage.openCreateModalAtSlot(2, 9);
    await fillRequiredExceptAssignee(page, title);
    await pickAssignee(page, 0);
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    const body = await saveAndCaptureCreate(page);
    console.log(`C27 POST body complianceEnabled=${body?.complianceEnabled}`);

    expect(body?.complianceEnabled, 'compliance should default to true').toBe(true);

    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
  });

  // =======================================================================
  // C28 — compliance can be disabled.
  // =======================================================================
  test('C28: compliance can be disabled', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `C28-${generateRandmString(5)}`;

    // Thursday 09:00 (next week).
    await calendarPage.openCreateModalAtSlot(3, 9);
    await fillRequiredExceptAssignee(page, title);
    await pickAssignee(page, 0);
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    // Turn compliance (overdue-shown) OFF.
    await page.locator('#calendarEventComplianceOff').click();
    await page.waitForTimeout(300);

    const body = await saveAndCaptureCreate(page);
    console.log(`C28 POST body complianceEnabled=${body?.complianceEnabled}`);

    expect(body?.complianceEnabled, 'compliance should serialize as false').toBe(false);

    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
  });

  // =======================================================================
  // C33 — create at a future time TODAY succeeds (current week, not next).
  // =======================================================================
  // The create-time guard rejects a start in the past, so the chosen hour
  // must be a future band on TODAY. We derive it from the wall clock and clamp
  // into a valid grid band. If no future hour remains today (run very late in
  // the day) there is no deterministic future slot, so the row is fixme'd.
  test('C33: create at a future time today succeeds', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `C33-${generateRandmString(5)}`;

    const now = new Date();
    // Today's column on a Mon..Sun (0..6) grid.
    const todayDayIndex = (now.getDay() + 6) % 7;
    // A future hour today: at least one hour ahead, clamped to a 23:00 ceiling
    // so it stays on-grid. If the clamp would not actually be in the future
    // (run at/after 22:00), skip with a clear message — there is no
    // deterministic future slot today.
    const futureHour = now.getHours() + 2;
    test.skip(
      futureHour > 23,
      `no future hour remains on today (now=${now.getHours()}:00) — C33 needs a future slot today`
    );
    const hour = Math.min(23, futureHour);

    // Create directly on the CURRENT week's today column at a future hour.
    // openCreateModalOnCurrentWeek does NOT advance the week, so the event
    // lives on today's column and needs no navigation to assert. (Using
    // openCreateModalAtSlot would create on NEXT week's same weekday instead.)
    await calendarPage.openCreateModalOnCurrentWeek(todayDayIndex, hour);
    await fillRequiredExceptAssignee(page, title);
    await pickAssignee(page, 0);
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    const body = await saveAndCaptureCreate(page);
    console.log(`C33 POST body startHour=${body?.startHour}`);

    // The event is on today's column of the current week — assert it renders.
    await page.waitForTimeout(1000);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
    expect(await calendarPage.getEventDayIndex(title)).toBe(todayDayIndex);
  });

  // =======================================================================
  // C25 — all-day event.
  // =======================================================================
  // fixme: the create modal ALWAYS sets a concrete start/end time (the slot
  // click seeds startHour + duration and the time mtx-selects are required),
  // so there is no UI affordance to create an all-day event (no
  // CalendarConfiguration / all-day flag exists in the modal). An all-day
  // event is therefore not creatable via the modal; this row is left as a
  // documented placeholder. The intended behaviour is captured below for when
  // an all-day affordance lands.
  test.fixme('C25: all-day event is created without a time band', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `C25-${generateRandmString(5)}`;

    await calendarPage.openCreateModalAtSlot(4, 9);
    await fillRequiredExceptAssignee(page, title);
    await pickAssignee(page, 0);
    await page.locator('#calendarEventTitle').click();

    // INTENDED: toggle an "all-day" control (does not exist yet), then assert
    // the POST body carries an all-day flag / a full-day duration band.
    const body = await saveAndCaptureCreate(page);
    expect(body?.allDay ?? false).toBe(true);
  });

  // =======================================================================
  // C29 — non-default board.
  // =======================================================================
  // fixme: the board row only renders when filteredBoards.length > 0
  // (task-create-edit-modal.component.html ~line 395), and the board
  // mtx-select has NO stable id. The seed here creates a single property with
  // its default board only, so there is no SECOND board to pick — the board
  // row may not even render. C29 is only testable when the property has 2+
  // boards (not produced by this seed). Left as a documented placeholder with
  // the intended-behaviour body; multi-board provisioning would be needed in
  // the seed to implement it.
  test.fixme('C29: a non-default board is sent in the create payload', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `C29-${generateRandmString(5)}`;

    await calendarPage.openCreateModalAtSlot(5, 9);
    await fillRequiredExceptAssignee(page, title);
    await pickAssignee(page, 0);

    // INTENDED: open the board mtx-select (the .gcal-row whose icon is
    // calendar_today, after the compliance row) and pick the SECOND board.
    const boardRow = page
      .locator('.gcal-row')
      .filter({ has: page.locator('mat-icon.gcal-icon:has-text("calendar_today")') })
      .last();
    await boardRow.locator('.ng-select-container').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').nth(1).click();
    await page.locator('#calendarEventTitle').click();

    const body = await saveAndCaptureCreate(page);
    // INTENDED: a non-default board ships a numeric boardId and its color.
    expect(typeof body?.boardId).toBe('number');
    expect(body?.boardId).toBeGreaterThan(0);
    expect(typeof body?.color).toBe('string');
  });

  // =======================================================================
  // C30 — property switch mid-form.
  // =======================================================================
  // fixme: requires a SECOND property assigned to the same workers so the
  // property mtx-select (the .gcal-row whose icon is "business"; no stable id)
  // has a second option to switch to. The seed here creates a single property,
  // so there is no second property to switch to. Adding a second seeded
  // property + re-pairing both workers is non-trivial in this suite's
  // beforeable scope, so this is left as a documented placeholder with the
  // intended-behaviour body. The switch should reload boards/employees and
  // clear the previously-picked assignees.
  test.fixme('C30: switching property mid-form reloads boards/employees and clears assignees', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `C30-${generateRandmString(5)}`;

    await calendarPage.openCreateModalAtSlot(6, 9);
    await fillRequiredExceptAssignee(page, title);
    await pickAssignee(page, 0);

    // INTENDED: open the property mtx-select (business icon row) and pick the
    // OTHER property.
    const propertyRow = page
      .locator('.gcal-row')
      .filter({ has: page.locator('mat-icon.gcal-icon:has-text("business")') })
      .first();
    await propertyRow.locator('.ng-select-container').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').nth(1).click();
    await page.waitForTimeout(500);

    // INTENDED: the assignee selection is cleared after a property switch
    // (employees reload for the new property).
    const assigneeValues = page.locator('#calendarEventAssignee .ng-value-label');
    expect(await assigneeValues.count()).toBe(0);
  });

  // =======================================================================
  // C34 — edge-of-month monthly clamp.
  // =======================================================================
  // fixme: a 31st-of-month monthly rule must clamp on short months (Feb/Apr/
  // …). Asserting the clamp end-to-end needs multi-month navigation across
  // several short months, which is slow and flaky in e2e and is already
  // covered deterministically by calendar-repeat.service.spec (unit). Left as
  // a documented placeholder with the intended-behaviour body.
  test.fixme('C34: a 31st monthly rule clamps on short months', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `C34-${generateRandmString(5)}`;

    await calendarPage.openCreateModalAtSlot(0, 10);
    await fillRequiredExceptAssignee(page, title);
    await pickAssignee(page, 0);

    // INTENDED: pick a monthly-by-day rule anchored on day 31, save, then
    // navigate to a short month and assert the occurrence renders on the
    // clamped last day rather than spilling into the next month.
    await calendarPage.selectRepeatPreset('monthlyByDay');
    const body = await saveAndCaptureCreate(page);
    expect(body?.dayOfMonth).toBe(31);
  });
});
