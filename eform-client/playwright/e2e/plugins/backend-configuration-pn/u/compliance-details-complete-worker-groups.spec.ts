import { test, expect, Page, Locator } from '@playwright/test';
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
import {
  API_TIMEOUT,
  UI_TIMEOUT,
  ignoreUnhandledRejections,
  waitForApiResponse,
} from '../wait-helpers';

/**
 * Standalone Compliance page — DETALJER row → complete modal, worker grouping
 * (#1187). The Detaljer variant of L6 in `p/calendar-complete-layout.spec.ts`.
 *
 * Also carries two tests that need this file's fixture (one property, one OPEN
 * compliance row) and have no home anywhere else:
 *
 *  - the Detaljer DELETE-confirm test ported from
 *    `r/calendar-compliance-view.spec.ts` when #1170 deleted the calendar
 *    Compliance view mode;
 *  - the STATUS-filter round trip — the only place in the Playwright suite
 *    where changing `#complianceFilterStatus` is asserted to change the
 *    rendered row set (see the comment on that test).
 *
 * Clicking a not-completed Detaljer row opens the very same
 * `CalendarCompleteEventModalComponent` the calendar grid opens. Until #1187 the
 * row handed it `assigneeIds: []` (the row DTO carried worker NAMES only), so
 * the modal's empty-group guard always produced a flat list and nothing was
 * pre-selected. The row now carries `workerSiteIds`, so from Detaljer the
 * dropdown groups into "Tildelte medarbejdere" / "Øvrige medarbejdere" and a
 * single assignee is pre-selected — exactly as from the calendar.
 *
 * SELF-SEEDED, because shard `u` seeds no SQL (only shard `a` does). The
 * fixture is the one `s/compliance-overview.spec.ts` uses: property + workers through the
 * page objects, one-off tasks on the property's calendar, then an OPEN
 * Compliance row per task materialised by clicking the task's `.completion-btn`
 * (POST `/tasks/{id}/prepare-complete` runs `EnsureComplianceForOccurrenceAsync`
 * server-side) and CANCELLING the modal. One task → one Compliance row, which
 * respects the UNIQUE (PlanningId, Deadline) constraint on `Compliances`.
 *
 * #1300 — TWO tasks since the compliance pages stopped offering completion and
 * deletion for UNCOMPLETED FUTURE tasks: the main one (`TASK_TITLE`) is dated
 * TODAY, at a still-future hour so the grid accepts the slot click, because
 * every test below completes or deletes through it; the second
 * (`FUTURE_TASK_TITLE`) is dated next week and is the row the #1300 test
 * asserts is locked. Materialising the future one through the CALENDAR's
 * `.completion-btn` also proves the calendar still opens its early-completion
 * modal for a future occurrence. The suite is skipped when the run starts in
 * the last hour of the local day: there is no future slot left on today's
 * column (the grid silently rejects past-slot clicks).
 *
 * A SECOND worker is required, not incidental: the modal deliberately renders
 * an ungrouped list when either group would be empty, so with one worker who
 * is also the assignee there are no groups and every grouped assertion below
 * would be vacuous. Two workers give exactly one assignee and one remainder.
 *
 * `fillAndSaveEvent` assigns whichever worker is FIRST in the create-modal's
 * assignee dropdown, and this spec does not control that order. Instead of
 * assuming it, the test reads the assignee's name from the Detaljer row's own
 * worker chip (`workerNames[0]`, the same set that backs `workerSiteIds`) and
 * asserts it is one of the two seeded workers; the other seeded worker is then
 * the "Øvrige" one by construction.
 *
 * Local traps this file is written around: options are picked BY LABEL and
 * never by `nth()`; the selected value is read from `.ng-value-label`, never
 * `.ng-value` (whose innerText includes the × clear-icon glyph); a regex
 * `hasText`/`toHaveText` matches RAW text with padding, hence `/^\s*…\s*$/`;
 * the mtx-select panel is appended to `<body>`, so it is located from `page`.
 */

const BASE_URL = 'http://localhost:4200';
const PAGE_URL = `${BASE_URL}/plugins/backend-configuration-pn/compliance-report`;
const rand = generateRandmString(6).toLowerCase();
const TASK_TITLE = `DET-GRP-${rand}`;
const FUTURE_TASK_TITLE = `DET-FUT-${rand}`;

