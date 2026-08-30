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
 * Task list BATCH MODAL CANCEL suite (backend-configuration-task-list-page
 * feature, shard y): one representative action per of the FIVE modal
 * COMPONENTS covered here (`batch-worker-modal`, `batch-eform-modal`,
 * `batch-tags-modal`, `batch-copy-modal`, `batch-delete-modal` — not per the
 * 10 batch actions), proving `#batchModalCancel` (added to those templates
 * specifically for this suite — see the sibling template edits — they
 * previously only had an unidentified `.btn-cancel`) leaves everything
 * exactly as it was:
 *   - `hide()` -> `dialogRef.close()` with NO result, so `openBatchModal`'s
 *     `afterClosed()` subscriber in `task-list-page.component.ts` never
 *     enters its `if (result)` branch — `this.selection` is NOT cleared and
 *     `loadTasks()` is NOT re-run, unlike every x/ happy-path spec.
 *   - Consequently `#taskListSelectionCount` — which only exists at all
 *     while `selection.size > 0` — must show the exact same text before and
 *     after cancel, and the acted-on row's relevant cell must be byte-equal
 *     before/after.
 *
 * The two LATER modal components carry the same `#batchModalCancel` and the
 * same inert-cancel contract, but are cancel-tested in their own suites
 * alongside the behaviour that is unique to them:
 * `batch-compliance-modal` in `e/task-list-batch-compliance.spec.ts` and
 * `batch-start-date-modal` (whose cancel must additionally discard a
 * RESOLVED preview) in `i/task-list-batch-start-date.spec.ts` SD3.
 *
 * eForm modal cancel here is PHASE-1 cancel (before `#batchModalSubmit` is
 * even clicked) — deliberately different from
 * `y/task-list-modal-validation.spec.ts` V3, which cancels AT the two-phase
 * confirm step; together they cover both cancel points without duplicating
 * either.
 *
 * Seed: one property + one worker + one task — no modal here is ever
 * actually submitted, so a single row covers all five.
 */

