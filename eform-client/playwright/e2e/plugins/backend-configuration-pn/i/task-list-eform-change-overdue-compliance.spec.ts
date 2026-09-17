import { test, expect, Page, Response } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import { TaskListPage } from '../task-list.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';
import { customerDatabase, runMariadbSql } from '../db-helpers';
import {
  API_TIMEOUT,
  SLOW_API_TIMEOUT,
  UI_TIMEOUT,
  ignoreUnhandledRejections,
  waitForApiRequest,
  waitForApiResponse,
} from '../wait-helpers';

/**
 * Overdue compliance entries follow an event's eForm change (#1287, shard i).
 *
 * THE BUG: changing an event's eForm (here through the task list's batch
 * "Skift eForm") ran `EventDeployService.RepairEformForOpenOccurrencesAsync`,
 * which only swapped LIVE SDK cases. An OVERDUE entry is usually not live any
 * more: when the items-planning service deploys the next occurrence it retires
 * the previous one — SDK case `Removed` (Status 77), PlanningCaseSite and
 * PlanningCase `Retracted` — while the Compliance row stays open. Such an entry
 * kept the OLD eForm, and since the compliance report reads the template from
 * `Cases.CheckListId`, Compliance/Detaljer kept listing and opening the old form.
 * The fix (`RepointRetiredOccurrencesInPlaceAsync`) re-points retired,
 * uncompleted, unanswered cases IN PLACE: CheckListId, PlanningCaseSite and
 * Compliance eForm ids move to the new eForm, EMPTY FieldValues from the old
 * template are soft-removed, and nothing is revived onto a device.
 *
 * WHY THE ROTATION IS SIMULATED WITH `docker exec`: CI does not run the
 * items-planning rotation service, so no overdue entry is ever retired there.
 * OEC03 therefore applies exactly the writes that service makes when it retires
 * a previous occurrence, straight in CI's MariaDB container (see
 * `../db-helpers.ts`), plus one EMPTY FieldValue to model someone having opened
 * the entry without answering. Without it every overdue entry would still be
 * live, the old swap path would handle them all, and the spec would pass
 * against the unfixed code.
 *
 * Two entries are tracked, and both are needed:
 *   R — retired by OEC03: proves the fix (re-pointed in place, still removed).
 *   L — left live: proves the pre-existing live-case swap still happens next to it.
 *
 * Runs in CI only (CLAUDE.md): it needs the app container, MariaDB and the
 * `mariadbtest` container name from `.github/workflows/dotnet-core-pr.yml`.
 *
 * Seed: property + worker + one WEEKLY calendar event (the calendar picks the
 * first eForm, X). Weekly, because backdating a one-off event yields a single
 * overdue occurrence and this spec needs at least two.
 */

const BASE_URL = 'http://localhost:4200';
const COMPLIANCE_URL = `${BASE_URL}/plugins/backend-configuration-pn/compliance-report`;

