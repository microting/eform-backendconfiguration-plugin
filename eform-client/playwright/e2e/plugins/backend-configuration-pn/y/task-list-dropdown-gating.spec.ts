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
 * Task list BATCH-DROPDOWN GATING suite (backend-configuration-task-list-page
 * feature, shard y). Complements `x/task-list-page.spec.ts` PP9 (which
 * proves the 11-option/3-group shape, the 4-disabled/0-disabled counts, and
 * that clicking a disabled option is a no-op) with three angles PP9 does
 * NOT cover, all read directly off `task-list-page.component.ts`/`.html`
 * while writing this suite:
 *   - `#taskListBatchAction`'s bound value (`.ng-value-label`), not just the
 *     modal, after a no-op disabled-option click.
 *   - Going from a single-property filter BACK to no filter in the same
 *     session (not a page reload) — `batchActions` is memoized on
 *     `${singleSelectedPropertyId}|${lang}` (`_batchActionsCache`), so this
 *     is the one path that actually exercises the cache-invalidation branch;
 *     PP9 only ever goes no-filter -> single-property, never back.
 *   - `pendingAction` — `onBatchActionPicked` resets it to `null` the
 *     instant an action is picked (BEFORE the modal opens, "reset dropdown
 *     (mockup behavior)"), so the dropdown already shows its placeholder
 *     while the modal is still open; cancelling doesn't need to reset
 *     anything further. The real regression surface is whether picking the
 *     SAME action id twice in a row still fires `(change)` a second time —
 *     it does, because the bound value passes through `null` in between.
 *
 * Note property.propertyIds filter clear: this suite clears the filter
 * without a page reload via `clearPropertyFilter()` (ng-select's clear-all
 * × button). An earlier approach re-clicked the selected option to toggle
 * it off, but this ng-select build doesn't reliably deselect that way (see
 * DG2's inline comment / CI shard-y rounds 1-2), so the explicit clear-all
 * is used instead.
 *
 * Seed: one property + one worker + one task (dropdown gating doesn't
 * exercise any modal's submit path, so a single row is enough).
 */

const property: PropertyCreateUpdate = {
  name: `tlg-${generateRandmString(5)}`,
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
const task = `dg-task-${rand}`;

// Danish translations of the relevant batch-action dropdown labels
// (BackendConfiguration da.ts, lines 574/579) — same source as the other
// x/ batch specs.
const LABEL_ASSIGN = 'Flyt valgte til medarbejder';
const LABEL_DELETE = 'Slet valgte';

let seeded = false;

// NOT serial: DG1-DG3 are mutation-independent — each does its own `goto()`
// and only reads/gates against the shared seeded task (DG3 cancels its delete
// modal, never submits), so none depends on another's state. Plain `describe`
// (with `workers:1` + `fullyParallel:false`, the seed test still runs first by
// declaration order) lets every test run even if one fails, so all failures
// surface in a single CI round instead of one-per-round.
test.describe('Task list — batch-action dropdown gating', () => {
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
  // Seed — one property + one worker + one task.
  // -----------------------------------------------------------------------
  test('seed: create property + worker + task', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();
    await calendarPage.selectProperty(property.name);
    await page.waitForTimeout(1000);
    await calendarPage.openCreateModalAtSlot(0, 9);
    await calendarPage.fillAndSaveEvent(task);

    seeded = true;
  });

  // =======================================================================
  // DG1 — clicking a disabled option is a no-op: no modal opens AND the
  // dropdown's own bound value stays empty (PP9 only asserts the former).
  // =======================================================================
  test('DG1: clicking a disabled option opens no modal and leaves the dropdown value empty', async ({ page }) => {
    expect(seeded).toBe(true);
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    // No property filter -> assign/reassign/addWorker/copy are disabled.
    await taskListPage.search(task);
    await expect(taskListPage.row(task)).toBeVisible();
    await taskListPage.selectRow(task);

    await taskListPage.openBatchActionPanel();
    const assignOption = taskListPage.batchActionOption(new RegExp(LABEL_ASSIGN));
    await expect(assignOption).toHaveClass(/ng-option-disabled/);
    await assignOption.click();
    await page.waitForTimeout(400);

    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
    await expect(taskListPage.getModalTaskList()).toHaveCount(0);
    // The click never registered a selection, so #taskListBatchAction still
    // renders its placeholder, not a `.ng-value-label`.
    await expect(page.locator('#taskListBatchAction .ng-value-label')).toHaveCount(0);

    await page.keyboard.press('Escape');
  });

  // =======================================================================
  // DG2 — gating recomputes live: single-property filter -> 0 disabled;
  // clearing the SAME filter (no reload) -> back to 4 disabled.
  // =======================================================================
  test('DG2: disabled count recomputes when the property filter is cleared without a reload', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(task)).toBeVisible();
    await taskListPage.selectRow(task);

    await taskListPage.openBatchActionPanel();
    expect(await taskListPage.countDisabledBatchActions()).toBe(0);
    await page.keyboard.press('Escape');

    // Clear the property filter live (no navigation) via ng-select's
    // clear-all (×) button. NOT by re-clicking the selected option:
    // CI shard-y rounds 1-2 proved that reclick, in this ng-select build,
    // leaves the chip in place while still firing a filters change +
    // tasks reload — so the filter wasn't actually cleared AND the row
    // selection got wiped, permanently disabling the dropdown.
    // `onFiltersChanged` -> `loadTasks()` clears `selection` (a fresh grid
    // load never carries selection forward), so the row is reselected
    // afterwards; `clearPropertyFilter`/`selectRow` both await the reload
    // and verify the checkbox stuck.
    await taskListPage.clearPropertyFilter();
    await expect(taskListPage.row(task)).toBeVisible();
    await taskListPage.selectRow(task);

    await taskListPage.openBatchActionPanel();
    expect(await taskListPage.countDisabledBatchActions()).toBe(4);
    await page.keyboard.press('Escape');
  });

  // =======================================================================
  // DG3 — the dropdown resets to its placeholder once the action's modal
  // CLOSES (task-list-page.component.ts resets `pendingAction` in the
  // dialog's afterClosed, not on pick — while the modal is open the dropdown
  // legitimately still shows the picked action, and resetting mid-overlay
  // fights ng-select's just-committed selection; see the component comment /
  // CI shard-y DG3 rounds 3-4). Cancelling out and picking the SAME action
  // again must still open a fresh modal — the real regression this guards is
  // `pendingAction`/ng-select's internal selection staying stuck at a
  // non-null value, which would make a repeat pick of the same id a no-op
  // change.
  // =======================================================================
  test('DG3: dropdown resets after the modal closes; picking the same action again still opens the modal', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.search(task);
    await expect(taskListPage.row(task)).toBeVisible();
    await taskListPage.selectRow(task);

    await taskListPage.pickBatchAction(new RegExp(LABEL_DELETE));
    await expect(taskListPage.getModalTaskList()).toBeVisible();

    await taskListPage.cancelModal();
    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
    // afterClosed reset the dropdown to its placeholder — no bound value.
    await expect(page.locator('#taskListBatchAction .ng-value-label')).toHaveCount(0);

    // Selection survives a cancel (task-list-page.component.ts only clears
    // it inside the `if (result)` branch) — the row is still selected, so
    // picking Delete again should reopen the modal without needing to
    // reselect the row. This is the load-bearing assertion: it only passes
    // if the afterClosed reset actually cleared ng-select's internal
    // selection, so re-picking Delete fires a fresh (change).
    await taskListPage.pickBatchAction(new RegExp(LABEL_DELETE));
    await expect(taskListPage.getModalTaskList()).toBeVisible();
    await taskListPage.cancelModal();
    await expect(page.locator('mat-dialog-container')).toHaveCount(0);

    // The task must still exist — this suite never submits Delete.
    await taskListPage.goto();
    await taskListPage.search(task);
    await expect(taskListPage.row(task)).toBeVisible();
  });
});