const property: PropertyCreateUpdate = {
  name: `tlk-${generateRandmString(5)}`,
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
const task = `mc-task-${rand}`;

// Danish translations of the relevant batch-action dropdown labels
// (BackendConfiguration da.ts, lines 574-579) — same source as the x/
// batch specs.
const LABEL_ASSIGN = 'Flyt valgte til medarbejder';
const LABEL_CHANGE_EFORM = 'Skift eForm';
const LABEL_ADD_TAGS = 'Tilføj tags';
const LABEL_COPY = 'Kopiér til ejendom';
const LABEL_DELETE = 'Slet valgte';

let seeded = false;

// NOT serial: MC1-MC5 all CANCEL their modals (never submit), so none mutates
// persistent state — each is independent given the shared seed. Plain
// `describe` (with `workers:1` + `fullyParallel:false`, the seed test still
// runs first by declaration order) lets every test run even if one fails, so
// all failures surface in a single CI round instead of one-per-round.
test.describe('Task list — batch modal cancel', () => {
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
  // MC1 — worker modal (assign): cancel leaves the assignedTo cell and the
  // selection count untouched.
  // =======================================================================
  test('MC1: cancelling the worker modal (assign) changes nothing', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await expect(taskListPage.row(task)).toBeVisible();
    await taskListPage.selectRow(task);
    const countBefore = (await page.locator('#taskListSelectionCount').innerText()).trim();
    const assignedBefore = (await taskListPage.columnCell(task, 'assignedTo').innerText()).trim();

    await taskListPage.pickBatchAction(new RegExp(LABEL_ASSIGN));
    await expect(taskListPage.getModalTaskList()).toBeVisible();
    await taskListPage.cancelModal();

    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
    const assignedAfter = (await taskListPage.columnCell(task, 'assignedTo').innerText()).trim();
    expect(assignedAfter).toBe(assignedBefore);
    const countAfter = (await page.locator('#taskListSelectionCount').innerText()).trim();
    expect(countAfter).toBe(countBefore);
    await expect(taskListPage.rowCheckbox(task)).toHaveClass(/mat-mdc-checkbox-checked/);
  });

  // =======================================================================
  // MC2 — eForm modal: PHASE-1 cancel (before ever clicking submit) leaves
  // the eForm cell untouched. (The confirm-PHASE cancel is covered by
  // y/task-list-modal-validation.spec.ts V3.)
  // =======================================================================
  test('MC2: cancelling the eForm modal before submit changes nothing', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await expect(taskListPage.row(task)).toBeVisible();
    await taskListPage.selectRow(task);
    const countBefore = (await page.locator('#taskListSelectionCount').innerText()).trim();
    const eformBefore = (await taskListPage.columnCell(task, 'eform').innerText()).trim();

    await taskListPage.pickBatchAction(new RegExp(LABEL_CHANGE_EFORM));
    await expect(taskListPage.getModalTaskList()).toBeVisible();
    // Cancel WITHOUT touching #batchModalSubmit — the confirm text/step
    // must never have appeared.
    await expect(page.locator('#batchEformConfirmText')).toHaveCount(0);
    await taskListPage.cancelModal();

    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
    const eformAfter = (await taskListPage.columnCell(task, 'eform').innerText()).trim();
    expect(eformAfter).toBe(eformBefore);
    const countAfter = (await page.locator('#taskListSelectionCount').innerText()).trim();
    expect(countAfter).toBe(countBefore);
  });

  // =======================================================================
  // MC3 — tags modal (addTags): cancel leaves the Tags cell untouched.
  // =======================================================================
  test('MC3: cancelling the tags modal (add tags) changes nothing', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await expect(taskListPage.row(task)).toBeVisible();
    await taskListPage.selectRow(task);
    const countBefore = (await page.locator('#taskListSelectionCount').innerText()).trim();
    const tagsBefore = (await taskListPage.columnCell(task, 'tags').innerText()).trim();

    await taskListPage.pickBatchAction(new RegExp(LABEL_ADD_TAGS));
    await expect(taskListPage.getModalTaskList()).toBeVisible();
    // Pick a tag (proves cancel discards an in-progress, otherwise-valid
    // pick too, not just an untouched form).
    await page.locator('#batchTagsSelect').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.keyboard.press('Escape');
    await taskListPage.cancelModal();

    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
    const tagsAfter = (await taskListPage.columnCell(task, 'tags').innerText()).trim();
    expect(tagsAfter).toBe(tagsBefore);
    const countAfter = (await page.locator('#taskListSelectionCount').innerText()).trim();
    expect(countAfter).toBe(countBefore);
  });

  // =======================================================================
  // MC4 — copy modal: cancel creates no copy; the acted-on row's own title
  // cell is untouched (copy never mutates the source row).
  // =======================================================================
  test('MC4: cancelling the copy modal changes nothing', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await expect(taskListPage.row(task)).toBeVisible();
    await taskListPage.selectRow(task);
    const countBefore = (await page.locator('#taskListSelectionCount').innerText()).trim();
    const titleBefore = (await taskListPage.columnCell(task, 'title').innerText()).trim();

    await taskListPage.pickBatchAction(new RegExp(LABEL_COPY));
    await expect(taskListPage.getModalTaskList()).toBeVisible();
    await taskListPage.cancelModal();

    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
    const titleAfter = (await taskListPage.columnCell(task, 'title').innerText()).trim();
    expect(titleAfter).toBe(titleBefore);
    const countAfter = (await page.locator('#taskListSelectionCount').innerText()).trim();
    expect(countAfter).toBe(countBefore);
  });

  // =======================================================================
  // MC5 — delete modal: cancel specifically must leave the row PRESENT.
  // =======================================================================
  test('MC5: cancelling the delete modal leaves the row present', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await expect(taskListPage.row(task)).toBeVisible();
    await taskListPage.selectRow(task);
    const countBefore = (await page.locator('#taskListSelectionCount').innerText()).trim();

    await taskListPage.pickBatchAction(new RegExp(LABEL_DELETE));
    await expect(taskListPage.getModalTaskList()).toBeVisible();
    await taskListPage.cancelModal();

    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
    await expect(taskListPage.row(task)).toBeVisible();
    const countAfter = (await page.locator('#taskListSelectionCount').innerText()).trim();
    expect(countAfter).toBe(countBefore);

    // Final proof the row survives a full reload too (not just the
    // in-memory `tasks` array that a cancelled dialog never touched).
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(task)).toBeVisible();
  });
});
