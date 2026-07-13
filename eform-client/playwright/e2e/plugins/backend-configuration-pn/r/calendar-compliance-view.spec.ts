import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString, selectDateRangeOnNewDatePicker } from '../../../helper-functions';
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
 * Calendar Compliance view (4th view-mode option) suite.
 *
 * Lives in `r/` alongside calendar-ui-enhancements.spec.ts — this shard has
 * the fewest calendar specs (1) in the n–w calendar range, so it absorbs the
 * new suite without unbalancing the matrix.
 *
 * Self-seeded: creates its own property + worker + a single one-off task
 * next week, then MATERIALISES an open Compliance row by clicking the task's
 * `.completion-btn` — the same on-demand path exercised by
 * `p/calendar-complete.spec.ts` (X06/X04): `PrepareComplete` calls
 * `IEventDeployService.EnsureComplianceForOccurrenceAsync` synchronously
 * inside the POST `/tasks/{id}/prepare-complete` request that opens the
 * combined complete modal (`app-calendar-complete-event-modal`) whenever no
 * Compliance row exists yet for the occurrence, regardless of how far in the
 * future the date is (BackendConfigurationCalendarService.cs PrepareComplete,
 * ~line 3389-3512). We never save the modal's embedded eForm (its mandatory
 * fields make submission impractical in e2e, see calendar-complete.spec.ts
 * file header) — cancelling it via `#completeCancelBtn` leaves the
 * materialised Compliance row in the OPEN (not completed) state, which is
 * exactly the fixture this suite needs.
 *
 * The Compliance report's built-in period presets are backward-looking from
 * today only for the "done" status; for open/all statuses they span
 * symmetrically into the future (calendar-compliance-view.component.ts
 * `get dateTo()`). These tests predate that and need a deterministic window
 * anyway, so every report-fetching test here uses the
 * "Set period" (custom) preset with an explicit range spanning
 * [today-2, nextMonday+8] (the whole retry week, see selectCompliancePeriodCustom)
 * instead.
 *
 * Locators favour class/id matching throughout. The one exception convention
 * already established by sibling suites (switchToScheduleView/
 * switchToDayView in calendar-ui-enhancements.page.ts) is picking mtx-select
 * dropdown options by POSITION rather than translated text — the option
 * order is fixed in source (calendar-header.component.ts viewModeOptions;
 * calendar-compliance-view.component.ts statusOptions/periodOptions), so
 * positional picks are locale-independent and at least as robust as text
 * matching. This suite follows that same convention for the view-mode,
 * status, and period selects.
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
let complianceSeeded = false;
// Reassigned by the seed test once an OPEN compliance row is actually
// materialised (see that test's header comment for why the title isn't
// known up front).
let taskTitle = `CMPL-${generateRandmString(6)}`;

// ---------------------------------------------------------------------------
// Date helpers — local, deterministic, no calendar-day network round trips.
// ---------------------------------------------------------------------------

function mondayOfThisWeekLocal(): Date {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  const dow = d.getDay(); // 0=Sun..6=Sat
  const diff = dow === 0 ? -6 : 1 - dow;
  d.setDate(d.getDate() + diff);
  return d;
}

function addDays(d: Date, n: number): Date {
  const out = new Date(d);
  out.setDate(out.getDate() + n);
  return out;
}

// openCreateModalAtSlot(0, hour) advances the calendar one week (chevron
// click) then clicks dayOffset=0, i.e. Monday of NEXT week — matches
// mondayOfNextWeek() convention used in calendar-ui-enhancements.spec.ts.
const TASK_DATE = addDays(mondayOfThisWeekLocal(), 7);

// ---------------------------------------------------------------------------
// Compliance-view-local helpers (kept out of the shared page object —
// calendar-ui-enhancements.page.ts is intentionally self-contained per its
// own header comment; this suite stays independently maintainable too).
// ---------------------------------------------------------------------------

/** The 0-indexed `.text-field--rounded` filter blocks inside `.compliance-filters`,
 * in template order: 0=property, 1=board, 2=tag, 3=status, 4=employee, 5=period,
 * 6=export-format. */
function complianceFilterSelect(page: Page, index: number) {
  return page
    .locator('.compliance-filters .text-field--rounded')
    .nth(index)
    .locator('mtx-select .ng-select-container');
}

async function pickComplianceDropdownOption(page: Page, filterIndex: number, optionIndex: number): Promise<void> {
  await complianceFilterSelect(page, filterIndex).click();
  await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
  await page.locator('.ng-dropdown-panel .ng-option').nth(optionIndex).click();
  await page.waitForTimeout(200);
}

