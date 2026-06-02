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
 * Calendar DELETE scope suite for GitHub issue #895.
 *
 * Exercises the delete flow end-to-end from the preview popover's Delete
 * button:
 *   - ONE-OFF events open TaskDeleteModalComponent (`app-task-delete-modal`),
 *     a simple confirm dialog whose Delete button is `button.btn-delete`.
 *     The component hardcodes scope 'this'; scope is irrelevant for a one-off.
 *   - RECURRING events open RepeatScopeModalComponent with `{mode:'delete'}`
 *     — the SAME modal as move/resize, so `pickScopeInModal('this' |
 *     'thisAndFollowing' | 'all')` drives it. Confirming fires the delete and
 *     the grid reloads.
 *
 * The committed backend call is `PUT /api/backend-configuration-pn/calendar/
 * tasks/delete` (see BackendConfigurationPnCalendarService.deleteTask); the
 * follow-up `POST /tasks/week` reload re-renders the grid. The page-object
 * helpers confirmDeleteScope / confirmOneOffDelete await both before the
 * caller asserts (mirroring confirmEditScope + the move/resize reload-wait
 * pattern).
 *
 * Each test creates its own event with a UNIQUE title; recurring tests use
 * DISTINCT weekdays so a weekly series (which renders on its weekday across
 * EVERY visible week) never collides with another recurring test on the same
 * week index. After each destructive test the event is gone, which further
 * reduces cross-week collisions.
 *
 * The C# integration tests exhaustively cover the server delete-scope
 * semantics (single-exception-row de-duplication, etc.); this suite proves
 * the UI plumbing and the user-observable invariants.
 *
 * Matrix coverage (D01–D10):
 *   D06 — recurring → scope modal; one-off → delete modal.                 [here]
 *   D01/D09 — delete a one-off removes the event.                          [here]
 *   D02 — recurring scope=this hides only that occurrence.                 [here]
 *   D03 — recurring scope=thisAndFollowing truncates the series.           [here]
 *   D04 — thisAndFollowing from the FIRST occurrence deletes the series.   [here]
 *   D05 — recurring scope=all removes the whole series.                    [here]
 *   D07 — scope=this after a move keeps the occurrence gone.               [here]
 *   D08 — deleted occurrence stays gone across week navigation.            [here]
 *   D10 — delete a COMPLETED occurrence. Requires submitting the eForm
 *         completion dialog, which is not automatable in e2e (see
 *         calendar-complete.spec.ts) — test.fixme; covered server-side.    [fixme]
 *
 * Lives in `r/` to share the CI matrix slot with the resize / move /
 * edit-scope / copy suites; reuses CalendarUiEnhancementsPage and the same
 * property/worker seed pattern as calendar-move.spec.ts.
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

