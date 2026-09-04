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
 * Standalone Compliance page — OVERSIGT suite (#1164).
 *
 * The shell (#1163) has its own suite next door in `compliance-page-shell.spec.ts`;
 * this one owns the Oversigt view itself: the three columns and their order, the
 * `–` for a percentage that is not due yet, the header sort cycle and its
 * `aria-sort`, the row drill-down by mouse AND by keyboard, the un-drillable
 * totals row, and the empty state.
 *
 * SELF-SEEDED, because shard `s` seeds no SQL. The fixture is the one proved by
 * `r/calendar-compliance-view.spec.ts`: create properties + a worker through the
 * UI, create a one-off task NEXT WEEK on each property's calendar, then
 * materialise an OPEN Compliance row by clicking the task's `.completion-btn`
 * (POST `/tasks/{id}/prepare-complete` calls
 * `EnsureComplianceForOccurrenceAsync` server-side) and CANCELLING the modal
 * without ever saving its embedded eForm. Two seeded properties, because
 * "sorting changes the row order" is not assertable with one.
 *
 * WHAT THIS SUITE DELIBERATELY DOES NOT ASSERT, and why — stated rather than
 * covered by an assertion that would pass vacuously:
 *
 *  - **The low / mid / high pill bands and any non-null percentage.** Both
 *    require a task that has already fallen DUE, i.e. dated in the past. The
 *    calendar's create path still refuses a past `StartDate` (the guard is live;
 *    the past-dated backfill design was never implemented), and there is no API
 *    to backdate an occurrence, so no e2e on this shard can produce one. The
 *    banding thresholds are pinned by `compliance-overview.helper.spec.ts` and
 *    the server-side maths by `ComplianceReportOverviewTests.cs`.
 *  - **A NUMERIC column re-ordering the rows.** Both seeded properties hold a
 *    single not-yet-due task, so both rows carry `overdue = 0` and
 *    `compliancePct = null` — identical values, on which a correct sort is
 *    entitled to change nothing. The order change is therefore asserted on the
 *    `Ejendom` column, whose values genuinely differ; what the numeric headers
 *    are asserted to do is move `aria-sort` onto themselves and off everything
 *    else, which is assertable and is the half that regresses.
 *  - **The pagination being absent in Oversigt.** That `<nav>` belongs to the
 *    shell's template, and its suite asserts it (see the filter-change test
 *    there). Duplicating it here would put one guarantee in two suites.
 *
 * Local traps this file is written around, each already paid for in this repo:
 * dropdown options are picked BY LABEL and never by `nth()`; mtx-select text is
 * read from `.ng-value-label`, never `.ng-value` (whose innerText includes the ×
 * clear-icon glyph); and a regex `hasText` matches RAW text, so the en-dash cell
 * is matched with `/^\s*–\s*$/` rather than `/^–$/`.
 */

const BASE_URL = 'http://localhost:4200';
const PAGE_URL = `${BASE_URL}/plugins/backend-configuration-pn/compliance-report`;
const rand = generateRandmString(6).toLowerCase();

// The two names must have a KNOWN Danish collation order, since the whole point
// of the sort tests is that A and B swap places. `AAA-` and `ZZZ-` bracket every
// random name any other suite in this shard might leave in the database, and the
// assertions compare the two rows' RELATIVE positions rather than absolute ones,
// so foreign rows in between are harmless.
const propertyA: PropertyCreateUpdate = {
  name: `AAA-ovw-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};
const propertyB: PropertyCreateUpdate = {
  name: `ZZZ-ovw-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};