/** Switch the calendar header's view-mode select to Compliance.
 * viewModeOptions order (calendar-header.component.ts ngOnInit):
 * 0=Day, 1=Week, 2=List, 3=Compliance. Scoped to the header so it never
 * collides with the many mtx-selects inside `.compliance-filters` once
 * the view has switched. */
async function switchToComplianceView(page: Page): Promise<void> {
  const viewSelect = page
    .locator('app-calendar-header .text-field--rounded mtx-select .ng-select-container')
    .first();
  await viewSelect.click();
  await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
  await page.locator('.ng-dropdown-panel .ng-option').nth(3).click();
  await page.locator('.compliance-filters').waitFor({ state: 'visible', timeout: 10000 });
  await page.waitForTimeout(300);
}

/** statusOptions order (calendar-compliance-view.component.ts ngOnInit):
 * 0='open' (Ikke udførte opgaver), 1='done' (Udførte opgaver), 2='all' (Alle opgaver). */
async function selectComplianceStatus(page: Page, option: 'open' | 'done' | 'all'): Promise<void> {
  const indexByStatus: Record<string, number> = { open: 0, done: 1, all: 2 };
  await pickComplianceDropdownOption(page, 3, indexByStatus[option]);
}

/** periodOptions order: 0='1 month', 1='3 months', 2='6 months', 3='12 months',
 * 4='Year to date' (Ãr til dato), 5='custom' (Sæt periode — reveals the
 * date-range picker). All built-in presets clamp dateTo to today, so every
 * report-fetching test needs 'custom' to reach the next-week seeded task. */
async function selectCompliancePeriodCustom(page: Page): Promise<void> {
  await pickComplianceDropdownOption(page, 5, 5);
  await page.locator('.compliance-custom-range').waitFor({ state: 'visible', timeout: 5000 });

  const from = addDays(new Date(), -2);
  // The seed's eForm-retry loop may place the surviving task on ANY weekday
  // of next week (Mon=TASK_DATE .. Sun=TASK_DATE+6), so the range must cover
  // the whole week plus margin.
  const to = addDays(TASK_DATE, 8);

  await page.locator('.compliance-custom-range mat-datepicker-toggle button').click();
  await selectDateRangeOnNewDatePicker(
    page,
    from.getFullYear(), from.getMonth() + 1, from.getDate(),
    to.getFullYear(), to.getMonth() + 1, to.getDate(),
  );
  await page.waitForTimeout(300);
}

/** Full flow: switch to Compliance, pick a status, set the wide custom
 * period, click "Vis rapport" (Show report), and await the report POST. */
async function openComplianceReport(page: Page, status: 'open' | 'done' | 'all'): Promise<void> {
  await switchToComplianceView(page);
  await selectComplianceStatus(page, status);
  await selectCompliancePeriodCustom(page);

  const reportWait = page.waitForResponse(
    r => r.url().includes('/api/backend-configuration-pn/calendar/compliance-report')
      && r.request().method() === 'POST',
    { timeout: 30000 }
  );
  await page.locator('.compliance-show-report-btn').click();
  await reportWait;
  await page.waitForTimeout(500);
}

function complianceRowByTitle(page: Page, title: string) {
  return page.locator('.compliance-row').filter({ hasText: title });
}

// Ensure the combined complete-event modal (app-calendar-complete-event-modal)
// that appears when completing an event has a worker selected — mirrors
// handleWorkerSelectModal in p/calendar-complete.spec.ts.
async function handleWorkerSelectModal(page: Page): Promise<void> {
  const modal = page.locator('app-calendar-complete-event-modal');
  await modal.waitFor({ state: 'visible', timeout: 10000 });
  const workerSelect = page.locator('#completeWorkerSelect');
  await workerSelect.waitFor({ state: 'visible', timeout: 10000 });
  // The preselect (single-assignee seed) applies asynchronously once the
  // linked-sites call resolves — give it a moment before falling back to an
  // explicit pick.
  const preselected = await workerSelect
    .locator('.ng-value-label')
    .first()
    .waitFor({ state: 'attached', timeout: 3000 })
    .then(() => true)
    .catch(() => false);
  if (!preselected) {
    await workerSelect.click();
    const seededOption = page
      .locator('.ng-dropdown-panel .ng-option')
      .filter({ hasText: `${worker.name} ${worker.surname}` })
      .first();
    if ((await seededOption.count()) > 0) {
      await seededOption.click();
    } else {
      await page.locator('.ng-dropdown-panel .ng-option').first().click();
    }
  }
}