const property: PropertyCreateUpdate = {
  name: `DET-grp-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const worker: PropertyWorker = {
  name: `Wa${generateRandmString(4)}`,
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name!],
  workerEmail: `${generateRandmString(5)}@test.com`,
};

const otherWorker: PropertyWorker = {
  name: `Wb${generateRandmString(4)}`,
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name!],
  workerEmail: `${generateRandmString(5)}@test.com`,
};

const fullName = (w: PropertyWorker) => `${w.name} ${w.surname}`;

let propertiesSeeded = false;
let complianceSeeded = false;

// ---------------------------------------------------------------------------
// Dates — local and deterministic, mirroring `s/compliance-overview.spec.ts`.
// ---------------------------------------------------------------------------

function mondayOfThisWeekLocal(): Date {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  const dow = d.getDay(); // 0=Sun..6=Sat
  d.setDate(d.getDate() + (dow === 0 ? -6 : 1 - dow));
  return d;
}

function addDays(d: Date, n: number): Date {
  const out = new Date(d);
  out.setDate(out.getDate() + n);
  return out;
}

// #1300: the completable/deletable row is dated TODAY, on today's column of the
// CURRENT week, one hour after the run started (computed once, at load, so the
// slot is still in the future when the seed test reaches it).
const NOW_AT_LOAD = new Date();
const TODAY_COLUMN = (NOW_AT_LOAD.getDay() + 6) % 7; // Mon..Sun → 0..6
const TODAY_SEED_HOUR = NOW_AT_LOAD.getHours() + 1;
const NO_FUTURE_SLOT_TODAY = TODAY_SEED_HOUR > 23;

// `openCreateModalAtSlot(1, 9)` advances the calendar one week, then clicks the
// second visible day — i.e. TUESDAY of NEXT week, at 09:00. Tuesday, not
// Monday: from a Sunday evening that is still two days ahead, so it is a
// future date in Copenhagen too even when the browser runs on UTC.
const FUTURE_TASK_DATE = addDays(mondayOfThisWeekLocal(), 8);

// ---------------------------------------------------------------------------
// Page helpers
// ---------------------------------------------------------------------------

function isPrepareComplete(r: import('@playwright/test').Response): boolean {
  return (
    /\/api\/backend-configuration-pn\/calendar\/tasks\/\d+\/prepare-complete/.test(r.url()) &&
    r.request().method() === 'POST'
  );
}

/**
 * Login happens once per test in `beforeEach`, so this only navigates — a
 * second `LoginPage.login()` on an authenticated session hangs waiting for a
 * `#loginBtn` that is no longer on the page.
 */
async function goToCompliancePage(page: Page): Promise<void> {
  await page.goto(PAGE_URL);
  await page.locator('#complianceFilterProperty').waitFor({ state: 'visible', timeout: 60000 });
}

/** Narrow to the seeded property. Picked BY LABEL — never by nth() index. */
async function selectSeededProperty(page: Page): Promise<void> {
  await page.locator('#complianceFilterProperty').click();
  await page
    .locator('.ng-dropdown-panel .ng-option', { hasText: property.name! })
    .first()
    .click();
  await page.waitForTimeout(300);
}

/**
 * The built-in presets except "År til dato + 1 år" (#1299) are bounded ABOVE
 * by today and the future seed is next week; an explicit "Sæt periode" range
 * keeps this suite independent of that one preset and of the year boundary.
 */
async function selectPeriodCoveringSeed(page: Page): Promise<void> {
  await page.locator('#complianceFilterPeriod').click();
  await page
    .locator('.ng-dropdown-panel .ng-option', { hasText: 'Sæt periode' })
    .first()
    .click();
  await page
    .locator('.compliance-filters__custom-range')
    .waitFor({ state: 'visible', timeout: 10000 });

  const from = addDays(new Date(), -2);
  const to = addDays(FUTURE_TASK_DATE, 8);
  await page.locator('.compliance-filters__custom-range mat-datepicker-toggle button').click();
  await selectDateRangeOnNewDatePicker(
    page,
    from.getFullYear(), from.getMonth() + 1, from.getDate(),
    to.getFullYear(), to.getMonth() + 1, to.getDate(),
  );
  await page.waitForTimeout(300);
}

/**
 * `Opdater periode` — commits the staged custom range (#1185). Every other
 * filter change auto-fetches the active mode; "Sæt periode" is the one
 * exception, and the button exists ONLY while it is selected (which
 * `selectPeriodCoveringSeed` guarantees). In Detaljer the commit hits `/index`.
 */
async function showDetails(page: Page): Promise<void> {
  const button = page.locator('#complianceShowReportBtn');
  await expect(button).toHaveText(/^\s*Opdater periode\s*$/);
  const response = page.waitForResponse(
    r => r.url().includes('/api/backend-configuration-pn/compliance-report/index')
      && r.request().method() === 'POST',
    { timeout: 60000 },
  );
  await button.click();
  await response;
  await page.waitForTimeout(500);
}

/**
 * Land on the page in Detaljer, filtered to the seeded property over a window
 * containing the seed, under the default `Ikke udførte opgaver` status — which
 * is exactly the state the open seeded row lives in.
 */
async function openSeededDetails(page: Page): Promise<void> {
  await goToCompliancePage(page);
  // The property change auto-fetches Oversigt (#1185) — harmless here, it is
  // superseded by the custom period staged next, which fetches NOTHING until
  // the button commits it.
  await selectSeededProperty(page);
  await selectPeriodCoveringSeed(page);
  // Switch mode BEFORE committing: the status control is not shown in Oversigt (#1299),
  // and the commit fetches whichever mode is active. `showDetails` waits for
  // its own `/index` response, then the row assertions retry until it rendered.
  await page.locator('#complianceMode-details').click();
  await expect(page.locator('#complianceMode-details-button')).toHaveAttribute('aria-checked', 'true');
  await showDetails(page);
}

/**
 * A Detaljer row located by its TITLE CELL, matched EXACTLY — never by a bare
 * `hasText` on the whole row. A string `hasText` is a case-insensitive
 * SUBSTRING match over every cell, and the row's `.compliance-details__property`
 * cell prints `property.name` (`DET-grp-<rand>`), which case-insensitively
 * contains `TASK_TITLE` (`DET-GRP-<rand>`). With one seeded row that went
 * unnoticed; since #1300 seeded a second task on the same property, a whole-row
 * `hasText: TASK_TITLE` matched BOTH rows. The anchored regex is case-sensitive
 * and scoped to the title span, so neither task's row can match the other's.
 */
function rowByTitle(page: Page, title: string): Locator {
  return page.locator('.compliance-details__row').filter({
    has: page
      .locator('.compliance-details__title')
      .filter({ hasText: new RegExp(`^\\s*${escapeRegExp(title)}\\s*$`) }),
  });
}

function seededRow(page: Page): Locator {
  return rowByTitle(page, TASK_TITLE);
}

function futureRow(page: Page): Locator {
  return rowByTitle(page, FUTURE_TASK_TITLE);
}

/**
 * Moves `#complianceFilterStatus` and waits for the Detaljer refetch that the
 * change triggers.
 *
 * NO `Opdater periode` click: the button commits a STAGED range, and by the
 * time this runs `showDetails` has already committed one. With a valid
 * committed period every other filter change auto-fetches the active mode
 * after the ~300 ms debounce (`setFilter` → `scheduleFetch`, #1185) — the
 * behaviour `s/compliance-page-shell.spec.ts` pins for this very control.
 * Re-committing on top would put a second `/index` in flight behind the
 * assertions.
 *
 * The wait is armed BEFORE the gesture, because a request that fires after a
 * debounce cannot be observed by anything read at a fixed moment afterwards.
 * Options are matched BY LABEL with an ANCHORED regex, never by `nth()` and
 * never by a bare string: `hasText` is a case-insensitive SUBSTRING match, so
 * `'Udførte opgaver'` also matches `'Ikke udførte opgaver'` and the click
 * would land on whichever came first.
 */
async function selectStatusAndAwaitDetails(page: Page, label: string): Promise<void> {
  const anchored = new RegExp(`^\\s*${escapeRegExp(label)}\\s*$`);
  const refetch = waitForApiResponse(
    page,
    `compliance /index refetch after selecting status "${label}"`,
    r => r.url().includes('/api/backend-configuration-pn/compliance-report/index')
      && r.request().method() === 'POST',
    API_TIMEOUT,
  );
  ignoreUnhandledRejections(refetch);

  // Both clicks carry an EXPLICIT budget. `playwright.config.ts` sets no
  // `actionTimeout`, so a bare `.click()` inherits the TEST timeout — 240 s
  // here — and a renamed control or a changed option label would hang for four
  // minutes on a bare Playwright message. Worse, the helper that would have
  // named the cause is muted: `ignoreUnhandledRejections(refetch)` attaches a
  // catch, so `waitForApiResponse`'s "the request was never observed" rejection
  // fires silently at API_TIMEOUT and is only re-raised after the click gives up.
  await page.locator('#complianceFilterStatus').click({ timeout: UI_TIMEOUT });
  await page
    .locator('.ng-dropdown-panel .ng-option')
    .filter({ hasText: anchored })
    .click({ timeout: UI_TIMEOUT });
  // `.ng-value-label`, never `.ng-value` (whose innerText carries the × glyph).
  await expect(page.locator('#complianceFilterStatus .ng-value-label')).toHaveText(anchored);

  expect((await refetch).ok()).toBeTruthy();
}

async function cancelCompleteModal(page: Page): Promise<void> {
  const modal = page.locator('app-calendar-complete-event-modal').first();
  if ((await modal.count()) === 0) return;
  const cancelBtn = page.locator('#completeCancelBtn');
  if ((await cancelBtn.count()) > 0) {
    await cancelBtn.click({ timeout: UI_TIMEOUT });
  } else {
    await page.keyboard.press('Escape');
  }
  await modal.waitFor({ state: 'detached', timeout: UI_TIMEOUT }).catch(() => undefined);
}

// ---------------------------------------------------------------------------
// Seed helper — lifted from `s/compliance-overview.spec.ts`.
// ---------------------------------------------------------------------------

/**
 * Clicks the calendar block's `.completion-btn` and cancels the modal WITHOUT
 * saving, so the Compliance row `prepare-complete` has just materialised stays
 * OPEN — an occurrence that exists and is not done.
 */
async function materialiseOpenRow(page: Page, calendarPage: CalendarUiEnhancementsPage, title: string): Promise<void> {
  const block = calendarPage.findEventBlock(title);
  await expect(block).toBeVisible({ timeout: UI_TIMEOUT });

  const prepareComplete = page.waitForResponse(isPrepareComplete, { timeout: 60000 });
  await block.locator('.completion-btn').click({ timeout: UI_TIMEOUT });
  const response = await prepareComplete;
  // The calendar sends no compliance-page source, so even a FUTURE occurrence
  // resolves (its intended early completion, #1300).
  expect((await response.json())?.success, `prepare-complete for ${title}`).toBe(true);
  const modal = page.locator('app-calendar-complete-event-modal');
  await modal.waitFor({ state: 'visible', timeout: 10000 });
  await page.locator('#completeWorkerSelect').waitFor({ state: 'visible', timeout: 10000 });
  await cancelCompleteModal(page);
}

/**
 * Two tasks on the property, each materialised into an OPEN Compliance row:
 * `TASK_TITLE` today (current week), `FUTURE_TASK_TITLE` next week.
 */
async function seedOpenComplianceRows(page: Page): Promise<void> {
  const calendarPage = new CalendarUiEnhancementsPage(page);
  await calendarPage.goToCalendar();
  const folderResponse = page.waitForResponse(
    r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
    { timeout: 60000 },
  );
  await calendarPage.selectProperty(property.name!);
  await folderResponse.catch(() => undefined);
  await page.waitForTimeout(1000);

  // TODAY — on the current week, no week advance.
  await calendarPage.openCreateModalOnCurrentWeek(TODAY_COLUMN, TODAY_SEED_HOUR);
  // Assigns the FIRST worker in the assignee dropdown — see the file header
  // for why the test does not need to know which of the two that is.
  await calendarPage.fillAndSaveEvent(TASK_TITLE);
  await materialiseOpenRow(page, calendarPage, TASK_TITLE);

  // NEXT WEEK — `openCreateModalAtSlot` advances one week first.
  await calendarPage.openCreateModalAtSlot(1, 9);
  await calendarPage.fillAndSaveEvent(FUTURE_TASK_TITLE);
  await materialiseOpenRow(page, calendarPage, FUTURE_TASK_TITLE);
}

// ---------------------------------------------------------------------------

test.describe.serial('Compliance Detaljer — complete modal groups workers by assignment (#1187)', () => {
  test.skip(
    NO_FUTURE_SLOT_TODAY,
    `no future slot left on today's column (run started at ${NOW_AT_LOAD.getHours()}:xx) — ` +
    'the fixture needs a task dated today (#1300)',
  );

  test.beforeEach(async ({ page }) => {
    await page.goto(BASE_URL);
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);
  });

  test.afterAll(async ({ browser }) => {
    // browser.newPage() can itself reject — a browser that crashed or got
    // disconnected during a long run — and an exception thrown here escapes the
    // hook and fails the job, which is exactly what this non-fatal teardown
    // exists to prevent. Record it and give up on cleanup instead.
    const page = await browser.newPage().catch((err: any) => {
      console.log(`afterAll cleanup failed (non-fatal): could not open a cleanup page: ${err?.message ?? err}`);
      return undefined;
    });
    if (!page) {
      return;
    }
    const cleanup = async () => {
      await page.goto(BASE_URL);
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
      await Promise.race([cleanup(), new Promise(resolve => setTimeout(resolve, 60000))]);
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    }
    try { await page.close(); } catch {}
  });

  // =========================================================================
  // Seed 1 — one property, TWO workers on it.
  // =========================================================================
  test('seed: create the property and two workers', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);
    await workersPage.create(otherWorker);

    propertiesSeeded = true;
  });

  // =========================================================================
  // Seed 2 — two tasks (today + next week), each assigned to one worker and
  // materialised into one open row.
  // =========================================================================
  test('seed: materialise open compliance rows for today and next week', async ({ page }) => {
    test.setTimeout(600000);
    expect(propertiesSeeded).toBe(true);

    await seedOpenComplianceRows(page);

    complianceSeeded = true;
  });

  // =========================================================================
  // The Detaljer variant of L6: grouped dropdown, assignee first, pre-selected.
  // =========================================================================
  test('clicking a not-completed row opens the modal with the assignee grouped first and pre-selected', async ({ page }) => {
    test.setTimeout(240000);
    expect(complianceSeeded).toBe(true);

    await openSeededDetails(page);

    const row = seededRow(page);
    // Clearer failure than a bare count mismatch when nothing rendered at all.
    await expect(page.locator('#complianceDetailsEmpty')).toHaveCount(0);
    await expect(row).toHaveCount(1);
    // A not-completed row with a task behind it is the clickable kind.
    await expect(row).toHaveClass(/is-clickable/);
    // A focusable table row with Enter/Space handlers — no role="button", which
    // would strip its row semantics and nest the delete button inside a button.
    await expect(row).toHaveAttribute('tabindex', '0');
    await expect(row).not.toHaveAttribute('role');

    // Which of the two seeded workers is the assignee, read from the row itself
    // (`workerNames[0]`; exactly one assignee, so the chip is the whole name).
    const chip = row.locator('.compliance-details__chip--worker');
    await expect(chip).toHaveCount(1);
    const assignedName = (await chip.innerText()).trim();
    const seededNames = [fullName(worker), fullName(otherWorker)];
    expect(seededNames).toContain(assignedName);
    const otherName = seededNames.find(n => n !== assignedName)!;

    // Click the row → the calendar's complete pipeline (prepare-complete POST,
    // then the combined modal).
    const prepareComplete = page.waitForResponse(isPrepareComplete, { timeout: 60000 });
    await row.click();
    await prepareComplete;

    const modal = page.locator('app-calendar-complete-event-modal').first();
    await modal.waitFor({ state: 'visible', timeout: 20000 });
    const workerSelect = page.locator('#completeWorkerSelect');
    await workerSelect.waitFor({ state: 'visible', timeout: 20000 });
    // The shell the calendar suite asserts, still wired from here.
    await expect(page.locator('#completeSaveBtn')).toHaveCount(1);
    await expect(page.locator('#completeCancelBtn')).toHaveCount(1);

    // #1205 — the dialog is headed with the TASK name (the same string this
    // row prints in its "Opgave" column), NOT the embedded eForm template's
    // name, which is what the header used to bind. Anchored with \s* because
    // toHaveText matches the raw text including Material's padding.
    await expect(modal.locator('h2[mat-dialog-title]'))
      .toHaveText(new RegExp(`^\\s*${escapeRegExp(TASK_TITLE)}\\s*$`));

    // Exactly one assignee → pre-selected, the same as from the calendar grid.
    // `.ng-value-label`, never `.ng-value`.
    await expect(workerSelect.locator('.ng-value-label'))
      .toHaveText(new RegExp(`^\\s*${escapeRegExp(assignedName)}\\s*$`));

    // Open the dropdown (mtx-select appends its panel to <body>).
    await workerSelect.click();
    const panel = page.locator('.ng-dropdown-panel');
    await panel.waitFor({ state: 'visible', timeout: 10000 });

    // Two groups, assignees first. Group headers are .ng-optgroup and never
    // .ng-option, so the option assertions below cannot be satisfied by a header.
    const groups = panel.locator('.ng-optgroup');
    await expect(groups).toHaveCount(2);
    await expect(groups.nth(0)).toHaveText(/^\s*Tildelte medarbejdere\s*$/);
    await expect(groups.nth(1)).toHaveText(/^\s*Øvrige medarbejdere\s*$/);
    await expect(panel.locator('.ng-optgroup.ng-option')).toHaveCount(0);

    // Options render through the custom template (the calendar suite's L6
    // invariant), and both seeded workers are offered.
    await expect(panel.locator('.eform-worker-option-label').first()).toBeVisible();
    await expect(panel.locator('.ng-option').filter({ hasText: assignedName })).toHaveCount(1);
    await expect(panel.locator('.ng-option').filter({ hasText: otherName })).toHaveCount(1);

    // Membership is positional in ng-select's flat panel DOM: a group header is
    // followed by its own options until the next header. Walk headers and
    // options in document order and check which header each worker sits under.
    const membership = await panel.evaluate((el, names) => {
      const out: Record<string, string | null> = {};
      let current: string | null = null;
      for (const node of Array.from(el.querySelectorAll('.ng-optgroup, .ng-option'))) {
        const text = (node.textContent ?? '').trim();
        if (node.classList.contains('ng-optgroup')) {
          current = text;
          continue;
        }
        for (const name of names) {
          if (text === name) out[name] = current;
        }
      }
      return out;
    }, seededNames);
    expect(membership[assignedName]).toBe('Tildelte medarbejdere');
    expect(membership[otherName]).toBe('Øvrige medarbejdere');

    // Escape does not just close the ng-select panel here: the CDK overlay
    // dispatcher sees the same keydown and closes the dialog too (it is not
    // disableClose), with an exit animation. Assert what Escape actually did
    // instead of then racing cancelCompleteModal()'s Cancel click against
    // that animation. Nothing is saved; the row stays open.
    await page.keyboard.press('Escape');
    await expect(modal).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  // =========================================================================
  // Status filter — the only end-to-end proof that it changes the ROW SET.
  //
  // The two ends are pinned separately and never joined: the store spec
  // (`compliance-report-state.service.spec.ts`) proves `setFilter` writes
  // `status` into the request model, and `ComplianceReportIndexTests` proves
  // the server honours it. Nothing in between. `s/compliance-page-shell.spec.ts`
  // does move this control, but it asserts no row selector at all — only the
  // new label, that a refetch arrived, and that the shell chrome survived it.
  // A regression that stopped propagating the selected value from the select
  // into `setFilter` would keep every one of those suites green.
  //
  // Hence the ROUND TRIP: the seeded row disappears under `Udførte opgaver`
  // and comes BACK under `Ikke udførte opgaver`. Asserting only the
  // disappearance would also pass if the grid had simply stopped rendering.
  //
  // Runs BEFORE the delete test and mutates nothing: the filter lives in the
  // page's own state service (no storage, no server state) and each test gets
  // a fresh page, so the open row the delete test needs is untouched — and
  // this test does not inherit the risk of running after a delete confirm.
  // =========================================================================
  test('the status filter drives the rendered rows: the open row leaves under Udførte and returns under Ikke udførte', async ({ page }) => {
    test.setTimeout(240000);
    expect(complianceSeeded).toBe(true);

    await openSeededDetails(page);

    // --- Baseline: the default status, and the seeded OPEN row on screen ---
    await expect(page.locator('#complianceFilterStatus .ng-value-label'))
      .toHaveText(/^\s*Ikke udførte opgaver\s*$/);
    await expect(page.locator('#complianceDetailsEmpty')).toHaveCount(0);
    await expect(seededRow(page)).toHaveCount(1);

    // The week heading the row is grouped under. `helpers/compliance-week-grouping.spec.ts`
    // pins the label STRINGS; what it cannot reach is the `{{ group.label }}`
    // binding, which no other Playwright spec asserts — `.compliance-details__week-title`
    // appears in this file and nowhere else in `playwright/`.
    const weekTitle = page
      .locator('.compliance-details__week')
      // Via the exact-title row, not `hasText: TASK_TITLE`: next week's group
      // also contains the property name, which matches it case-insensitively.
      .filter({ has: seededRow(page) })
      .locator('.compliance-details__week-title');
    await expect(weekTitle).toHaveCount(1);
    await expect(weekTitle).toBeVisible();
    await expect(weekTitle).toHaveText(/\S/);

    // The pagination summary, counted from the same response. `/\d/` because
    // the exact wording ("Viser 1–1 af 1") mixes plugin and CORE i18n keys and
    // the numbers themselves are pinned in `compliance-report-state.service.spec.ts`
    // (`showingFrom`/`showingTo`); what has no other coverage is that this
    // element renders in a browser at all.
    const pageInfo = page.locator('#compliancePageInfo');
    await expect(pageInfo).toBeVisible();
    await expect(pageInfo).toHaveText(/\d/);

    // --- Udførte opgaver: the open row must leave ---------------------------
    // Explicit budgets on the post-refetch assertions, matching the way
    // `s/compliance-page-shell.spec.ts` budgets its own: each runs after
    // `await refetch`, so one change-detection pass is all it waits for — the
    // budget states that rather than leaving it to Playwright's 5 s default.
    await selectStatusAndAwaitDetails(page, 'Udførte opgaver');
    await expect(seededRow(page)).toHaveCount(0, { timeout: API_TIMEOUT });
    // Not "the grid broke": the view rendered its own empty result, which it
    // only does after a response (`hasFetched && !loading && rowCount === 0`).
    //
    // Nor could a FAILED refetch have produced this row set. `rowCount` and
    // `groups` are written only in `applyResponse`, which is reached only when
    // `res.success`; the failure branch returns early and deliberately leaves
    // the previous rendering standing, so the seeded row would still be on
    // screen and the count above would fail. A non-2xx is ruled out separately
    // by the `.ok()` check inside `selectStatusAndAwaitDetails`.
    await expect(page.locator('#complianceDetailsEmpty')).toBeVisible({ timeout: API_TIMEOUT });
    await expect(pageInfo).toHaveText(/^\s*Ingen resultater\s*$/, { timeout: API_TIMEOUT });

    // --- ...and come back under the default status -------------------------
    await selectStatusAndAwaitDetails(page, 'Ikke udførte opgaver');
    await expect(page.locator('#complianceDetailsEmpty')).toHaveCount(0, { timeout: API_TIMEOUT });
    await expect(seededRow(page)).toHaveCount(1, { timeout: API_TIMEOUT });
    await expect(pageInfo).toHaveText(/\d/, { timeout: API_TIMEOUT });
  });

  // =========================================================================
  // Delete action — confirm popover, Annuller keeps the row.
  //
  // PORTED from `r/calendar-compliance-view.spec.ts`, deleted by #1170 along
  // with the calendar Compliance view mode. That suite's "delete button opens
  // a confirm dialog; Annuller keeps the row" test was the only assertion in
  // it with no counterpart on the standalone page, so it moves here rather
  // than being dropped.
  //
  // A map of where the STANDALONE page's Compliance coverage lives — not a
  // claim about what the deleted suite contained — checked selector by
  // selector rather than assumed:
  //   - filter bar and pagination chrome — `s/compliance-page-shell.spec.ts`
  //     (they belong to the shell's template, which that suite owns);
  //   - Oversigt rows — `s/compliance-overview.spec.ts`;
  //   - Rapport — `s/compliance-report-view.spec.ts`, but only the mode's
  //     CHROME. Its own header says so: sub-report tables need completed,
  //     answered cases it will not seed, so it asserts the meta line, the
  //     `dd.MM.yyyy` period format, the empty-result wording and the section
  //     structure. It pins no report rows.
  //   - Detaljer ROW RENDERING and the WEEK HEADINGS are in no `s/` spec. Not
  //     because shard `s` has no rows — `s/compliance-overview.spec.ts`
  //     self-seeds two open Compliance rows, commits a covering custom range
  //     and drills into Detaljer twice, which does render a seeded Detaljer
  //     row — but because no `s/` spec ASSERTS a Detaljer row selector (the
  //     drill-down tests assert the mode switch and the filters that travelled
  //     with it, nothing below them). This file is the only spec that asserts
  //     one: `.compliance-details__row` appears nowhere else in `playwright/`,
  //     here in the two tests above and in the delete test below, and the
  //     week title and `#compliancePageInfo` in exactly one of them (the
  //     status test).
  //   - STATUS FILTERING likewise. `s/compliance-page-shell.spec.ts` moves
  //     `#complianceFilterStatus`, but asserts no row selector — only the new
  //     label and that a refetch arrived. Nor could it see a fixture like this
  //     one: every period it uses is bounded ABOVE by today (`periodBounds` in
  //     `compliance-report-state.service.ts`; the default is `ytd`), and the
  //     single custom range it commits lies entirely in the past, while this
  //     seed is dated today (and, since #1300, next week too). The status test
  //     above is the only end-to-end
  //     coverage of that chain.
  //
  // Two gaps this file does NOT close, recorded rather than covered (no test
  // is added for either in this round):
  //   - `[class.is-done]` on a Detaljer row (compliance-details-view.component.html:77)
  //     is asserted by no spec, in either direction — completing a row is out
  //     of reach here, and the open row's lack of the modifier goes unchecked;
  //   - the `Alle opgaver` status value has no e2e ROW-SET coverage; only
  //     `Udførte opgaver` / `Ikke udførte opgaver` are round-tripped above.
  //
  // The confirm is NOT the calendar view's `mat-dialog-container`: Detaljer
  // uses an anchored CDK overlay popover (`#complianceDeletePopover`,
  // `openDeleteConfirm`) with no backdrop, attached at body level rather than
  // inside the row — so it is located from `page`, never from the row. Cancel
  // leads and the destructive action follows, per the repo's button-order gate.
  // Runs LAST in the serial block because a stray confirm would destroy the
  // fixture the tests above depend on.
  // =========================================================================
  test('an uncompleted FUTURE row offers neither completion nor deletion, while today\'s row offers both (#1300)', async ({ page }) => {
    test.setTimeout(240000);
    expect(complianceSeeded).toBe(true);

    await openSeededDetails(page);

    const today = seededRow(page);
    const future = futureRow(page);
    await expect(today).toHaveCount(1, { timeout: API_TIMEOUT });
    await expect(future).toHaveCount(1, { timeout: API_TIMEOUT });

    // Today's open row: clickable, focusable, deletable.
    await expect(today).toHaveClass(/is-clickable/, { timeout: UI_TIMEOUT });
    await expect(today).toHaveAttribute('tabindex', '0', { timeout: UI_TIMEOUT });
    await expect(today.locator('.compliance-details__delete')).toBeVisible({ timeout: UI_TIMEOUT });

    // The future open row: locked, with the reason in its title.
    await expect(future).toHaveClass(/is-future/, { timeout: UI_TIMEOUT });
    await expect(future).not.toHaveClass(/is-clickable/, { timeout: UI_TIMEOUT });
    await expect(future).not.toHaveAttribute('tabindex', /.*/, { timeout: UI_TIMEOUT });
    await expect(future).toHaveAttribute('title', /\S/, { timeout: UI_TIMEOUT });
    await expect(future.locator('.compliance-details__delete')).toHaveCount(0, { timeout: UI_TIMEOUT });

    // Clicking it starts nothing: no prepare-complete request, no modal.
    let prepareCompleteCalls = 0;
    const onRequest = (r: import('@playwright/test').Request) => {
      if (/\/calendar\/tasks\/\d+\/prepare-complete/.test(r.url()) && r.method() === 'POST') {
        prepareCompleteCalls++;
      }
    };
    page.on('request', onRequest);
    await future.locator('.compliance-details__title').click({ timeout: UI_TIMEOUT });
    await expect(page.locator('app-calendar-complete-event-modal')).toBeHidden({ timeout: 3000 });
    page.off('request', onRequest);
    expect(prepareCompleteCalls).toBe(0);
  });

  test('the delete action opens a confirm popover; Annuller closes it and keeps the row', async ({ page }) => {
    test.setTimeout(240000);
    expect(complianceSeeded).toBe(true);

    await openSeededDetails(page);

    const row = seededRow(page);
    await expect(page.locator('#complianceDetailsEmpty')).toHaveCount(0);
    await expect(row).toHaveCount(1);

    // The actions cell is always there (it keeps the columns aligned), but the
    // delete button renders only for rows that are NOT completed, and the
    // seeded row is open — so the button must be there.
    const deleteBtn = row.locator('.compliance-details__delete');
    await expect(deleteBtn).toBeVisible();
    await deleteBtn.click();

    const popover = page.locator('#complianceDeletePopover');
    await popover.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await expect(popover).toHaveAttribute('role', 'dialog');
    await expect(popover.locator('.compliance-delete-popover__title'))
      .toHaveText(/^\s*Slet log\s*$/);

    // Cancel first, destructive second. `toHaveText` matches RAW text, hence
    // the \s* anchors around Material's padding.
    const actions = popover.locator('button');
    await expect(actions).toHaveCount(2);
    await expect(actions.nth(0)).toHaveText(/^\s*Annuller\s*$/);
    await expect(actions.nth(1)).toHaveText(/^\s*Slet\s*$/);

    // The actions cell stops the click from reaching the row handler, so
    // asking to delete must never also open the eForm completion modal.
    await expect(page.locator('app-calendar-complete-event-modal')).toHaveCount(0);

    await popover.locator('.compliance-delete-popover__cancel').click();
    await popover.waitFor({ state: 'detached', timeout: UI_TIMEOUT });

    // Nothing was deleted: the row is still rendered and still deletable.
    await expect(seededRow(page)).toHaveCount(1);
    await expect(seededRow(page).locator('.compliance-details__delete')).toBeVisible();
  });
});

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
