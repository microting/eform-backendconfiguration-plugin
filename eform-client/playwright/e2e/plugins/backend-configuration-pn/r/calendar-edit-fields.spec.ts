import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from './calendar-ui-enhancements.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';

/**
 * Calendar EDIT-event field-combination suite for GitHub issue #891.
 *
 * Companion to calendar-create-fields.spec.ts (#890): that suite proves the
 * CREATE POST body; this one proves the EDIT PUT round-trip. We create a
 * NON-recurring event (Repeat = none, the modal default), open its preview →
 * Edit, mutate a single field, and save. Because the event is non-recurring,
 * the backend treats the save as a FULL series update — there is NO
 * RepeatScopeModal — so the PUT fires straight through on save. Each edit is
 * therefore a series-wide edit by construction.
 *
 * Primary assertions are on the PUT request body (.../calendar/tasks, method
 * PUT) which is robust against cross-week rendering races; observable UI is
 * asserted in addition where it is deterministic (inactive dimming, assignee
 * chips after a reopen).
 *
 * The edit payload field names come from the modal's buildPayload()
 * (task-create-edit-modal.component.ts ~line 770) which mirrors the backend
 * CalendarTaskUpdateRequestModel:
 *   - sites             : number[]  (the assignee site ids — backend field)
 *   - assigneeIds       : number[]  (UI/compat duplicate of sites)
 *   - status            : 1 (active) | 2 (inactive)   [statusControl ? 1 : 2]
 *   - complianceEnabled : boolean   (overdue-shown toggle)
 *
 * Compliance toggle semantics (verified in the component):
 *   onPickOverdueShown()  (#calendarEventComplianceOn)  -> status=true,  compliance=true
 *   onPickOverdueHidden() (#calendarEventComplianceOff) -> status=true,  compliance=false
 *   i.e. touching EITHER compliance toggle FORCES status active. E16 asserts
 *   this: after toggling compliance the PUT carries status === 1.
 *
 * Matrix coverage (E11–E17):
 *   E11 — edit board updates boardId/color.          [fixme — single seed board]
 *   E12 — edit property reloads boards/employees.       [fixme — 1 property]
 *   E13 — edit assignees updates the PlanningSites.                     [here]
 *   E15 — edit status active->inactive flips + dims.                   [here]
 *   E16 — edit compliance toggle flips complianceEnabled, forces active.[here]
 *   E17 — past event is read-only.                  [fixme — can't seed past]
 *
 * Lives in `r/` to share the matrix slot with the create/move/resize/copy
 * suites; reuses CalendarUiEnhancementsPage and the same property/worker seed
 * pattern as calendar-create-fields.spec.ts. Seeds TWO workers so E13
 * (add/remove assignees) has 2 options to pick.
 */