const property: PropertyCreateUpdate = {
  name: `oec-${generateRandmString(5)}`,
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

const rand = generateRandmString(6);
const task = `oec-task-${rand}`;

// Danish batch-action labels (BackendConfiguration da.ts), matched as in
// i/task-list-batch-start-date.spec.ts and x/task-list-batch-eform-tags.spec.ts.
const LABEL_CHANGE_START_DATE = /Skift startdato/;
const LABEL_CHANGE_EFORM = /Skift eForm/;
const LABEL_PERIOD_12_MONTHS = /^\s*12 mdr\.\s*$/;
const LABEL_STATUS_NOT_DONE = /^\s*Ikke udførte opgaver\s*$/;

// One month back is enough: the 1st of last month is at least 28 days ago, so a
// weekly series has at least four occurrences before today — and it keeps the
// synchronous backfill inside change-start-date small.
const MONTHS_BACK = 1;

const SDK_DB = customerDatabase('SDK');
const ITEMS_PLANNING_DB = customerDatabase('eform-angular-items-planning-plugin');

/** The subset of `ComplianceReportRowModel` this spec reads. */
interface ComplianceRow {
  complianceId: number;
  taskDate: string;
  title: string;
  completed: boolean;
  sdkCaseId: number;
  eformId: number | null;
  planningId: number;
  checkListId: number | null;
}

// State handed from test to test (describe.serial runs them in order, in one worker).
let seeded = false;
let backdated = false;
let eformX = 0;
let retired: ComplianceRow | undefined;
let live: ComplianceRow | undefined;
let emptyFieldValueId = 0;
let rotationSimulated = false;
let eformY = 0;

// ---------------------------------------------------------------------------
// Small utilities
// ---------------------------------------------------------------------------

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function exactText(value: string): RegExp {
  return new RegExp(`^\\s*${escapeRegExp(value)}\\s*$`);
}

function isoDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Today minus 12 months, clamped at month ends — the page's `addClampedMonths(today, -12)`. */
function twelveMonthsAgo(): Date {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  const targetMonth = d.getMonth() - 12;
  d.setMonth(targetMonth);
  if (d.getMonth() !== ((targetMonth % 12) + 12) % 12) {
    d.setDate(0);
  }
  return d;
}

function requireId(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value <= 0) {
    throw new Error(`Refusing to build SQL: ${what} is not a positive integer (${JSON.stringify(value)})`);
  }
  return value;
}

/** `-N -B` output as rows of tab-separated columns. */
function parseRows(stdout: string): string[][] {
  return stdout
    .trim()
    .split('\n')
    .filter(line => line.length > 0)
    .map(line => line.split('\t'));
}

/**
 * Scalar subquery counting the LIVE (neither retracted nor removed) PlanningCaseSites
 * of an SDK case. `caseId` must already have passed `requireId`.
 */
function liveSitesCountSql(caseId: number): string {
  return (
    `(SELECT COUNT(*) FROM \`${ITEMS_PLANNING_DB}\`.PlanningCaseSites s ` +
    `WHERE s.MicrotingSdkCaseId = ${caseId} AND s.WorkflowState NOT IN ('retracted', 'removed'))`
  );
}

/** Asserts a 2xx response whose body carries `success: true`, and returns the parsed body. */
async function expectApiSuccess(response: Response, what: string): Promise<any> {
  expect(response.ok(), `${what} must return 2xx`).toBeTruthy();
  const body = await response.json();
  expect(body?.success, `${what} must succeed: ${JSON.stringify(body?.message ?? '')}`).toBe(true);
  return body;
}

// ---------------------------------------------------------------------------
// Compliance page
// ---------------------------------------------------------------------------

/** Opens a page-level mtx-select and picks the option whose label matches `label`. */
async function pickFilterOption(page: Page, selectId: string, label: RegExp): Promise<void> {
  await page.locator(selectId).click({ timeout: UI_TIMEOUT });
  await page.locator('.ng-dropdown-panel .ng-option').filter({ hasText: label }).click({ timeout: UI_TIMEOUT });
  // `.ng-value-label`, never `.ng-value` (whose innerText carries the × glyph).
  await expect(page.locator(`${selectId} .ng-value-label`)).toHaveText(label, { timeout: UI_TIMEOUT });
}

/**
 * Opens Compliance → Detaljer for the seeded property over "12 mdr." under the
 * default "Ikke udførte opgaver" status, and returns the `/index` rows.
 *
 * The response wait is armed BEFORE the first gesture and matches on the request
 * BODY (a property is set and the period starts 12 months back), because each
 * filter change auto-fetches after a debounce: an `/index` for an earlier, partial
 * filter set must not be mistaken for the one this spec reads.
 */