// Cancel the combined complete-event modal without saving — mirrors
// cancelCompleteModal in p/calendar-complete.spec.ts. Leaves the
// materialised Compliance row in the OPEN (not completed) state.
async function closeComplianceCaseDialog(page: Page): Promise<void> {
  const modal = page.locator('app-calendar-complete-event-modal').first();
  if ((await modal.count()) === 0) return;
  const cancelBtn = page.locator('#completeCancelBtn');
  if ((await cancelBtn.count()) > 0) {
    await cancelBtn.click();
  } else {
    await page.keyboard.press('Escape');
  }
  await modal.waitFor({ state: 'detached', timeout: 5000 }).catch(() => undefined);
}

/**
 * Create + save a one-off event in an already-open create-modal, picking the
 * eForm at `eformIndex` (clamped to the list's actual length) rather than
 * always the first option. Otherwise identical to
 * CalendarUiEnhancementsPage.fillAndSaveEvent (first planning tag + first
 * assignee, awaits the create POST + rendered block).
 *
 * Needed because whether completing the seeded task opens the
 * compliance-case dialog (leaving the row OPEN) or completes it in place
 * (row ends up DONE) depends entirely on whether the CHOSEN eForm template
 * has mandatory fields (BackendConfigurationCalendarService.ToggleComplete →
 * HasMandatoryFields) — a property of environment seed data, not something
 * this suite controls. The seed test below tries eForm options in order
 * until it finds one that yields an OPEN row.
 */
async function fillAndSaveEventWithEformIndex(
  page: Page,
  calendarPage: CalendarUiEnhancementsPage,
  title: string,
  eformIndex: number,
): Promise<void> {
  await page.locator('#calendarEventTitle').fill(title);

  const eform = page.locator('#calendarEventEform');
  await eform.click();
  await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
  const eformOptions = page.locator('.ng-dropdown-panel .ng-option');
  const eformCount = await eformOptions.count();
  await eformOptions.nth(Math.min(eformIndex, Math.max(0, eformCount - 1))).click();
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
      && r.request().method() === 'POST',
    { timeout: 30000 }
  );
  await page.locator('#calendarEventSaveBtn').click();
  await createResp;
  await page.waitForTimeout(1500);
  await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
}

