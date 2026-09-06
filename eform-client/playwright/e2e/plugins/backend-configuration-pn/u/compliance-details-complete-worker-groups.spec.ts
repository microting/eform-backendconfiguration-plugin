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

/**
 * Standalone Compliance page — DETALJER row → complete modal, worker grouping
 * (#1187). The Detaljer variant of L6 in `p/calendar-complete-layout.spec.ts`.
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
 * page objects, a one-off task NEXT WEEK on the property's calendar, then an
 * OPEN Compliance row materialised by clicking the task's `.completion-btn`
 * (POST `/tasks/{id}/prepare-complete` runs `EnsureComplianceForOccurrenceAsync`
 * server-side) and CANCELLING the modal. One task → one Compliance row, which
 * respects the UNIQUE (PlanningId, Deadline) constraint on `Compliances`.
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

// `openCreateModalAtSlot(0, 9)` advances the calendar one week, then clicks the
// first visible day — i.e. Monday of NEXT week, at 09:00.
const TASK_DATE = addDays(mondayOfThisWeekLocal(), 7);

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
 * Every built-in preset is bounded ABOVE by today (a compliance report is
 * retrospective) and the seeded task is next week, so reaching it needs
 * "Sæt periode" with an explicit range.
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
  const to = addDays(TASK_DATE, 8);
  await page.locator('.compliance-filters__custom-range mat-datepicker-toggle button').click();
  await selectDateRangeOnNewDatePicker(
    page,
    from.getFullYear(), from.getMonth() + 1, from.getDate(),
    to.getFullYear(), to.getMonth() + 1, to.getDate(),
  );
  await page.waitForTimeout(300);
}

/** `Opdater tabel` — the ONLY control that fetches; in Detaljer it hits `/index`. */
async function showDetails(page: Page): Promise<void> {
  const response = page.waitForResponse(
    r => r.url().includes('/api/backend-configuration-pn/compliance-report/index')
      && r.request().method() === 'POST',
    { timeout: 60000 },
  );
  await page.locator('#complianceShowReportBtn').click();
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
  await selectSeededProperty(page);
  await selectPeriodCoveringSeed(page);
  // Switch mode BEFORE fetching: the status control is disabled in Oversigt.
  // The page auto-fetches on entry and a mode switch replays that trigger, so
  // there are two identical `/index` requests either way; the assertions
  // below tolerate that (`showDetails` waits for its own request, then the
  // row assertions retry until the last response has rendered).
  await page.locator('#complianceMode-details').click();
  await expect(page.locator('#complianceMode-details')).toHaveAttribute('aria-pressed', 'true');
  await showDetails(page);
}

function seededRow(page: Page): Locator {
  return page.locator('.compliance-details__row').filter({ hasText: TASK_TITLE });
}

async function cancelCompleteModal(page: Page): Promise<void> {
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

// ---------------------------------------------------------------------------
// Seed helper — lifted from `s/compliance-overview.spec.ts`.
// ---------------------------------------------------------------------------

/** One next-week task on the property, materialised into an OPEN Compliance row. */
async function seedOpenComplianceRow(page: Page): Promise<void> {
  const calendarPage = new CalendarUiEnhancementsPage(page);
  await calendarPage.goToCalendar();
  await calendarPage.ensureSidebarOpen();
  const folderResponse = page.waitForResponse(
    r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
    { timeout: 60000 },
  );
  await calendarPage.selectProperty(property.name!);
  await folderResponse.catch(() => undefined);
  await page.waitForTimeout(1000);

  await calendarPage.openCreateModalAtSlot(0, 9);
  // Assigns the FIRST worker in the assignee dropdown — see the file header
  // for why the test does not need to know which of the two that is.
  await calendarPage.fillAndSaveEvent(TASK_TITLE);

  const block = calendarPage.findEventBlock(TASK_TITLE);
  await expect(block).toBeVisible();

  const prepareComplete = page.waitForResponse(isPrepareComplete, { timeout: 60000 });
  await block.locator('.completion-btn').click();
  await prepareComplete;
  const modal = page.locator('app-calendar-complete-event-modal');
  await modal.waitFor({ state: 'visible', timeout: 10000 });
  await page.locator('#completeWorkerSelect').waitFor({ state: 'visible', timeout: 10000 });
  // Cancel WITHOUT saving, so the Compliance row `prepare-complete` has just
  // materialised stays OPEN — an occurrence that exists and is not done.
  await cancelCompleteModal(page);
}

// ---------------------------------------------------------------------------

test.describe.serial('Compliance Detaljer — complete modal groups workers by assignment (#1187)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto(BASE_URL);
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);
  });

  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage();
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
  // Seed 2 — one task assigned to one worker, materialised into one open row.
  // =========================================================================
  test('seed: materialise one open compliance row assigned to one worker', async ({ page }) => {
    test.setTimeout(600000);
    expect(propertiesSeeded).toBe(true);

    await seedOpenComplianceRow(page);

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
    await expect(row).toHaveAttribute('role', 'button');

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

    // Close the panel, then the modal — nothing is saved; the row stays open.
    await page.keyboard.press('Escape');
    await cancelCompleteModal(page);
    await expect(modal).toHaveCount(0);
  });
});

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