// A third property with NO tasks at all. The aggregation emits one row per
// property that has at least one matching compliance row, so filtering to this
// one is how the empty state is reached without touching the period.
const propertyEmpty: PropertyCreateUpdate = {
  name: `MMM-ovw-empty-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const worker: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [propertyA.name!, propertyB.name!],
  workerEmail: `${generateRandmString(5)}@test.com`,
};

let propertiesSeeded = false;
let complianceSeeded = false;

// ---------------------------------------------------------------------------
// Dates — local and deterministic, no calendar-day round trips.
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

/**
 * Login happens once per test in `beforeEach` (the seed tests need it before
 * they ever touch a page object), so this only navigates — calling
 * `LoginPage.login()` a second time on an already-authenticated session would
 * hang waiting for a `#loginBtn` that is no longer on the page.
 */
async function goToCompliancePage(page: Page): Promise<void> {
  await page.goto(PAGE_URL);
  await page.locator('#complianceFilterProperty').waitFor({ state: 'visible', timeout: 60000 });
}

/**
 * Every built-in preset is bounded ABOVE by today (`periodBounds` — a compliance
 * report is retrospective), and the seeded tasks are next week, so reaching them
 * needs "Sæt periode" with an explicit range.
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

/** `Opdater tabel` — the ONLY control that fetches. */
async function showReport(page: Page): Promise<void> {
  const response = page.waitForResponse(
    r => r.url().includes('/api/backend-configuration-pn/compliance-report/overview')
      && r.request().method() === 'POST',
    { timeout: 60000 },
  );
  await page.locator('#complianceShowReportBtn').click();
  await response;
  await page.waitForTimeout(500);
}

/** Land on the page and fetch Oversigt over a window containing both seeds. */
async function openSeededOverview(page: Page): Promise<void> {
  await goToCompliancePage(page);
  await selectPeriodCoveringSeed(page);
  await showReport(page);
  await page.locator('.compliance-overview__table').waitFor({ state: 'visible', timeout: 30000 });
}

function overviewRow(page: Page, propertyName: string): Locator {
  return page.locator('tr.compliance-overview__row').filter({ hasText: propertyName });
}

/**
 * The rendered property column, top to bottom. Scoped to `.compliance-overview__row`
 * so the `<tfoot>` totals cell — which carries the same `__property` class — is
 * excluded.
 */
async function renderedPropertyNames(page: Page): Promise<string[]> {
  return page
    .locator('tr.compliance-overview__row .compliance-overview__property')
    .allTextContents();
}

/** Relative order of the two seeded rows, ignoring any foreign rows between them. */
async function seededOrder(page: Page): Promise<{ a: number; b: number }> {
  const names = (await renderedPropertyNames(page)).map(n => n.trim());
  return {
    a: names.findIndex(n => n.includes(propertyA.name!)),
    b: names.findIndex(n => n.includes(propertyB.name!)),
  };
}

const headerCell = (page: Page, index: number) =>
  page.locator('.compliance-overview__th').nth(index);

/** `[attr.aria-sort]="null"` REMOVES the attribute, so absence is the assertion. */
async function ariaSortOf(page: Page, index: number): Promise<string | null> {
  return headerCell(page, index).getAttribute('aria-sort');
}

// ---------------------------------------------------------------------------
// Seed helpers — lifted from r/calendar-compliance-view.spec.ts.
// ---------------------------------------------------------------------------

async function handleWorkerSelectModal(page: Page): Promise<void> {
  const modal = page.locator('app-calendar-complete-event-modal');
  await modal.waitFor({ state: 'visible', timeout: 10000 });
  const workerSelect = page.locator('#completeWorkerSelect');
  await workerSelect.waitFor({ state: 'visible', timeout: 10000 });
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

/**
 * Cancel WITHOUT saving, so the Compliance row that
 * `EnsureComplianceForOccurrenceAsync` has just materialised stays in the OPEN
 * state — which is the fixture, an occurrence that exists and is not done.
 */
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

/** One next-week task on `propertyName`, materialised into an OPEN Compliance row. */
async function seedOpenComplianceRow(page: Page, propertyName: string, title: string): Promise<void> {
  const calendarPage = new CalendarUiEnhancementsPage(page);
  // A fresh calendar load per property, so the week offset is always "this
  // week" before openCreateModalAtSlot advances it exactly once.
  await calendarPage.goToCalendar();
  await calendarPage.ensureSidebarOpen();
  const folderResponse = page.waitForResponse(
    r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
    { timeout: 60000 },
  );
  await calendarPage.selectProperty(propertyName);
  await folderResponse.catch(() => undefined);
  await page.waitForTimeout(1000);

  await calendarPage.openCreateModalAtSlot(0, 9);
  await calendarPage.fillAndSaveEvent(title);

  const block = calendarPage.findEventBlock(title);
  await expect(block).toBeVisible();

  const prepareComplete = page.waitForResponse(
    r => /\/api\/backend-configuration-pn\/calendar\/tasks\/\d+\/prepare-complete/.test(r.url())
      && r.request().method() === 'POST',
    { timeout: 60000 },
  );
  await block.locator('.completion-btn').click();
  await prepareComplete;
  await handleWorkerSelectModal(page);
  await cancelCompleteModal(page);
}

// ---------------------------------------------------------------------------

test.describe.serial('Compliance Oversigt (#1164)', () => {
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
  // Seed 1 — three properties (two with data, one deliberately empty) + a
  // worker paired to the two that get tasks.
  // =========================================================================
  test('seed: create the properties and the worker', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(propertyA);
    await propertiesPage.createProperty(propertyB);
    await propertiesPage.createProperty(propertyEmpty);

    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    propertiesSeeded = true;
  });

  // =========================================================================
  // Seed 2 — one open Compliance row per data-bearing property.
  // =========================================================================
  test('seed: materialise one open compliance row on each property', async ({ page }) => {
    test.setTimeout(600000);
    expect(propertiesSeeded).toBe(true);

    await seedOpenComplianceRow(page, propertyA.name!, `OVW-A-${rand}`);
    await seedOpenComplianceRow(page, propertyB.name!, `OVW-B-${rand}`);

    complianceSeeded = true;
  });

  // =========================================================================
  // Columns.
  // =========================================================================
  test('renders exactly three columns — Ejendom, Overskredet, Compliance % — in that order', async ({ page }) => {
    test.setTimeout(180000);
    expect(complianceSeeded).toBe(true);

    await openSeededOverview(page);

    const headers = page.locator('.compliance-overview__th');
    await expect(headers).toHaveCount(3);
    // `Opgaver i alt` and `Udført` were removed on purpose; a fourth header of
    // any kind fails the count above.
    await expect(headers.nth(0)).toContainText('Ejendom');
    await expect(headers.nth(1)).toContainText('Overskredet');
    await expect(headers.nth(2)).toContainText('Compliance %');

    // Both seeded properties made it into the table.
    await expect(overviewRow(page, propertyA.name!)).toHaveCount(1);
    await expect(overviewRow(page, propertyB.name!)).toHaveCount(1);
  });

  // =========================================================================
  // The en dash. A next-week task has not fallen due, so `dueTotal` is 0 and
  // the server sends `compliancePct: null` — which must render as `–`, never
  // `0`, `0%` or `NaN`.
  // =========================================================================
  test('a percentage that is not due yet renders as the en dash, not zero', async ({ page }) => {
    test.setTimeout(180000);
    expect(complianceSeeded).toBe(true);

    await openSeededOverview(page);

    const pill = overviewRow(page, propertyA.name!).locator('.compliance-overview__pill');
    // A regex hasText/toHaveText match runs against RAW text, hence the \s*.
    await expect(pill).toHaveText(/^\s*–\s*$/);
    await expect(pill).toHaveClass(/is-none/);

    const overdue = overviewRow(page, propertyA.name!).locator('.compliance-overview__overdue');
    await expect(overdue).toHaveText(/^\s*0\s*$/);
    await expect(overdue).toHaveClass(/is-calm/);
  });

  // =========================================================================
  // Sorting. The landing sort is compliancePct ascending; clicking `Ejendom`
  // starts ascending, clicking it again flips to descending — two states, never
  // a third "unsorted" step.
  // =========================================================================
  test('sorting by Ejendom reorders the rows and flips on a second click', async ({ page }) => {
    test.setTimeout(180000);
    expect(complianceSeeded).toBe(true);

    await openSeededOverview(page);

    await page.locator('#complianceOverviewSort-propertyName').click();
    const ascending = await seededOrder(page);
    expect(ascending.a).toBeGreaterThanOrEqual(0);
    expect(ascending.b).toBeGreaterThanOrEqual(0);
    expect(ascending.a).toBeLessThan(ascending.b);

    await page.locator('#complianceOverviewSort-propertyName').click();
    const descending = await seededOrder(page);
    expect(descending.b).toBeLessThan(descending.a);
  });

  test('aria-sort lands on the clicked header and on no other', async ({ page }) => {
    test.setTimeout(180000);
    expect(complianceSeeded).toBe(true);

    await openSeededOverview(page);

    // Landing state: compliancePct (the third column) ascending, worst first.
    expect(await ariaSortOf(page, 0)).toBeNull();
    expect(await ariaSortOf(page, 1)).toBeNull();
    expect(await ariaSortOf(page, 2)).toBe('ascending');

    await page.locator('#complianceOverviewSort-propertyName').click();
    expect(await ariaSortOf(page, 0)).toBe('ascending');
    expect(await ariaSortOf(page, 1)).toBeNull();
    expect(await ariaSortOf(page, 2)).toBeNull();

    await page.locator('#complianceOverviewSort-propertyName').click();
    expect(await ariaSortOf(page, 0)).toBe('descending');

    // A NEW numeric key starts descending ("most overdue first"), and takes the
    // marker with it. The rows themselves cannot be shown to move here — both
    // seeded properties have overdue 0 — see the file header.
    await page.locator('#complianceOverviewSort-overdue').click();
    expect(await ariaSortOf(page, 0)).toBeNull();
    expect(await ariaSortOf(page, 1)).toBe('descending');
    expect(await ariaSortOf(page, 2)).toBeNull();

    // Clicking the ACTIVE key flips it rather than clearing it.
    await page.locator('#complianceOverviewSort-overdue').click();
    expect(await ariaSortOf(page, 1)).toBe('ascending');

    // The indicator icon is rendered in the sorted header only.
    await expect(page.locator('.compliance-overview__sort-icon')).toHaveCount(1);
  });

  // =========================================================================
  // Drill-down.
  // =========================================================================
  test('clicking a row drills into Detaljer with that property filtered and status forced to all', async ({ page }) => {
    test.setTimeout(180000);
    expect(complianceSeeded).toBe(true);

    await openSeededOverview(page);

    const row = overviewRow(page, propertyA.name!);
    // The attribute exists so a test can identify a row by property; use it.
    const propertyId = await row.getAttribute('data-property-id');
    expect(Number(propertyId)).toBeGreaterThan(0);

    await row.click();

    await expect(page.locator('#complianceMode-details')).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('#complianceMode-overview')).toHaveAttribute('aria-pressed', 'false');
    // `.ng-value-label`, never `.ng-value` — the latter's innerText carries the
    // × clear-icon glyph.
    await expect(page.locator('#complianceFilterProperty .ng-value-label'))
      .toHaveText(propertyA.name!);
    // Oversigt counts done and not-done together, so the drill forces `all`;
    // anything else would not add up to the number just clicked.
    await expect(page.locator('#complianceFilterStatus .ng-value-label'))
      .toHaveText('Alle opgaver');
    // The drill is SILENT: it must not blank the result it just navigated to.
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);
  });

  test('a row is reachable and activated from the keyboard', async ({ page }) => {
    test.setTimeout(180000);
    expect(complianceSeeded).toBe(true);

    await openSeededOverview(page);

    const row = overviewRow(page, propertyB.name!);
    await expect(row).toHaveAttribute('tabindex', '0');

    await row.focus();
    await page.keyboard.press('Enter');

    await expect(page.locator('#complianceMode-details')).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('#complianceFilterProperty .ng-value-label'))
      .toHaveText(propertyB.name!);
  });

  // =========================================================================
  // The totals row — present, weighted, and NOT a drill-down target.
  // =========================================================================
  test('the totals row is rendered but is neither focusable nor drillable', async ({ page }) => {
    test.setTimeout(180000);
    expect(complianceSeeded).toBe(true);

    await openSeededOverview(page);

    const totals = page.locator('tfoot tr.compliance-overview__totals');
    await expect(totals).toHaveCount(1);
    await expect(totals).toContainText('I alt');

    // It carries propertyId 0 and is not a property: no tabindex, no role, and
    // no data-property-id for a drill-down to key off.
    expect(await totals.getAttribute('tabindex')).toBeNull();
    expect(await totals.getAttribute('role')).toBeNull();
    expect(await totals.getAttribute('data-property-id')).toBeNull();
    // It is also not one of the `<tbody>` rows.
    await expect(page.locator('tr.compliance-overview__row').filter({ hasText: 'I alt' }))
      .toHaveCount(0);

    await totals.click();
    await expect(page.locator('#complianceMode-overview')).toHaveAttribute('aria-pressed', 'true');
  });

  // =========================================================================
  // Empty state — the table is REPLACED, not rendered empty.
  // =========================================================================
  test('a property with no compliance rows shows the empty state instead of an empty table', async ({ page }) => {
    test.setTimeout(180000);
    expect(complianceSeeded).toBe(true);

    await openSeededOverview(page);
    await expect(page.locator('#complianceOverviewEmpty')).toHaveCount(0);

    // Narrow to the property that was created without a single task. Picked BY
    // LABEL — never by nth() index.
    await page.locator('#complianceFilterProperty').click();
    await page
      .locator('.ng-dropdown-panel .ng-option', { hasText: propertyEmpty.name! })
      .first()
      .click();
    // A filter change blanks the result and issues no request; only
    // `Opdater tabel` fetches.
    await expect(page.locator('#complianceEmptyState')).toBeVisible();
    await showReport(page);

    await expect(page.locator('#complianceOverviewEmpty')).toBeVisible();
    await expect(page.locator('#complianceOverviewEmpty'))
      .toHaveText('Ingen ejendomme matcher de valgte filtre.');
    // No <table> at all in this state — not a table with a header and no body.
    await expect(page.locator('.compliance-overview__table')).toHaveCount(0);
  });
});