async function loadDetailsRows(page: Page): Promise<ComplianceRow[]> {
  await page.goto(COMPLIANCE_URL);
  await page.locator('#complianceFilterProperty').waitFor({ state: 'visible', timeout: SLOW_API_TIMEOUT });

  const expectedFrom = isoDate(twelveMonthsAgo());
  const indexResponse = waitForApiResponse(
    page,
    `POST compliance-report/index for property "${property.name}" from ${expectedFrom}`,
    r => {
      if (!r.url().includes('/api/backend-configuration-pn/compliance-report/index')
        || r.request().method() !== 'POST') {
        return false;
      }
      let body: any;
      try {
        body = r.request().postDataJSON();
      } catch {
        return false;
      }
      return body?.propertyId != null && body?.dateFrom === expectedFrom && body?.status === 'open';
    },
    SLOW_API_TIMEOUT
  );
  ignoreUnhandledRejections(indexResponse);

  await pickFilterOption(page, '#complianceFilterProperty', exactText(property.name));
  await pickFilterOption(page, '#complianceFilterPeriod', LABEL_PERIOD_12_MONTHS);
  await page.locator('#complianceMode-details').click({ timeout: UI_TIMEOUT });
  await expect(page.locator('#complianceMode-details')).toHaveAttribute('aria-pressed', 'true', { timeout: UI_TIMEOUT });
  await expect(page.locator('#complianceFilterStatus .ng-value-label')).toHaveText(LABEL_STATUS_NOT_DONE, {
    timeout: UI_TIMEOUT,
  });

  const body = await expectApiSuccess(await indexResponse, 'compliance-report/index');
  const entities = (body?.model?.entities ?? []) as ComplianceRow[];
  expect(
    body?.model?.total,
    `compliance-report/index paged its result (total ${body?.model?.total}, ${entities.length} rows returned): ` +
      'the spec only reads the first page — widen the page size or narrow the period'
  ).toBeLessThanOrEqual(entities.length);
  return entities;
}

function findRow(rows: ComplianceRow[], complianceId: number, what: string): ComplianceRow {
  const row = rows.find(r => r.complianceId === complianceId);
  if (!row) {
    throw new Error(
      `${what} (complianceId ${complianceId}) is missing from compliance-report/index; got ` +
        JSON.stringify(rows.map(r => ({ id: r.complianceId, title: r.title, taskDate: r.taskDate })))
    );
  }
  return row;
}

function isPrepareComplete(r: Response): boolean {
  return (
    /\/api\/backend-configuration-pn\/calendar\/tasks\/\d+\/prepare-complete/.test(r.url())
    && r.request().method() === 'POST'
  );
}

// ---------------------------------------------------------------------------