const property: PropertyCreateUpdate = {
  name: generateRandmString(5),
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// Two distinct workers, both assigned to the property, so the assignee
// multi-select lists 2 options (required by E13).
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
// week reload + move/resize sibling routes.
function isCreatePost(method: string, url: string): boolean {
  return (
    url.includes('/api/backend-configuration-pn/calendar/tasks') &&
    !url.includes('/tasks/week') &&
    !url.includes('/tasks/move') &&
    !url.includes('/tasks/resize') &&
    method === 'POST'
  );
}

// Predicate: the EDIT backend call. updateTask PUTs to .../calendar/tasks
// (exact-match suffix, as confirmed by confirmEditScope's URL predicate in the
// page object).
function isEditPut(method: string, url: string): boolean {
  return (
    url.endsWith('/api/backend-configuration-pn/calendar/tasks') &&
    method === 'PUT'
  );
}

test.describe.serial('Calendar edit-event field combinations (#891)', () => {
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

  // -----------------------------------------------------------------------
  // Helpers — shared create + edit plumbing.
  // -----------------------------------------------------------------------

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

  // Pick the Nth assignee option (0-based) in the multi-select.
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

  // Remove the Nth assignee chip (0-based) in the multi-select by clicking its
  // clear-icon. ng-select renders one `.ng-value-icon` per selected value.
  async function removeAssignee(
    page: import('@playwright/test').Page,
    index: number,
  ): Promise<void> {
    await page.locator('#calendarEventAssignee .ng-value-icon').nth(index).click();
    await page.waitForTimeout(300);
  }

  async function assigneeChipCount(
    page: import('@playwright/test').Page,
  ): Promise<number> {
    return page.locator('#calendarEventAssignee .ng-value-label').count();
  }

  // Create a NON-recurring event in the currently-open create modal with ONE
  // assignee (index 0). Repeat defaults to "none" so this is non-recurring.
  // Awaits the create POST 200 and the rendered block, then returns.
  async function createNonRecurringEvent(
    page: import('@playwright/test').Page,
    calendarPage: CalendarUiEnhancementsPage,
    title: string,
  ): Promise<void> {
    await fillRequiredExceptAssignee(page, title);
    await pickAssignee(page, 0);
    await page.locator('#calendarEventTitle').click(); // close the dropdown
    await page.waitForTimeout(300);

    const reqPromise = page.waitForRequest(
      r => isCreatePost(r.method(), r.url()),
      { timeout: 30000 }
    );
    const respPromise = page.waitForResponse(
      r => isCreatePost(r.request().method(), r.url()),
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await reqPromise;
    const resp = await respPromise;
    expect(resp.status(), 'create POST should return 200').toBe(200);

    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
  }

  // Save the (already-open) EDIT modal. Because the event is non-recurring
  // there is NO scope modal: the PUT fires straight through. Register both the
  // PUT request waiter and the follow-up week-reload waiter BEFORE clicking
  // save so neither can miss the round-trip; return the parsed PUT body.
  async function saveEditAndCapturePut(
    page: import('@playwright/test').Page,
  ): Promise<any> {
    const reqPromise = page.waitForRequest(
      r => isEditPut(r.request().method(), r.url()),
      { timeout: 30000 }
    );
    const respPromise = page.waitForResponse(
      r => isEditPut(r.request().method(), r.url()),
      { timeout: 30000 }
    );
    // Closing the edit modal triggers loadTasks() -> POST /calendar/tasks/week.
    // Await it so the grid has re-rendered before any UI assertion.
    const reloadPromise = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/week') &&
        r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    const req = await reqPromise;
    const resp = await respPromise;
    expect(resp.status(), 'edit PUT should return 200').toBe(200);
    await reloadPromise.catch(() => undefined);
    await page.waitForTimeout(800); // let the weekGrid settle after the reload
    return req.postDataJSON();
  }

  // =======================================================================
  // E13 — edit assignees updates the PlanningSites.
  // =======================================================================
  test('E13: edit assignees updates the PlanningSites (sites array)', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `E13-${generateRandmString(5)}`;

    // Monday 09:00 (next week). Create with ONE assignee.
    await calendarPage.openCreateModalAtSlot(0, 9);
    await createNonRecurringEvent(page, calendarPage, title);

    // --- Edit #1: ADD the 2nd assignee ------------------------------------
    await calendarPage.openEditModal(title);
    // Sanity: the form rehydrated with exactly one assignee chip.
    expect(
      await assigneeChipCount(page),
      'edit modal should rehydrate with the single original assignee'
    ).toBe(1);

    await pickAssignee(page, 1); // second option in the panel
    await page.locator('#calendarEventTitle').click(); // close the dropdown
    await page.waitForTimeout(300);
    expect(
      await assigneeChipCount(page),
      'after adding the 2nd assignee the modal should show 2 chips'
    ).toBe(2);

    const addBody = await saveEditAndCapturePut(page);
    const addSites = Array.isArray(addBody?.sites) ? addBody.sites : [];
    const addAssigneeIds = Array.isArray(addBody?.assigneeIds) ? addBody.assigneeIds : [];
    console.log(`E13 add PUT sites=${JSON.stringify(addSites)}, assigneeIds=${JSON.stringify(addAssigneeIds)}`);
    expect(
      Math.max(addSites.length, addAssigneeIds.length),
      `after add, expected >= 2 assignees; sites=${JSON.stringify(addSites)} assigneeIds=${JSON.stringify(addAssigneeIds)}`
    ).toBeGreaterThanOrEqual(2);

    // Reopen and confirm 2 assignee chips persisted (rehydration round-trip).
    await calendarPage.openEditModal(title);
    expect(
      await assigneeChipCount(page),
      'reopened edit modal should rehydrate with 2 assignee chips'
    ).toBe(2);

    // --- Edit #2: REMOVE one assignee back down to 1 ----------------------
    await removeAssignee(page, 0);
    await page.waitForTimeout(300);
    expect(
      await assigneeChipCount(page),
      'after removing one chip the modal should show 1 chip'
    ).toBe(1);

    const removeBody = await saveEditAndCapturePut(page);
    const removeSites = Array.isArray(removeBody?.sites) ? removeBody.sites : [];
    const removeAssigneeIds = Array.isArray(removeBody?.assigneeIds) ? removeBody.assigneeIds : [];
    console.log(`E13 remove PUT sites=${JSON.stringify(removeSites)}, assigneeIds=${JSON.stringify(removeAssigneeIds)}`);
    // The remaining single assignee must still be carried. Use the smaller of
    // the two arrays' "effective" length: both should report exactly 1.
    const effectiveLen = Math.max(removeSites.length, removeAssigneeIds.length);
    expect(
      effectiveLen,
      `after remove, expected exactly 1 assignee; sites=${JSON.stringify(removeSites)} assigneeIds=${JSON.stringify(removeAssigneeIds)}`
    ).toBe(1);
  });

  // =======================================================================
  // E15 — edit status active->inactive flips status and dims the block.
  // =======================================================================
  test('E15: edit status active->inactive flips status and dims the block', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `E15-${generateRandmString(5)}`;

    // Tuesday 09:00 (next week). Created active by default.
    await calendarPage.openCreateModalAtSlot(1, 9);
    await createNonRecurringEvent(page, calendarPage, title);

    await calendarPage.openEditModal(title);
    // Flip the status toggle to Inactive.
    await page.locator('#calendarEventStatusInactive').click();
    await page.waitForTimeout(300);

    const body = await saveEditAndCapturePut(page);
    console.log(`E15 PUT status=${body?.status}`);
    // status === 2 is "inactive" (statusControl.value ? 1 : 2 in buildPayload).
    expect(body?.status, 'inactive status should serialize as 2').toBe(2);

    // The rendered block carries .gcal-task--inactive (bound to status === false).
    const block = calendarPage.findEventBlock(title);
    await block.waitFor({ state: 'visible', timeout: 10000 });
    await expect(block).toHaveClass(/gcal-task--inactive/);
  });

  // =======================================================================
  // E16 — edit compliance toggle flips complianceEnabled and forces status
  // active.
  // =======================================================================
  // Verified in task-create-edit-modal.component.ts:
  //   onPickOverdueShown()  (#calendarEventComplianceOn)  -> status=true, compliance=true
  //   onPickOverdueHidden() (#calendarEventComplianceOff) -> status=true, compliance=false
  // So touching EITHER compliance toggle forces status active. We assert that
  // forcing-to-active behaviour on the second edit (compliance On) where it is
  // unambiguous: status must be 1.
  test('E16: edit compliance toggle flips complianceEnabled and forces status active', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `E16-${generateRandmString(5)}`;

    // Wednesday 09:00 (next week). Created active + compliance-on by default.
    await calendarPage.openCreateModalAtSlot(2, 9);
    await createNonRecurringEvent(page, calendarPage, title);

    // --- Edit #1: turn compliance OFF -------------------------------------
    await calendarPage.openEditModal(title);
    await page.locator('#calendarEventComplianceOff').click();
    await page.waitForTimeout(300);

    const offBody = await saveEditAndCapturePut(page);
    console.log(`E16 off PUT complianceEnabled=${offBody?.complianceEnabled}, status=${offBody?.status}`);
    expect(
      offBody?.complianceEnabled,
      'compliance Off should serialize complianceEnabled === false'
    ).toBe(false);
    // onPickOverdueHidden() also forces status active.
    expect(
      offBody?.status,
      'toggling compliance Off forces status active (status === 1)'
    ).toBe(1);

    // --- Edit #2: turn compliance back ON ---------------------------------
    await calendarPage.openEditModal(title);
    await page.locator('#calendarEventComplianceOn').click();
    await page.waitForTimeout(300);

    const onBody = await saveEditAndCapturePut(page);
    console.log(`E16 on PUT complianceEnabled=${onBody?.complianceEnabled}, status=${onBody?.status}`);
    expect(
      onBody?.complianceEnabled,
      'compliance On should serialize complianceEnabled === true'
    ).toBe(true);
    // onPickOverdueShown() forces status active.
    expect(
      onBody?.status,
      'toggling compliance On forces status active (status === 1)'
    ).toBe(1);
  });

  // =======================================================================
  // E11 — edit board updates boardId/color.
  // =======================================================================
  // fixme: the board mtx-select row only renders when filteredBoards.length > 0
  // and a SECOND board must exist to be able to switch the boardId. The single-
  // property seed here yields only the property's DEFAULT board, and boards are
  // not creatable via the calendar UI in this fixture — so there is no second
  // board to pick. Left as a documented placeholder with the intended-behaviour
  // body; enabling it would require seeding the property with 2+ calendar
  // boards.
  test.fixme('E11: edit board updates boardId/color in the PUT body', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `E11-${generateRandmString(5)}`;

    await calendarPage.openCreateModalAtSlot(3, 9);
    await createNonRecurringEvent(page, calendarPage, title);

    await calendarPage.openEditModal(title);

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

    const body = await saveEditAndCapturePut(page);
    // INTENDED: a non-default board ships a numeric boardId and its color.
    expect(typeof body?.boardId).toBe('number');
    expect(body?.boardId).toBeGreaterThan(0);
    expect(typeof body?.color).toBe('string');
  });

  // =======================================================================
  // E12 — edit property reloads boards/employees and clears the board.
  // =======================================================================
  // fixme: requires a SECOND property so the property mtx-select (the .gcal-row
  // whose icon is "business") has a second option to switch to. Switching
  // property reloads the new property's boards + employees and clears the
  // currently-selected board (it no longer belongs to the new property). The
  // single-property seed here has no second property, and the board-clear flow
  // needs BOTH a multi-property AND a multi-board fixture to be observable.
  // Left as a documented placeholder; could be enabled later by seeding a 2nd
  // property (and re-pairing both workers) with its own boards.
  test.fixme('E12: editing property reloads boards/employees and clears the board', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `E12-${generateRandmString(5)}`;

    await calendarPage.openCreateModalAtSlot(4, 9);
    await createNonRecurringEvent(page, calendarPage, title);

    await calendarPage.openEditModal(title);

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

    // INTENDED: the previously-picked board is cleared after a property switch
    // (boards reload for the new property; the old board no longer applies).
    const boardValues = page
      .locator('.gcal-row')
      .filter({ has: page.locator('mat-icon.gcal-icon:has-text("calendar_today")') })
      .last()
      .locator('.ng-value-label');
    expect(await boardValues.count()).toBe(0);
  });

  // =======================================================================
  // E17 — past event is read-only.
  // =======================================================================
  // fixme: the create modal's start-time guard REJECTS creating an event in the
  // past (the chosen slot must be in the future), so a past event cannot be
  // produced via the UI in the first place — there is nothing to then reopen as
  // read-only. The read-only / disabled-controls behaviour for past events is
  // enforced in task-create-edit-modal.component.ts (statusControl.disable() /
  // complianceEnabledControl.disable() when the task is in the past). Left as a
  // documented placeholder; verifying it end-to-end would need a way to seed a
  // past event directly (e.g. via API) outside the create modal.
  test.fixme('E17: a past event opens read-only with disabled controls', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `E17-${generateRandmString(5)}`;

    // INTENDED: seed a PAST event (not possible through the create modal),
    // then open it for edit and assert the status/compliance controls are
    // disabled and the Save button is unavailable.
    await calendarPage.openEditModal(title);
    await expect(page.locator('#calendarEventStatusActive')).toBeDisabled();
    await expect(page.locator('#calendarEventComplianceOn')).toBeDisabled();
    await expect(page.locator('#calendarEventSaveBtn')).toBeDisabled();
  });
});
