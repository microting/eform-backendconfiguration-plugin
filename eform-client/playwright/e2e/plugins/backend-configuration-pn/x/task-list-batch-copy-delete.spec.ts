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
 * Task list BATCH COPY/DELETE suite (backend-configuration-task-list-page
 * feature), wired through `BackendConfigurationTaskListService` (host app
 * source, read while writing this suite):
 *   - Copy requires a target property + a board belonging to it + an
 *     explicit worker (server-side validated to belong to the target
 *     property — `Copy`'s `siteIsTargetPropertyWorker` guard) + a start
 *     date. The copy ALWAYS starts `TaskWizardStatuses.NotActive` (an
 *     admin must review it), so a fresh copy always renders with the
 *     "Active" badge showing "No" (`.badge.nej` — asserted by class, not
 *     translated text).
 *   - Copy is property-scoped in the batch dropdown (only offered when
 *     `#taskListPropertyFilter` has exactly one property selected) — same
 *     as assign/reassign/addWorker (`x/task-list-batch-workers.spec.ts`).
 *   - Delete's modal (`BatchDeleteModalComponent`) has no two-phase confirm
 *     of its own — the "Are you sure you want to delete?" dialog IS the
 *     confirmation; `#batchModalSubmit` deletes directly (no
 *     `#batchModalConfirm` — unlike changeEform's modal).
 *
 * Every property in this repo auto-provisions a lowest-id "Default" board
 * on creation (documented project convention), so `batchCopyBoardSelect`
 * always has exactly one option for a freshly created target property —
 * picking it via `.first()` is deterministic, not index-fragile.
 *
 * Seed: SOURCE property + worker + one task, TARGET property + a worker
 * belonging to the target property (required by the copy endpoint's
 * cross-property validation).
 *
 * CD1 (copy):   copy the source task onto the target property/board/worker
 *     -> switch the property filter to the target -> the copy is present
 *     with the same title and an inactive ("No") status badge.
 * CD2 (delete): select the copy in the target-filtered view -> delete ->
 *     row gone.
 */

const sourceProperty: PropertyCreateUpdate = {
  name: `tlc-src-${generateRandmString(5)}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const targetProperty: PropertyCreateUpdate = {
  name: `tlc-tgt-${generateRandmString(5)}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const rand = generateRandmString(6);
const sourceWorker: PropertyWorker = {
  name: `src-w-${rand}`, surname: generateRandmString(5), language: 'Dansk',
  properties: [sourceProperty.name], workerEmail: `srcw-${rand}@test.com`,
};
const targetWorker: PropertyWorker = {
  name: `tgt-w-${rand}`, surname: generateRandmString(5), language: 'Dansk',
  properties: [targetProperty.name], workerEmail: `tgtw-${rand}@test.com`,
};

const task = `cd-task-${rand}`;

// Danish translations of the relevant batch-action dropdown labels
// (BackendConfiguration da.ts, lines 578-579).
const LABEL_COPY = 'Kopiér til ejendom';
const LABEL_DELETE = 'Slet valgte';

let seeded = false;

test.describe.serial('Task list — batch copy/delete actions', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(1500);
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
  // Seed — source property + worker + task, target property + worker.
  // -----------------------------------------------------------------------
  test('seed: source property/worker/task + target property/worker', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(sourceProperty);
    await propertiesPage.createProperty(targetProperty);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(sourceWorker);
    await workersPage.create(targetWorker);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();
    await calendarPage.selectProperty(sourceProperty.name);
    await page.waitForTimeout(1000);
    await calendarPage.openCreateModalAtSlot(0, 9);
    await calendarPage.fillAndSaveEvent(task);

    seeded = true;
  });

  // =======================================================================
  // CD1 — copy the source task onto the target property; the copy is
  // inactive by design.
  // =======================================================================
  test('CD1: copy to another property creates an inactive copy there', async ({ page }) => {
    expect(seeded).toBe(true);
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.selectProperty(sourceProperty.name);
    await expect(taskListPage.row(task)).toBeVisible();

    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_COPY));

    await taskListPage.selectModalOption('batchCopyPropertySelect', targetProperty.name);
    // Board/worker lists are fetched async once the target property is
    // picked (BatchCopyModalComponent.onPropertyChange) — give them time.
    await page.waitForTimeout(1200);
    await taskListPage.selectModalOptionFirst('batchCopyBoardSelect');
    await taskListPage.selectModalOption('batchCopyWorkerSelect', targetWorker.name);
    await taskListPage.pickFutureCopyDate();

    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();
    await page.waitForTimeout(1000);

    // Switch the filter to the TARGET property — the copy lives there, not
    // under the source (still-selected) filter.
    await taskListPage.goto();
    await taskListPage.selectProperty(targetProperty.name);
    await expect(taskListPage.row(task)).toBeVisible();
    await expect(taskListPage.columnCell(task, 'status').locator('.badge.nej')).toBeVisible();
  });

  // =======================================================================
  // CD2 — delete the copy from the target-filtered view.
  // =======================================================================
  test('CD2: delete removes the selected row', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.selectProperty(targetProperty.name);
    await expect(taskListPage.row(task)).toBeVisible();

    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_DELETE));
    // BatchDeleteModalComponent has no separate confirm step — the dialog
    // itself IS the confirmation; #batchModalSubmit deletes directly.
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();
    await page.waitForTimeout(1000);

    await expect(taskListPage.row(task)).toHaveCount(0);
  });
});