test.describe.serial('Task list — eForm change moves overdue compliance entries (#1287)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto(BASE_URL);
    // login() waits for the app shell, so the app is loaded when it returns.
    await new LoginPage(page).login();
  });

  test.afterAll(async ({ browser }) => {
    // Non-fatal teardown, same shape as i/task-list-batch-start-date.spec.ts: a
    // failure here must never fail the job or mask a real test failure.
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
      await workersPage.clearTable();

      const propertiesPage = new BackendConfigurationPropertiesPage(page);
      await propertiesPage.goToProperties();
      await propertiesPage.clearTable();
    };
    let cleanupTimer: ReturnType<typeof setTimeout> | undefined;
    try {
      await Promise.race([
        cleanup(),
        new Promise(resolve => {
          cleanupTimer = setTimeout(resolve, 60000);
        }),
      ]);
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    } finally {
      clearTimeout(cleanupTimer);
    }
    try { await page.close(); } catch {}
  });

  // =========================================================================
  // Seed — property + worker + one WEEKLY event on eForm X.
  // =========================================================================
  test('seed: create property + worker + weekly calendar event', async ({ page }) => {
    // 6 min: login (up to 2 min on a cold app), a property create, a device-user
    // create (SDK provisioning, SLOW_API_TIMEOUT alone) with its list refreshes,
    // then the calendar load and one event create — the same work as the seed in
    // i/task-list-batch-start-date.spec.ts, with room for a slow CI runner.
    test.setTimeout(360000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    // selectProperty waits for the boards + week responses itself.
    await calendarPage.selectProperty(property.name);

    await calendarPage.openCreateModalAtSlot(0, 9);
    // Weekly BEFORE the rest of the form: fillAndSaveEvent saves as its last step.
    await calendarPage.selectRepeatPreset('weeklyOne');
    await calendarPage.fillAndSaveEvent(task);

    seeded = true;
  });

  // =========================================================================
  // OEC01 — backdate the series so several overdue occurrences exist.
  // =========================================================================
  test('OEC01: backdating the weekly event through "Skift startdato" creates overdue occurrences', async ({ page }) => {
    // 4 min: login (up to 2 min), the task-list load (30s), the preview (30s) and
    // the change-start-date POST, which backfills every overdue occurrence
    // synchronously through the SDK (SLOW_API_TIMEOUT).
    test.setTimeout(240000);
    expect(seeded, 'the seed test must have passed').toBe(true);

    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.search(rand);
    await expect(taskListPage.row(task)).toBeVisible({ timeout: API_TIMEOUT });
    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(LABEL_CHANGE_START_DATE);

    const expectedDate = await taskListPage.pickPastStartDate(MONTHS_BACK);
    await taskListPage.waitForStartDatePreviewResolved();

    const overdueText = (await taskListPage.startDatePreviewCount('Overdue').innerText({ timeout: UI_TIMEOUT })).trim();
    const overdueMatch = /\d+/.exec(overdueText);
    expect(overdueMatch, `the Overdue preview count must contain a number; got "${overdueText}"`).not.toBeNull();
    expect(
      Number(overdueMatch![0]),
      'backdating a weekly event must promise at least two overdue occurrences (is compliance on by default?)'
    ).toBeGreaterThanOrEqual(2);

    const changeStartDate = waitForApiResponse(
      page,
      'POST task-list/change-start-date',
      r => r.url().endsWith('/api/backend-configuration-pn/task-list/change-start-date')
        && r.request().method() === 'POST',
      SLOW_API_TIMEOUT
    );
    ignoreUnhandledRejections(changeStartDate);
    await expect(taskListPage.batchModalSubmitButton()).toBeEnabled({ timeout: UI_TIMEOUT });
    await taskListPage.submitModal();

    await expectApiSuccess(await changeStartDate, 'change-start-date');
    await taskListPage.waitForModalClosed();

    await expect(taskListPage.columnCell(task, 'taskDate')).toHaveText(expectedDate, { timeout: API_TIMEOUT });
    backdated = true;
  });

  // =========================================================================
  // OEC02 — baseline: the report lists the overdue, open entries on eForm X.
  // =========================================================================
  test('OEC02: Compliance Detaljer lists at least two overdue, not-completed entries on eForm X', async ({ page }) => {
    // 3 min: login (up to 2 min) plus one compliance page load and its /index
    // (SLOW_API_TIMEOUT).
    test.setTimeout(180000);
    expect(backdated, 'OEC01 must have backdated the event').toBe(true);

    const rows = await loadDetailsRows(page);
    const today = isoDate(new Date());
    const overdue = rows
      .filter(r => r.title === task && !r.completed && r.taskDate < today)
      .sort((a, b) => a.taskDate.localeCompare(b.taskDate));
    expect(
      overdue.length,
      `expected at least two overdue, open "${task}" rows; got ${JSON.stringify(overdue)}`
    ).toBeGreaterThanOrEqual(2);

    eformX = requireId(overdue[0].checkListId, 'the overdue rows\' checkListId');
    for (const row of overdue) {
      expect(row.checkListId, `row ${row.complianceId} must be on the event's eForm`).toBe(eformX);
      expect(row.eformId, `row ${row.complianceId}: the event's eformId must match its case template`).toBe(eformX);
      requireId(row.sdkCaseId, `row ${row.complianceId} sdkCaseId`);
    }

    // R = the oldest (the rotation retires PREVIOUS occurrences); L = the newest.
    retired = overdue[0];
    live = overdue[overdue.length - 1];
    expect(retired.sdkCaseId, 'R and L must be different SDK cases').not.toBe(live.sdkCaseId);
    expect(retired.planningId, 'R and L belong to the same planning').toBe(live.planningId);
  });

  // =========================================================================
  // OEC03 — simulate the items-planning rotation retiring entry R.
  // =========================================================================
  test('OEC03: simulate the rotation retiring overdue entry R in the database', async () => {
    // 3 min: login (up to 2 min, run by beforeEach) plus two docker exec calls of
    // API_TIMEOUT each.
    test.setTimeout(180000);
    expect(retired, 'OEC02 must have picked entry R').toBeDefined();

    const caseId = requireId(retired!.sdkCaseId, 'R.sdkCaseId');

    // Exactly the retirement the items-planning service performs, plus one EMPTY
    // FieldValue: someone opened the entry and answered nothing.
    const rotationSql = [
      `UPDATE \`${SDK_DB}\`.Cases SET WorkflowState = 'removed', Status = 77 WHERE Id = ${caseId};`,
      `UPDATE \`${ITEMS_PLANNING_DB}\`.PlanningCaseSites SET WorkflowState = 'retracted' ` +
        `WHERE MicrotingSdkCaseId = ${caseId};`,
      `UPDATE \`${ITEMS_PLANNING_DB}\`.PlanningCases pc ` +
        `JOIN \`${ITEMS_PLANNING_DB}\`.PlanningCaseSites s ON s.PlanningCaseId = pc.Id ` +
        `SET pc.WorkflowState = 'retracted' WHERE s.MicrotingSdkCaseId = ${caseId};`,
      `INSERT INTO \`${SDK_DB}\`.FieldValues (CaseId, Value, WorkflowState, CreatedAt, UpdatedAt, Version) ` +
        `VALUES (${caseId}, NULL, 'created', UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 1);`,
      'SELECT LAST_INSERT_ID();',
    ].join(' ');
    const inserted = parseRows(await runMariadbSql(rotationSql, `retire SDK case ${caseId} (entry R)`));
    expect(inserted.length, `the rotation SQL must print the new FieldValue id; got ${JSON.stringify(inserted)}`).toBe(1);
    emptyFieldValueId = requireId(Number(inserted[0][0]), 'the inserted FieldValue id');

    const verifySql =
      `SELECT c.WorkflowState, c.Status, ` +
      `(SELECT COUNT(*) FROM \`${ITEMS_PLANNING_DB}\`.PlanningCaseSites s ` +
      `WHERE s.MicrotingSdkCaseId = ${caseId} AND s.WorkflowState = 'retracted'), ` +
      `${liveSitesCountSql(caseId)} ` +
      `FROM \`${SDK_DB}\`.Cases c WHERE c.Id = ${caseId};`;
    const verify = parseRows(await runMariadbSql(verifySql, `read back retired SDK case ${caseId}`));
    expect(verify.length, `SDK case ${caseId} must exist; got ${JSON.stringify(verify)}`).toBe(1);
    const [workflowState, status, retractedSites, liveSites] = verify[0];
    expect(workflowState, `SDK case ${caseId} must now be removed`).toBe('removed');
    expect(status, `SDK case ${caseId} must now carry the retired status 77`).toBe('77');
    expect(Number(retractedSites), `case ${caseId} must have at least one retracted PlanningCaseSite`).toBeGreaterThanOrEqual(1);
    expect(Number(liveSites), `case ${caseId} must have no live PlanningCaseSite left`).toBe(0);

    // L is deliberately left alone: it must still be a LIVE case, or OEC05 could not
    // prove the live-case swap next to R's in-place re-point.
    const liveCaseId = requireId(live!.sdkCaseId, 'L.sdkCaseId');
    const liveCase = parseRows(await runMariadbSql(
      `SELECT c.WorkflowState FROM \`${SDK_DB}\`.Cases c WHERE c.Id = ${liveCaseId};`,
      `read back live SDK case ${liveCaseId} (entry L)`
    ));
    expect(liveCase.length, `SDK case ${liveCaseId} (entry L) must exist; got ${JSON.stringify(liveCase)}`).toBe(1);
    expect(['removed', 'retracted'], `SDK case ${liveCaseId} (entry L) must still be live`).not.toContain(liveCase[0][0]);

    rotationSimulated = true;
  });

  // =========================================================================
  // OEC04 — batch "Skift eForm" moves the event to eForm Y.
  // =========================================================================
  test('OEC04: batch "Skift eForm" moves the event to another eForm', async ({ page }) => {
    // 4 min: login (up to 2 min), the task-list load (30s) and the change-eform
    // POST, which runs the repair pass over every open occurrence synchronously
    // (SLOW_API_TIMEOUT), then the grid reload.
    test.setTimeout(240000);
    expect(rotationSimulated, 'OEC03 must have retired entry R').toBe(true);

    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.search(rand);
    await expect(taskListPage.row(task)).toBeVisible({ timeout: API_TIMEOUT });
    const eformXLabel = (await taskListPage.columnCell(task, 'eform').innerText({ timeout: UI_TIMEOUT })).trim();
    expect(eformXLabel.length, 'the eForm cell must show eForm X\'s label').toBeGreaterThan(0);

    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(LABEL_CHANGE_EFORM);
    const eformYLabel = await taskListPage.selectAnyOptionExcept('batchEformSelect', eformXLabel);
    expect(eformYLabel).not.toBe(eformXLabel);

    // Phase 1: submit only reveals the confirmation.
    await taskListPage.submitModal();
    await expect(page.locator('#batchEformConfirmText')).toBeVisible({ timeout: UI_TIMEOUT });

    // Phase 2: confirm posts. Both waits armed before the click.
    const isChangeEform = (url: string, method: string) =>
      url.endsWith('/api/backend-configuration-pn/task-list/change-eform') && method === 'POST';
    const changeEformRequest = waitForApiRequest(
      page,
      'POST task-list/change-eform (request)',
      r => isChangeEform(r.url(), r.method()),
      UI_TIMEOUT
    );
    const changeEformResponse = waitForApiResponse(
      page,
      'POST task-list/change-eform (response)',
      r => isChangeEform(r.url(), r.request().method()),
      SLOW_API_TIMEOUT
    );
    ignoreUnhandledRejections(changeEformRequest, changeEformResponse);
    await taskListPage.confirmModal();

    const request = await changeEformRequest;
    eformY = requireId(request.postDataJSON()?.eformId, 'change-eform payload eformId');
    expect(eformY, 'eForm Y must be a different template than X').not.toBe(eformX);

    await expectApiSuccess(await changeEformResponse, 'change-eform');
    await taskListPage.waitForModalClosed();

    await expect(taskListPage.columnCell(task, 'eform')).toHaveText(eformYLabel, { timeout: API_TIMEOUT });
  });

  // =========================================================================
  // OEC05 — the report now shows BOTH overdue entries on eForm Y.
  // =========================================================================
  test('OEC05: Compliance lists both the retired and the live overdue entry on eForm Y, still open', async ({ page }) => {
    // 3 min: login (up to 2 min) plus one compliance page load and its /index
    // (SLOW_API_TIMEOUT).
    test.setTimeout(180000);
    expect(eformY, 'OEC04 must have changed the eForm').toBeGreaterThan(0);

    const rows = await loadDetailsRows(page);
    const retiredAfter = findRow(rows, retired!.complianceId, 'retired entry R');
    const liveAfter = findRow(rows, live!.complianceId, 'live entry L');
    for (const [what, after] of [['retired entry R', retiredAfter], ['live entry L', liveAfter]] as const) {
      expect(after.completed, `${what} must still be not completed`).toBe(false);
      expect(after.checkListId, `${what}: the case template must have moved off eForm X`).not.toBe(eformX);
      expect(after.checkListId, `${what}: the case template must be eForm Y`).toBe(eformY);
      expect(after.eformId, `${what}: the event's eForm must be Y`).toBe(eformY);
    }
    // R was re-pointed in place, not redeployed...
    expect(retiredAfter.sdkCaseId, 'R must keep its SDK case').toBe(retired!.sdkCaseId);
    // ...while L went through the live swap: SwapCaseEformAsync creates a replacement
    // case and re-points Compliance.MicrotingSdkCaseId at it.
    expect(liveAfter.sdkCaseId, 'L must have been swapped onto a replacement SDK case').not.toBe(live!.sdkCaseId);
  });

  // =========================================================================
  // OEC06 — database: R re-pointed in place, not revived, empty answer removed.
  // =========================================================================
  test('OEC06: the retired case stays removed, carries eForm Y, and its empty FieldValue is soft-removed', async () => {
    // 3 min: login (up to 2 min, run by beforeEach) plus one docker exec (API_TIMEOUT).
    test.setTimeout(180000);
    expect(eformY, 'OEC04 must have changed the eForm').toBeGreaterThan(0);

    const caseId = requireId(retired!.sdkCaseId, 'R.sdkCaseId');
    const fieldValueId = requireId(emptyFieldValueId, 'the empty FieldValue id');
    const sql =
      `SELECT c.WorkflowState, c.CheckListId, fv.WorkflowState, ${liveSitesCountSql(caseId)} ` +
      `FROM \`${SDK_DB}\`.Cases c JOIN \`${SDK_DB}\`.FieldValues fv ON fv.Id = ${fieldValueId} ` +
      `WHERE c.Id = ${caseId};`;
    const rows = parseRows(await runMariadbSql(sql, `read back re-pointed SDK case ${caseId}`));
    expect(rows.length, `SDK case ${caseId} and FieldValue ${fieldValueId} must exist; got ${JSON.stringify(rows)}`).toBe(1);
    const [caseState, checkListId, fieldValueState, liveSites] = rows[0];
    expect(caseState, `SDK case ${caseId} must still be removed — never revived onto a device`).toBe('removed');
    expect(Number(liveSites), `SDK case ${caseId} must not have a live PlanningCaseSite again`).toBe(0);
    expect(Number(checkListId), `SDK case ${caseId} must now point at eForm Y`).toBe(eformY);
    expect(fieldValueState, `the empty FieldValue ${fieldValueId} of the old template must be soft-removed`).toBe('removed');
  });

  // =========================================================================
  // OEC07 — UI: opening R from Detaljer renders eForm Y.
  // =========================================================================
  test('OEC07: opening the retired entry from Compliance Detaljer renders eForm Y', async ({ page }) => {
    // 5 min: login (up to 2 min), the compliance page and its /index
    // (SLOW_API_TIMEOUT), then prepare-complete and the template GET
    // (SLOW_API_TIMEOUT each, awaited in sequence).
    test.setTimeout(300000);
    expect(eformY, 'OEC04 must have changed the eForm').toBeGreaterThan(0);

    await loadDetailsRows(page);
    const row = page.locator(`.compliance-details .compliance-details__row[data-compliance-id="${retired!.complianceId}"]`);
    await expect(row, 'R must render exactly once in Detaljer').toHaveCount(1, { timeout: API_TIMEOUT });
    await expect(row).toHaveClass(/is-clickable/, { timeout: UI_TIMEOUT });

    const prepareComplete = waitForApiResponse(page, 'POST calendar/tasks/{id}/prepare-complete', isPrepareComplete, SLOW_API_TIMEOUT);
    const templateGet = waitForApiResponse(
      page,
      `GET /api/templates/get/${eformY}`,
      r => new RegExp(`/api/templates/get/${eformY}(\\?|$)`).test(r.url()) && r.request().method() === 'GET',
      SLOW_API_TIMEOUT
    );
    ignoreUnhandledRejections(prepareComplete, templateGet);
    // The title cell, not the row centre: the actions cell stops propagation.
    await row.locator('.compliance-details__title').click({ timeout: UI_TIMEOUT });

    const body = await expectApiSuccess(await prepareComplete, 'prepare-complete');
    expect(body.model.complianceId, 'prepare-complete must resolve entry R').toBe(retired!.complianceId);
    expect(body.model.sdkCaseId, 'prepare-complete must resolve R\'s own (retired) case').toBe(retired!.sdkCaseId);
    expect(body.model.templateId, 'the complete modal must render eForm Y, not X').toBe(eformY);

    const modal = page.locator('app-calendar-complete-event-modal');
    await expect(modal).toHaveCount(1, { timeout: UI_TIMEOUT });
    const template = await templateGet;
    expect(template.ok(), `GET /api/templates/get/${eformY} must return 2xx`).toBeTruthy();

    await page.keyboard.press('Escape');
    await expect(modal, 'Escape must close the complete modal').toHaveCount(0, { timeout: UI_TIMEOUT });
  });
});