test.describe.serial('Calendar Compliance view', () => {
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

  // =========================================================================
  // Seed 1 — property + worker.
  // =========================================================================
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

  // =========================================================================
  // Seed 2 — a one-off task next week, materialised into an OPEN Compliance
  // row via the completion-indicator on-demand deploy path.
  //
  // Clicking .completion-btn now ALWAYS opens the combined complete modal
  // (app-calendar-complete-event-modal) — the modal's own POST
  // /tasks/{id}/prepare-complete materialises the Compliance row server-side
  // (EnsureComplianceForOccurrenceAsync) unconditionally, regardless of
  // whether the picked eForm template has mandatory fields. Cancelling the
  // modal (never saving it) therefore always leaves the row in the OPEN
  // state — the fixture this suite needs. The eForm-index retry loop below
  // predates this (it used to matter which eForm produced the RequiresForm
  // dialog vs. an in-place auto-complete); it is kept as a harmless no-op
  // safety net since the first attempt now always succeeds.
  // =========================================================================
  test('seed: create a next-week task and materialise an open compliance row', async ({ page }) => {
    test.setTimeout(180000);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    const MAX_ATTEMPTS = 7; // one per weekday slot of next week (Mon..Sun)
    let openTitle: string | null = null;

    for (let attempt = 0; attempt < MAX_ATTEMPTS; attempt++) {
      const title = `${taskTitle}-${attempt}`;
      if (attempt === 0) {
        await calendarPage.openCreateModalAtSlot(0, 9); // advances to next week, Monday 09:00
      } else {
        await calendarPage.openCreateModalOnCurrentWeek(attempt, 9); // Tue..Sun, same (already-advanced) week
      }
      await fillAndSaveEventWithEformIndex(page, calendarPage, title, attempt);

      const block = calendarPage.findEventBlock(title);
      await expect(block).toBeVisible();

      const completionWait = page.waitForResponse(
        r => /\/api\/backend-configuration-pn\/calendar\/tasks\/\d+\/prepare-complete/.test(r.url())
          && r.request().method() === 'POST',
        { timeout: 30000 }
      );
      await block.locator('.completion-btn').click();
      await completionWait;
      await handleWorkerSelectModal(page);

      const opened = await page
        .locator('app-calendar-complete-event-modal')
        .waitFor({ state: 'visible', timeout: 4000 })
        .then(() => true)
        .catch(() => false);

      if (opened) {
        // Cancel WITHOUT saving so the Compliance row materialised
        // server-side (EnsureComplianceForOccurrenceAsync) stays in the
        // OPEN state, which is what this suite needs as its fixture.
        await closeComplianceCaseDialog(page);
        openTitle = title;
        break;
      }
      // Not expected to happen now that the modal always opens, but kept as
      // a defensive fallback — try the next eForm option.
    }

    expect(
      openTitle,
      `None of the first ${MAX_ATTEMPTS} eForm options produced an open ` +
      `(mandatory-fields) compliance row in this environment — cannot seed ` +
      `the fixture this suite needs.`
    ).not.toBeNull();

    taskTitle = openTitle!;
    complianceSeeded = true;
  });

  // =========================================================================
  // View-mode switch — filter bar + placeholder visible, sidebar hidden.
  // =========================================================================
  test('switching to Compliance shows the filter bar + placeholder and hides the sidebar', async ({ page }) => {
    expect(complianceSeeded).toBe(true);

    await expect(page.locator('app-calendar-sidebar')).toHaveCount(1); // still week view

    await switchToComplianceView(page);

    await expect(page.locator('.compliance-filters')).toBeVisible();
    await expect(page.locator('.compliance-placeholder')).toBeVisible();
    // Sidebar component is *ngIf removed entirely in compliance mode.
    await expect(page.locator('app-calendar-sidebar')).toHaveCount(0);
    await expect(page.locator('.calendar-shell.sidebar-closed')).toHaveCount(1);
  });

  // =========================================================================
  // "Alle opgaver" (all) + a period wide enough for the seeded task.
  // =========================================================================
  test('Alle opgaver + wide custom period + Vis rapport shows the seeded row under a week header', async ({ page }) => {
    expect(complianceSeeded).toBe(true);

    await openComplianceReport(page, 'all');

    await expect(page.locator('.compliance-week-header').first()).toBeVisible();
    await expect(page.locator('.compliance-row').first()).toBeVisible();
    await expect(complianceRowByTitle(page, taskTitle)).toHaveCount(1);
    // Open (not-yet-completed) row must not carry the --done modifier.
    await expect(complianceRowByTitle(page, taskTitle)).not.toHaveClass(/compliance-row--done/);
  });

  // =========================================================================
  // "Udførte opgaver" (done) — the still-open seeded task must be absent.
  // =========================================================================
  test('Udførte opgaver status excludes the still-open seeded task', async ({ page }) => {
    expect(complianceSeeded).toBe(true);

    await openComplianceReport(page, 'done');

    await expect(complianceRowByTitle(page, taskTitle)).toHaveCount(0);
  });

  // =========================================================================
  // Delete button → confirm dialog → Annuller keeps the row.
  // =========================================================================
  test('delete button opens a confirm dialog; Annuller keeps the row', async ({ page }) => {
    expect(complianceSeeded).toBe(true);

    await openComplianceReport(page, 'all');

    const row = complianceRowByTitle(page, taskTitle);
    await expect(row).toHaveCount(1);
    // The delete button only renders for rows where !row.completed.
    await expect(row.locator('.compliance-delete-btn')).toBeVisible();

    await row.locator('.compliance-delete-btn').click();

    const dialog = page.locator('mat-dialog-container');
    await dialog.waitFor({ state: 'visible', timeout: 5000 });
    const actionButtons = dialog.locator('mat-dialog-actions button');
    await expect(actionButtons).toHaveCount(2);

    // Template order (calendar-compliance-view.component.html
    // #deleteConfirmTpl): Cancel first, Delete second.
    await actionButtons.nth(0).click();
    await dialog.waitFor({ state: 'detached', timeout: 5000 }).catch(() => undefined);

    // Row untouched — no delete request was ever sent.
    await expect(complianceRowByTitle(page, taskTitle)).toHaveCount(1);
  });

  // =========================================================================
  // Pagination bar — visible with the "Showing X–Y of Z" text.
  // =========================================================================
  test('pagination bar is visible with compliance-showing text', async ({ page }) => {
    expect(complianceSeeded).toBe(true);

    await openComplianceReport(page, 'all');

    await expect(page.locator('.compliance-pagination')).toBeVisible();
    const showing = page.locator('.compliance-showing');
    await expect(showing).toBeVisible();
    const text = ((await showing.textContent()) ?? '').trim();
    expect(text.length).toBeGreaterThan(0);
    // "{from}–{to} of {total}" — at least one digit must be present.
    expect(text).toMatch(/\d/);
  });
});
