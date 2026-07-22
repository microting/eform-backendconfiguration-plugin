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
 * Task list BATCH MODAL VALIDATION suite (backend-configuration-task-list-page
 * feature, shard y). Where `x/task-list-batch-*.spec.ts` prove each batch
 * action's happy-path EFFECT (submit -> cell changes), this suite proves the
 * `#batchModalSubmit` GATING itself — the `valid` getter on each modal
 * component (host app source, read while writing this suite) — plus one
 * mechanism (`batch-worker-modal`'s from/to mutual-exclusion) and one
 * cross-property leak check (`batch-copy-modal`'s worker list) that none of
 * the x/ specs exercise. Every test here CANCELS instead of submitting
 * (`#batchModalCancel`), so the seeded row/property state never mutates
 * across tests and the suite needs only one task.
 *
 * Modal source read for this suite:
 *   - `batch-worker-modal.component.ts`: `valid` = `siteId != null` for
 *     assign/addWorker, `fromSiteId != null && toSiteId != null &&
 *     fromSiteId !== toSiteId` for reassign. `fromWorkers`/`toWorkers` are
 *     STABLE fields (not getters) refreshed by `onFromSiteIdChange`/
 *     `onToSiteIdChange`, each filtering the OTHER select's current pick out
 *     of its own item list — so picking a "from" worker removes it from the
 *     "to" dropdown's options, not just from being independently pickable.
 *   - `batch-eform-modal.component.ts`: `submit()` only flips `confirming =
 *     true` (shows `#batchEformConfirmText` + swaps `#batchModalSubmit` for
 *     `#batchModalConfirm`) — the actual `changeEform` call only happens in
 *     `confirm()`. `#batchModalCancel` sits OUTSIDE the `*ngIf="confirming"`
 *     block, so it's clickable in both phases and always just closes with no
 *     result.
 *   - `batch-tags-modal.component.ts`: `valid` = `tagIds.length > 0` (shared
 *     by addTags/removeTags).
 *   - `batch-copy-modal.component.ts`: `valid` = `targetPropertyId != null
 *     && targetBoardId != null && siteId != null && startDate != null`.
 *     `startDate` is constructed as `new Date()` — i.e. it's ALREADY set to
 *     today when the modal opens, so of the four `valid` fields it's the
 *     one that's satisfied from the start; the true gating order is
 *     property -> board -> worker, and submit enables the instant the
 *     worker is picked (no separate "now also touch the date" step).
 *     `onPropertyChange` re-fetches `boards`/`workers` scoped to
 *     WHICHEVER property was just picked, so `batchCopyWorkerSelect`'s
 *     option list is the TARGET property's workers, never the source's.
 *
 * Seed: SOURCE property with two workers (srcWorker1 — the only one that
 * exists when the task is created, so the task starts assigned to exactly
 * it, deterministic — and srcWorker2, for the reassign from/to-exclusion
 * check) and one task; TARGET property with its own, differently-named
 * worker (tgtWorker) — used only by the copy-modal test to prove
 * `batchCopyWorkerSelect` doesn't leak the source property's roster.
 */

const sourceProperty: PropertyCreateUpdate = {
  name: `tlv-src-${generateRandmString(5)}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const targetProperty: PropertyCreateUpdate = {
  name: `tlv-tgt-${generateRandmString(5)}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const rand = generateRandmString(6);
const srcWorker1: PropertyWorker = {
  name: `sv1-${rand}`, surname: generateRandmString(5), language: 'Dansk',
  properties: [sourceProperty.name], workerEmail: `sv1-${rand}@test.com`,
};
const srcWorker2: PropertyWorker = {
  name: `sv2-${rand}`, surname: generateRandmString(5), language: 'Dansk',
  properties: [sourceProperty.name], workerEmail: `sv2-${rand}@test.com`,
};
const tgtWorker: PropertyWorker = {
  name: `tv1-${rand}`, surname: generateRandmString(5), language: 'Dansk',
  properties: [targetProperty.name], workerEmail: `tv1-${rand}@test.com`,
};

const task = `mv-task-${rand}`;

// Danish translations of the relevant batch-action dropdown labels
// (BackendConfiguration da.ts, lines 574-579) — same source as the x/
// batch specs.
const LABEL_ASSIGN = 'Flyt valgte til medarbejder';
const LABEL_REASSIGN = 'Flyt fra medarbejder til medarbejder';
const LABEL_CHANGE_EFORM = 'Skift eForm';
const LABEL_ADD_TAGS = 'Tilføj tags';
const LABEL_COPY = 'Kopiér til ejendom';

let seeded = false;

// NOT serial: V1-V5 only exercise submit-button GATING then cancel (V3 reaches
// the eForm confirm phase but cancels there — none commits a batch action), so
// no test mutates persistent state or depends on another. Plain `describe`
// (with `workers:1` + `fullyParallel:false`, the seed test still runs first by
// declaration order) lets every test run even if one fails, so all failures
// surface in a single CI round instead of one-per-round.
test.describe('Task list — batch modal validation', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(1500);

    if (seeded) {
      const taskListPage = new TaskListPage(page);
      await taskListPage.goto();
      await taskListPage.selectProperty(sourceProperty.name);
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
  // Seed — source property + srcWorker1 + one task (assigned to srcWorker1,
  // the only worker that exists yet), then srcWorker2, then the target
  // property + tgtWorker.
  // -----------------------------------------------------------------------
  test('seed: source property/workers/task + target property/worker', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(sourceProperty);
    await propertiesPage.createProperty(targetProperty);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(srcWorker1);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();
    await calendarPage.selectProperty(sourceProperty.name);
    await page.waitForTimeout(1000);
    await calendarPage.openCreateModalAtSlot(0, 9);
    await calendarPage.fillAndSaveEvent(task);

    await workersPage.goToPropertyWorkers();
    await workersPage.create(srcWorker2);
    await workersPage.create(tgtWorker);

    seeded = true;
  });

  // =======================================================================
  // V1 — worker modal (assign): submit disabled until a worker is chosen.
  // =======================================================================
  test('V1: assign submit is disabled until a worker is chosen', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await expect(taskListPage.row(task)).toBeVisible();
    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_ASSIGN));

    await expect(page.locator('#batchModalSubmit')).toBeDisabled();
    await taskListPage.selectModalOption('batchWorkerSelect', srcWorker2.name);
    await expect(page.locator('#batchModalSubmit')).toBeEnabled();

    await taskListPage.cancelModal();
    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
  });

  // =======================================================================
  // V2 — reassign: submit disabled until BOTH from/to are chosen, and the
  // "to" dropdown's option list excludes whichever worker was picked "from".
  // =======================================================================
  test('V2: reassign requires both sides and excludes the "from" pick from "to"', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await expect(taskListPage.row(task)).toBeVisible();
    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_REASSIGN));

    await expect(page.locator('#batchModalSubmit')).toBeDisabled();
    await taskListPage.selectModalOption('batchWorkerFromSelect', srcWorker1.name);
    await expect(page.locator('#batchModalSubmit')).toBeDisabled(); // "to" still unset

    // Inspect the "to" select's own option list — srcWorker1 (just picked
    // "from") must be absent, srcWorker2 must still be offered.
    await page.locator('#batchWorkerToSelect').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    const toOptionTexts = await page.locator('.ng-dropdown-panel .ng-option').allInnerTexts();
    expect(toOptionTexts.some(t => t.includes(srcWorker1.name))).toBe(false);
    expect(toOptionTexts.some(t => t.includes(srcWorker2.name))).toBe(true);
    await page.locator('.ng-dropdown-panel .ng-option', { hasText: srcWorker2.name }).first().click();
    await page.waitForTimeout(400);

    await expect(page.locator('#batchModalSubmit')).toBeEnabled();

    await taskListPage.cancelModal();
    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
  });

  // =======================================================================
  // V3 — eForm modal: submit leads to the confirm phase; cancelling AT the
  // confirm phase closes without changing the eForm cell.
  // =======================================================================
  test('V3: eForm modal reaches the confirm phase, but cancel there leaves the cell unchanged', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    const originalEformLabel = (await taskListPage.columnCell(task, 'eform').innerText()).trim();
    expect(originalEformLabel.length).toBeGreaterThan(0);

    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_CHANGE_EFORM));
    await taskListPage.selectAnyOptionExcept('batchEformSelect', originalEformLabel);

    await taskListPage.submitModal();
    await expect(page.locator('#batchModalConfirm')).toBeVisible();
    await expect(page.locator('#batchEformConfirmText')).toBeVisible();

    // #batchModalCancel is outside the *ngIf="confirming" block — it's still
    // there in the confirm phase and just closes with no result.
    await taskListPage.cancelModal();
    await expect(page.locator('mat-dialog-container')).toHaveCount(0);

    const afterEformLabel = (await taskListPage.columnCell(task, 'eform').innerText()).trim();
    expect(afterEformLabel).toBe(originalEformLabel);
  });

  // =======================================================================
  // V4 — tags modal: submit disabled with no tags selected.
  // =======================================================================
  test('V4: add-tags submit is disabled with no tags selected', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_ADD_TAGS));

    await expect(page.locator('#batchModalSubmit')).toBeDisabled();
    await page.locator('#batchTagsSelect').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.keyboard.press('Escape');
    await expect(page.locator('#batchModalSubmit')).toBeEnabled();

    await taskListPage.cancelModal();
    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
  });

  // =======================================================================
  // V5 — copy modal: progressive gating (property -> board -> worker; date
  // already defaults to today, so worker is the last field that flips
  // `valid`), and the worker dropdown is scoped to the TARGET property.
  // =======================================================================
  test('V5: copy submit gates on property/board/worker in order and scopes workers to the target property', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await expect(taskListPage.row(task)).toBeVisible();
    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_COPY));

    await expect(page.locator('#batchModalSubmit')).toBeDisabled();

    await taskListPage.selectModalOption('batchCopyPropertySelect', targetProperty.name);
    await expect(page.locator('#batchModalSubmit')).toBeDisabled(); // board + worker still unset

    // Board/worker lists are fetched async once the target property is
    // picked (BatchCopyModalComponent.onPropertyChange) — give them time,
    // same as x/task-list-batch-copy-delete.spec.ts CD1.
    await page.waitForTimeout(1200);
    await taskListPage.selectModalOptionFirst('batchCopyBoardSelect');
    await expect(page.locator('#batchModalSubmit')).toBeDisabled(); // worker still unset

    // The worker dropdown must offer the TARGET property's worker and
    // NEITHER of the source property's workers.
    await page.locator('#batchCopyWorkerSelect').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    const workerOptionTexts = await page.locator('.ng-dropdown-panel .ng-option').allInnerTexts();
    expect(workerOptionTexts.some(t => t.includes(tgtWorker.name))).toBe(true);
    expect(workerOptionTexts.some(t => t.includes(srcWorker1.name))).toBe(false);
    expect(workerOptionTexts.some(t => t.includes(srcWorker2.name))).toBe(false);
    await page.locator('.ng-dropdown-panel .ng-option', { hasText: tgtWorker.name }).first().click();
    await page.waitForTimeout(400);

    // startDate already defaulted to today — picking the worker is the last
    // gating field, so submit enables immediately with no date step.
    await expect(page.locator('#batchModalSubmit')).toBeEnabled();

    await taskListPage.cancelModal();
    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
  });
});
