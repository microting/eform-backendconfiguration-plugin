import { test, expect } from '@playwright/test';
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

/**
 * Task list BATCH WORKER-ACTIONS suite (backend-configuration-task-list-page
 * feature): assign / addWorker / reassign, wired through
 * `BackendConfigurationTaskListService` (host app,
 * `Services/BackendConfigurationTaskListService/BackendConfigurationTaskListService.cs`)
 * — verified against that source while writing this suite:
 *   - Assign REPLACES a task's whole worker list (`update.Sites = [siteId]`).
 *   - AddWorker APPENDS (dedup no-op if already present).
 *   - Reassign only touches tasks whose current worker list CONTAINS
 *     `fromSiteId` — others are silently skipped, "moving only the
 *     matching row".
 *
 * These three actions are property-scoped (`batchActions` getter,
 * `task-list-page.component.ts`) — only offered in the batch dropdown when
 * `#taskListPropertyFilter` has EXACTLY ONE property selected, so every
 * test here filters to the seeded property first.
 *
 * Seed: one property, worker W1 (the only worker that exists while both
 * tasks are created, so both start assigned to exactly W1 — deterministic,
 * sidesteps `fillAndSaveEvent`'s hardcoded "pick the first assignee option"
 * behavior), then two more workers W2/W3 created afterwards.
 *
 * Sequenced (single serial suite, state carries forward):
 *   WK1 (assign):    select taskC + taskD -> assign to W2.
 *                     Both rows end up assigned to ONLY W2.
 *   WK2 (addWorker):  select taskC only -> add W3.
 *                     taskC -> [W2, W3]; taskD untouched (-> still [W2]).
 *   WK3 (reassign):   select taskC + taskD -> reassign W3 -> W1.
 *                     Only taskC contains W3, so only taskC moves
 *                     (-> [W2, W1]); taskD (never had W3) stays [W2].
 */

const property: PropertyCreateUpdate = {
  name: `tlw-${generateRandmString(5)}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const rand = generateRandmString(6);
const w1: PropertyWorker = {
  name: `W1-${rand}`, surname: generateRandmString(5), language: 'Dansk',
  properties: [property.name], workerEmail: `w1-${rand}@test.com`,
};
const w2: PropertyWorker = {
  name: `W2-${rand}`, surname: generateRandmString(5), language: 'Dansk',
  properties: [property.name], workerEmail: `w2-${rand}@test.com`,
};
const w3: PropertyWorker = {
  name: `W3-${rand}`, surname: generateRandmString(5), language: 'Dansk',
  properties: [property.name], workerEmail: `w3-${rand}@test.com`,
};

const taskC = `wk-taskC-${rand}`;
const taskD = `wk-taskD-${rand}`;

// Danish translations of the batch-action dropdown labels (BackendConfiguration
// da.ts, lines 571-579) — the seeded admin's default UI locale is Danish
// (matches every other calendar/admin-gating spec in this suite).
const LABEL_ASSIGN = 'Flyt valgte til medarbejder';
const LABEL_ADD_WORKER = 'Tilføj medarbejder';
const LABEL_REASSIGN = 'Flyt fra medarbejder til medarbejder';

let seeded = false;

test.describe.serial('Task list — batch worker actions', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(1500);

    if (seeded) {
      const taskListPage = new TaskListPage(page);
      await taskListPage.goto();
      await taskListPage.selectProperty(property.name);
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
  // Seed — property + W1 + two tasks (both assigned to W1, the only worker
  // that exists yet), then W2/W3.
  // -----------------------------------------------------------------------
  test('seed: property, W1, two W1-assigned tasks, then W2/W3', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(w1);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();
    await calendarPage.selectProperty(property.name);
    await page.waitForTimeout(1000);

    await calendarPage.openCreateModalAtSlot(0, 9);
    await calendarPage.fillAndSaveEvent(taskC);
    await calendarPage.openCreateModalOnCurrentWeek(1, 9);
    await calendarPage.fillAndSaveEvent(taskD);

    await workersPage.goToPropertyWorkers();
    await workersPage.create(w2);
    await workersPage.create(w3);

    seeded = true;
  });

  // =======================================================================
  // WK1 — assign 2 selected rows to W2: both end up assigned to ONLY W2.
  // =======================================================================
  test('WK1: assign replaces the worker list on both selected rows', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.selectRow(taskC);
    await taskListPage.selectRow(taskD);
    await taskListPage.pickBatchAction(new RegExp(LABEL_ASSIGN));
    await taskListPage.selectModalOption('batchWorkerSelect', w2.name);
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();
    // The property filter set in beforeEach persists across the automatic
    // reload triggered by the modal's afterClosed() -> loadTasks(); no need
    // to reselect it (doing so would just toggle it back OFF).
    await page.waitForTimeout(1000);

    const cellC = (await taskListPage.columnCell(taskC, 'assignedTo').innerText()).trim();
    const cellD = (await taskListPage.columnCell(taskD, 'assignedTo').innerText()).trim();
    expect(cellC).toContain(w2.name);
    expect(cellC).not.toContain(w1.name);
    expect(cellD).toContain(w2.name);
    expect(cellD).not.toContain(w1.name);
  });

  // =======================================================================
  // WK2 — addWorker on taskC only: taskC gains W3 alongside W2; taskD
  // (not selected) is untouched.
  // =======================================================================
  test('WK2: add-worker appends only to the selected row', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.selectRow(taskC);
    await taskListPage.pickBatchAction(new RegExp(LABEL_ADD_WORKER));
    await taskListPage.selectModalOption('batchWorkerSelect', w3.name);
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();
    // The property filter set in beforeEach persists across the automatic
    // reload triggered by the modal's afterClosed() -> loadTasks(); no need
    // to reselect it (doing so would just toggle it back OFF).
    await page.waitForTimeout(1000);

    const cellC = (await taskListPage.columnCell(taskC, 'assignedTo').innerText()).trim();
    const cellD = (await taskListPage.columnCell(taskD, 'assignedTo').innerText()).trim();
    expect(cellC).toContain(w2.name);
    expect(cellC).toContain(w3.name);
    expect(cellD).toContain(w2.name);
    expect(cellD).not.toContain(w3.name);
  });

  // =======================================================================
  // WK3 — reassign W3 -> W1 on both selected rows: only taskC (which
  // contains W3) moves; taskD (never had W3) stays untouched.
  // =======================================================================
  test('WK3: reassign moves only the row that has the "from" worker', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.selectRow(taskC);
    await taskListPage.selectRow(taskD);
    await taskListPage.pickBatchAction(new RegExp(LABEL_REASSIGN));
    await taskListPage.selectModalOption('batchWorkerFromSelect', w3.name);
    await taskListPage.selectModalOption('batchWorkerToSelect', w1.name);
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();
    // The property filter set in beforeEach persists across the automatic
    // reload triggered by the modal's afterClosed() -> loadTasks(); no need
    // to reselect it (doing so would just toggle it back OFF).
    await page.waitForTimeout(1000);

    const cellC = (await taskListPage.columnCell(taskC, 'assignedTo').innerText()).trim();
    const cellD = (await taskListPage.columnCell(taskD, 'assignedTo').innerText()).trim();
    expect(cellC).toContain(w1.name);
    expect(cellC).toContain(w2.name);
    expect(cellC).not.toContain(w3.name);
    // taskD never had W3 assigned, so reassign (fromSiteId=W3) skips it —
    // it must be exactly the same as after WK2 (only W2).
    expect(cellD).toContain(w2.name);
    expect(cellD).not.toContain(w1.name);
    expect(cellD).not.toContain(w3.name);
  });
});