test.describe.serial('Calendar delete scope (#895)', () => {
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
  // Seed test — property + worker. Runs first via describe.serial.
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
  });

  // Helper — open create modal, save a 1-h NON-recurring event with the given
  // title at NEXT week's day-of-week `dayOffset` (0=Mon..6=Sun) at `hour`:00.
  // Mirrors createSimpleEvent in calendar-move.spec.ts. Leaves the calendar on
  // week +1 with the rendered block visible.
  async function createSimpleEvent(
    page: import('@playwright/test').Page,
    calendarPage: CalendarUiEnhancementsPage,
    title: string,
    dayOffset: number = 0,
    hour: number = 9,
  ): Promise<void> {
    await calendarPage.openCreateModalAtSlot(dayOffset, hour);
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

    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    const createResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
        && !r.url().includes('/tasks/week')
        && !r.url().includes('/tasks/move')
        && !r.url().includes('/tasks/resize')
        && !r.url().includes('/tasks/delete')
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await createResp;
    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
  }

  // Helper — create a WEEKLY recurring event on NEXT week's `dayOffset` at
  // `hour`:00. Mirrors createWeeklyEvent in calendar-move.spec.ts. Leaves the
  // calendar on week +1 with the block visible.
  async function createWeeklyEvent(
    page: import('@playwright/test').Page,
    calendarPage: CalendarUiEnhancementsPage,
    title: string,
    dayOffset: number,
    hour: number = 9,
  ): Promise<void> {
    await calendarPage.openCreateModalAtSlot(dayOffset, hour);
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

    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    // Weekly recurrence (index 2 in the repeat dropdown = 'weeklyOne').
    await calendarPage.setRepeatToWeekly();

    const createResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
        && !r.url().includes('/tasks/week')
        && !r.url().includes('/tasks/move')
        && !r.url().includes('/tasks/resize')
        && !r.url().includes('/tasks/delete')
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await createResp;
    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
  }

  // =======================================================================
  // D06 — modal routing: one-off opens the simple delete modal, recurring
  // opens the scope modal. Both are cancelled (no deletion) so the test
  // asserts only WHICH modal renders.
  // =======================================================================
  test('D06: recurring delete opens the scope modal; one-off opens the delete modal', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);

    // ----- one-off → TaskDeleteModal -----
    const oneOffTitle = `D06off-${generateRandmString(5)}`;
    const oneOffDay = 0; // Monday
    await createSimpleEvent(page, calendarPage, oneOffTitle, oneOffDay, 8);

    await calendarPage.openPreviewAndDelete(oneOffTitle);
    await expect(page.locator('app-task-delete-modal')).toBeVisible();
    await expect(page.locator('app-repeat-scope-modal')).toHaveCount(0);
    // Cancel — leaves the one-off intact (cleaned up by afterAll).
    await calendarPage.cancelOneOffDelete();

    // ----- recurring → RepeatScopeModal -----
    const recTitle = `D06rec-${generateRandmString(5)}`;
    const recDay = 1; // Tuesday — distinct from the one-off's Monday.
    await createWeeklyEvent(page, calendarPage, recTitle, recDay, 8);

    await calendarPage.openPreviewAndDelete(recTitle);
    await expect(page.locator('app-repeat-scope-modal')).toBeVisible();
    await expect(page.locator('app-task-delete-modal')).toHaveCount(0);
    // Cancel — leaves the recurring series intact.
    await calendarPage.cancelScopeModal();
  });

  // =======================================================================
  // D01/D09 — deleting a one-off removes the event entirely. Scope is
  // irrelevant for a one-off (the TaskDeleteModal hardcodes scope 'this').
  // =======================================================================
  test('D01/D09: deleting a one-off removes the event', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `D01-${generateRandmString(5)}`;
    const dayX = 2; // Wednesday

    await createSimpleEvent(page, calendarPage, title, dayX, 9);
    expect(await calendarPage.getEventDayIndex(title)).toBe(dayX);

    await calendarPage.openPreviewAndDelete(title);
    await calendarPage.confirmOneOffDelete();

    // The block is gone after the reload.
    expect(await calendarPage.findEventBlock(title).count()).toBe(0);
    expect(await calendarPage.getEventDayIndex(title)).toBe(-1);
  });

  // =======================================================================
  // Recurring deletes — create at week +1, advance to the anchor (week +3),
  // delete with a scope, then assert across weeks. Each uses a DISTINCT
  // weekday so the weekly series do not collide across visible weeks.
  // =======================================================================
  test.describe('Recurring event deletes (scope modal)', () => {
    test('D02: scope=this hides only that occurrence', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D02-${generateRandmString(5)}`;
      const dayX = 0; // Monday

      // Create weekly at week +1.
      await createWeeklyEvent(page, calendarPage, title, dayX, 9);

      // Advance two weeks → week +3 (the delete anchor).
      await calendarPage.navigateToNextWeek();
      await calendarPage.navigateToNextWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(dayX);

      // Delete only this occurrence.
      await calendarPage.openPreviewAndDelete(title);
      await calendarPage.confirmDeleteScope('this');

      // Week +3 (anchor): the occurrence is gone.
      expect(await calendarPage.getEventDayIndex(title)).toBe(-1);

      // Week +2 and +1: occurrences preserved.
      await calendarPage.navigateToPreviousWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(dayX);
      await calendarPage.navigateToPreviousWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(dayX);
    });

    test('D03: scope=thisAndFollowing truncates the series', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D03-${generateRandmString(5)}`;
      const dayX = 1; // Tuesday

      await createWeeklyEvent(page, calendarPage, title, dayX, 9);

      // Advance two weeks → week +3 (the delete anchor).
      await calendarPage.navigateToNextWeek();
      await calendarPage.navigateToNextWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(dayX);

      // Delete this occurrence and all following.
      await calendarPage.openPreviewAndDelete(title);
      await calendarPage.confirmDeleteScope('thisAndFollowing');

      // Week +3 (anchor) and week +4: gone.
      expect(await calendarPage.getEventDayIndex(title)).toBe(-1);
      await calendarPage.navigateToNextWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(-1);

      // Week +2 and +1 (before the anchor): preserved.
      await calendarPage.navigateToPreviousWeek(); // back to +3
      await calendarPage.navigateToPreviousWeek(); // +2
      expect(await calendarPage.getEventDayIndex(title)).toBe(dayX);
      await calendarPage.navigateToPreviousWeek(); // +1
      expect(await calendarPage.getEventDayIndex(title)).toBe(dayX);
    });

    // thisAndFollowing from the series' FIRST occurrence: because the
    // original date <= StartDate, there is nothing earlier to preserve, so the
    // whole series is removed (the server has no remaining occurrences to keep).
    test('D04: thisAndFollowing from the FIRST occurrence deletes the whole series', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D04-${generateRandmString(5)}`;
      const dayX = 2; // Wednesday

      // Create weekly at week +1 and DO NOT navigate away — week +1 is the
      // first occurrence (the series StartDate).
      await createWeeklyEvent(page, calendarPage, title, dayX, 9);
      expect(await calendarPage.getEventDayIndex(title)).toBe(dayX);

      // Delete this-and-following from the first occurrence.
      await calendarPage.openPreviewAndDelete(title);
      await calendarPage.confirmDeleteScope('thisAndFollowing');

      // Week +1 (anchor / series start): gone.
      expect(await calendarPage.getEventDayIndex(title)).toBe(-1);

      // Week +2 and +3: also gone — the entire series was removed.
      await calendarPage.navigateToNextWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(-1);
      await calendarPage.navigateToNextWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(-1);
    });

    test('D05: scope=all removes the whole series', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D05-${generateRandmString(5)}`;
      const dayX = 3; // Thursday

      await createWeeklyEvent(page, calendarPage, title, dayX, 9);

      // Advance two weeks → week +3 (the delete anchor).
      await calendarPage.navigateToNextWeek();
      await calendarPage.navigateToNextWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(dayX);

      // Delete the whole series.
      await calendarPage.openPreviewAndDelete(title);
      await calendarPage.confirmDeleteScope('all');

      // Week +3 (anchor) and +4: gone.
      expect(await calendarPage.getEventDayIndex(title)).toBe(-1);
      await calendarPage.navigateToNextWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(-1);

      // Weeks +2 and +1: also gone.
      await calendarPage.navigateToPreviousWeek(); // back to +3
      await calendarPage.navigateToPreviousWeek(); // +2
      expect(await calendarPage.getEventDayIndex(title)).toBe(-1);
      await calendarPage.navigateToPreviousWeek(); // +1
      expect(await calendarPage.getEventDayIndex(title)).toBe(-1);
    });

    test('D08: a deleted occurrence stays gone across week navigation', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D08-${generateRandmString(5)}`;
      const dayX = 4; // Friday

      await createWeeklyEvent(page, calendarPage, title, dayX, 9);

      // Advance two weeks → week +3 (the delete anchor).
      await calendarPage.navigateToNextWeek();
      await calendarPage.navigateToNextWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(dayX);

      // scope=this delete at the anchor.
      await calendarPage.openPreviewAndDelete(title);
      await calendarPage.confirmDeleteScope('this');
      expect(await calendarPage.getEventDayIndex(title)).toBe(-1);

      // Navigate AWAY (to week +2, then +1) and BACK to the anchor (+3).
      await calendarPage.navigateToPreviousWeek(); // +2 — still present
      expect(await calendarPage.getEventDayIndex(title)).toBe(dayX);
      await calendarPage.navigateToPreviousWeek(); // +1 — still present
      expect(await calendarPage.getEventDayIndex(title)).toBe(dayX);
      await calendarPage.navigateToNextWeek(); // +2
      await calendarPage.navigateToNextWeek(); // back to +3 (anchor)

      // The deleted occurrence is STILL gone on the anchor week.
      expect(await calendarPage.getEventDayIndex(title)).toBe(-1);
    });

    // D07 — delete scope=this AFTER a move on the same occurrence.
    //
    // Moving the anchor occurrence with scope=this creates an exception row in
    // the series; a subsequent scope=this delete on that same occurrence must
    // keep it gone (and must NOT duplicate the exception). The
    // "single exception row, not duplicated" guarantee is a DB-level invariant
    // not observable through the e2e UI — it is asserted server-side. Here we
    // assert ONLY the observable invariant: after the move-then-delete the
    // occurrence is gone, and earlier weeks (untouched by the move) are intact.
    test('D07: scope=this delete after a move keeps the occurrence gone', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D07-${generateRandmString(5)}`;
      const startDay = 5; // Saturday
      const movedDay = 6; // Sunday (the moved-to day for the exception)

      await createWeeklyEvent(page, calendarPage, title, startDay, 9);

      // Advance two weeks → week +3 (the move/delete anchor).
      await calendarPage.navigateToNextWeek();
      await calendarPage.navigateToNextWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(startDay);

      // MOVE the anchor occurrence cross-day with scope=this → creates an
      // exception. Scope modal pops → awaitReload:false, then pick 'this'.
      await calendarPage.dragEventToSlot(title, movedDay, 11, { awaitReload: false });
      const moveReload = page.waitForResponse(
        r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/week')
          && r.request().method() === 'POST',
        { timeout: 30000 }
      );
      await calendarPage.pickScopeInModal('this');
      await moveReload;
      await page.waitForTimeout(800);

      // The exception now sits on the moved day.
      expect(await calendarPage.getEventDayIndex(title)).toBe(movedDay);

      // Now delete that same (moved) occurrence with scope=this.
      await calendarPage.openPreviewAndDelete(title);
      await calendarPage.confirmDeleteScope('this');

      // The occurrence is gone on the anchor week (neither the moved day nor
      // the original day shows it).
      expect(await calendarPage.getEventDayIndex(title)).toBe(-1);

      // Earlier weeks (+2, +1) — untouched by the move/delete — still present
      // on the original Saturday.
      await calendarPage.navigateToPreviousWeek(); // +2
      expect(await calendarPage.getEventDayIndex(title)).toBe(startDay);
      await calendarPage.navigateToPreviousWeek(); // +1
      expect(await calendarPage.getEventDayIndex(title)).toBe(startDay);
    });
  });

  // =======================================================================
  // D10 — deleting a COMPLETED occurrence.
  //
  // Fully completing an event in e2e opens the eForm submission dialog and
  // requires submitting that form (see calendar-complete.spec.ts), which is
  // not automatable here. Without a genuinely completed occurrence the delete
  // path is indistinguishable from D02 (scope=this). The completed-occurrence
  // delete semantics are covered server-side.
  // =======================================================================
  test.fixme('D10: delete a completed occurrence', async () => {
    // Requires a fully-completed task: triggering completion opens the eForm
    // submission dialog (calendar-complete.spec.ts) and submitting it is not
    // automatable in e2e. Covered server-side.
  });
});
