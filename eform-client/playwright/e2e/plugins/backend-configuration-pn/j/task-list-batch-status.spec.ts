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
 * Task list BATCH ACTIVATE / DEACTIVATE suite (#1123, shard j).
 *
 * Covers the `setStatus` batch action (`batch-status-modal`), which POSTs
 * `task-list/set-status` and flips `AreaRulePlanning.Status` — and, downstream,
 * `Planning.Enabled`, the field the items-planning scheduler filters on — on
 * every selected task at once, by delegating each task to the ordinary
 * single-task calendar `UpdateTask` path.
 *
 * Four properties of the action drive the suite:
 *   - It is NOT property-scoped. Status is a per-planning boolean, so
 *     `batchActions` builds it with `disabled: false` unconditionally, exactly
 *     like `setCompliance` and `changeStartDate`.
 *   - It requires an EXPLICIT choice. `active` starts `null`, so neither radio
 *     is checked and `#batchModalSubmit` is disabled until the admin picks one
 *     (the `batch-compliance-modal` precedent: a radio pair has no "nothing
 *     selected" visual state, so a pre-checked default plus an enabled Save
 *     would let a curiosity click write onto every selected row).
 *   - Deactivating warns, activating does not. `#batchStatusDeactivateWarning`
 *     is `*ngIf="active === false"`, and its wording is the honest one: the
 *     OPEN occurrences are retracted from the app while completed ones and the
 *     data already collected are preserved.
 *   - A deactivate -> reactivate round trip must preserve the assignee, because
 *     deactivation deletes only the ITEMS-PLANNING PlanningSite rows while BC's
 *     own `AreaRulePlanning.PlanningSites` — what `BuildUpdateModel` reads —
 *     survive. If they did not, the reactivate would send `Sites = []` and the
 *     wizard's "Active && Sites.Count == 0" guard would silently coerce the task
 *     straight back to inactive, so ST5 is a real regression probe and not a
 *     tautology.
 *
 * Compliance is only RENDERED for an ACTIVE task (`#1129`): the grid cell
 * templates `<span *ngIf="row.status; else complianceNotApplicableTpl">`, so a
 * deactivated row shows `--` and NO badge at all. Nothing below ever asserts a
 * compliance badge on a deactivated row for that reason — the Aktiv column
 * (`.mat-column-status`) is the probe throughout.
 *
 * Selector discipline: grid cells by mtx-grid column class
 * (`.mat-column-status` / `.mat-column-assignedTo`) and badge class
 * (`.badge.ja` / `.badge.nej`), never by Danish display text; the modal's two
 * options by id (`#batchStatusActive` / `#batchStatusInactive`), never by their
 * (shared-with-the-calendar-modal) labels; dropdown options by label, never by
 * `nth()`.
 *
 * Seed: one property + one worker + TWO calendar-created tasks, so every flip is
 * proved on a real batch rather than on a degenerate single row.
 * `describe.serial` — ST3-ST6 each mutate the state the next one reads.
 *
 * ST1: `setStatus` sits in the Opgaver/Tasks optgroup and is NOT disabled with
 *      NO property filter applied, while the four property-scoped actions still
 *      are (4 disabled, unchanged by this issue).
 * ST2: opening the modal lists BOTH selected tasks in `#batchModalTaskList`.
 * ST3: the modal opens with neither radio checked, Save disabled and NO warning;
 *      picking "dimmed" enables Save and reveals the warning; picking "visible"
 *      hides it again; cancelling after a pick leaves both Aktiv cells and the
 *      selection count untouched.
 * ST4: picking "dimmed" and submitting flips BOTH rows Ja -> Nej.
 * ST5: picking "visible" flips BOTH rows back Nej -> Ja and the assignee cell is
 *      byte-identical to what it was before the deactivation.
 * ST6: the flip persists across a full page reload (so it was written
 *      server-side, not just mutated in the in-memory `tasks` array).
 */

const property: PropertyCreateUpdate = {
  name: `st-${generateRandmString(5)}`,
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

// Shared random token so ST1 can narrow the grid to exactly these two rows by
// free-text search — the only way to get a non-empty selection (which is what
// enables `#taskListBatchAction`) while leaving the property filter empty.
const rand = generateRandmString(6);
const taskOne = `sta-task-${rand}`;
const taskTwo = `stb-task-${rand}`;
const tasks = [taskOne, taskTwo];

// Danish labels of the batch-action dropdown entries (BackendConfiguration
// da.ts: `Tasks: 'Opgaver'`, `'Activate / deactivate': 'Aktivér / deaktivér'`).
// The dropdown is the one place these suites match on translated text, since
// ng-select options carry no stable per-option id.
const GROUP_TASKS = 'Opgaver';
const LABEL_SET_STATUS = 'Aktivér / deaktivér';

let seeded = false;

// Snapshotted in ST4, read in ST5. Module scope because `describe.serial` gives
// each test its own page context, so a variable inside a test body would not
// survive to the next one.
const assignedBeforeDeactivate: Record<string, string> = {};

test.describe.serial('Task list — batch activate / deactivate', () => {
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
  // Seed — property + worker + two tasks. The second uses
  // `openCreateModalOnCurrentWeek` because `openCreateModalAtSlot` advances the
  // calendar a week each time it runs; both tasks must land on the SAME future
  // week or the second click would target a different, unrelated one.
  // -----------------------------------------------------------------------
  test('seed: create property + worker + two tasks', async ({ page }) => {
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
    await calendarPage.fillAndSaveEvent(taskOne);

    await calendarPage.openCreateModalOnCurrentWeek(1, 10);
    await calendarPage.fillAndSaveEvent(taskTwo);

    seeded = true;
  });

  // =======================================================================
  // ST1 — the action is offered in the Opgaver group and is selectable with NO
  // property filter. Asserted alongside the still-disabled count of 4, so this
  // would fail just as loudly if the gating logic collapsed and everything
  // became enabled for the wrong reason.
  // =======================================================================
  test('ST1: setStatus is in the Tasks group and enabled without a property filter', async ({ page }) => {
    expect(seeded).toBe(true);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.search(rand);
    await expect(taskListPage.row(taskOne)).toBeVisible();
    await taskListPage.selectRow(taskOne);

    await taskListPage.openBatchActionPanel();

    const tasksGroupLabels = await taskListPage.batchActionLabelsInGroup(GROUP_TASKS);
    expect(tasksGroupLabels).toContain(LABEL_SET_STATUS);

    const option = taskListPage.batchActionOption(new RegExp(LABEL_SET_STATUS));
    await expect(option).toHaveCount(1);
    expect((await option.getAttribute('class')) ?? '').not.toContain('ng-option-disabled');

    // Only the four property-scoped actions are grayed out here; setStatus is
    // not one of them, and adding it must not have changed that count.
    expect(await taskListPage.countDisabledBatchActions()).toBe(4);
    await page.keyboard.press('Escape');
  });

  // =======================================================================
  // ST2 — the modal's task summary enumerates the whole selection, which is the
  // user's only confirmation of what a batch is about to hit.
  // =======================================================================
  test('ST2: the modal lists every selected task', async ({ page }) => {
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      await taskListPage.selectRow(name);
    }

    await taskListPage.pickBatchAction(new RegExp(LABEL_SET_STATUS));
    const items = taskListPage.getModalTaskList().locator('li');
    await expect(items).toHaveCount(2);
    await expect(items.filter({ hasText: taskOne })).toHaveCount(1);
    await expect(items.filter({ hasText: taskTwo })).toHaveCount(1);

    // Leave no dialog (and no mutation) behind for ST3.
    await taskListPage.cancelModal();
    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
  });

  // =======================================================================
  // ST3 — the empty-on-open contract, the conditional warning, and cancel.
  //
  // On open neither radio is checked and Save is disabled, so there is no value
  // a stray Save click could commit, and the warning is absent because nothing
  // destructive has been chosen. Picking "dimmed" enables Save AND reveals the
  // warning; picking "visible" hides it again, proving it tracks the choice
  // rather than merely appearing once.
  //
  // Then cancel: `hide()` closes with NO result, so `openBatchModal`'s
  // `afterClosed()` never enters its `if (result)` branch — the selection is not
  // cleared and `loadTasks()` is not re-run. Cancelling only AFTER a real pick
  // proves an in-progress, otherwise-submittable choice is discarded too, not
  // merely an untouched form.
  // =======================================================================
  test('ST3: the modal opens empty, warns only on deactivate, and cancelling changes nothing', async ({ page }) => {
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      await taskListPage.selectRow(name);
    }

    const countBefore = (await page.locator('#taskListSelectionCount').innerText()).trim();
    const before: string[] = [];
    for (const name of tasks) {
      before.push((await taskListPage.columnCell(name, 'status').innerText()).trim());
    }

    await taskListPage.pickBatchAction(new RegExp(LABEL_SET_STATUS));
    await expect(taskListPage.getModalTaskList()).toBeVisible();
    await expect(taskListPage.statusRadioInput(true)).not.toBeChecked();
    await expect(taskListPage.statusRadioInput(false)).not.toBeChecked();
    await expect(taskListPage.batchModalSubmitButton()).toBeDisabled();
    // *ngIf="active === false" — absent while `active` is still null.
    await expect(taskListPage.statusDeactivateWarning()).toHaveCount(0);

    await taskListPage.pickStatusOption(false);
    await expect(taskListPage.batchModalSubmitButton()).toBeEnabled();
    await expect(taskListPage.statusDeactivateWarning()).toBeVisible();

    // Switching to the non-destructive direction retracts the warning.
    await taskListPage.pickStatusOption(true);
    await expect(taskListPage.statusDeactivateWarning()).toHaveCount(0);
    await expect(taskListPage.batchModalSubmitButton()).toBeEnabled();

    await taskListPage.cancelModal();

    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
    for (let i = 0; i < tasks.length; i++) {
      const after = (await taskListPage.columnCell(tasks[i], 'status').innerText()).trim();
      expect(after).toBe(before[i]);
      await expect(taskListPage.columnCell(tasks[i], 'status').locator('.badge.ja')).toHaveCount(1);
    }
    const countAfter = (await page.locator('#taskListSelectionCount').innerText()).trim();
    expect(countAfter).toBe(countBefore);
  });

  // =======================================================================
  // ST4 — the happy path in the destructive direction: deactivating a batch of
  // two. The assignee cell is snapshotted here (module scope) so ST5 can prove
  // the round trip restored it.
  // =======================================================================
  test('ST4: picking "dimmed on calendar" deactivates both rows', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      // Baseline: a freshly created calendar task is active.
      await expect(taskListPage.columnCell(name, 'status').locator('.badge.ja')).toHaveCount(1);
      assignedBeforeDeactivate[name] =
        (await taskListPage.columnCell(name, 'assignedTo').innerText()).trim();
      await taskListPage.selectRow(name);
    }

    await taskListPage.pickBatchAction(new RegExp(LABEL_SET_STATUS));
    // Nothing pre-selected and Save disabled, so the pick below is necessarily a
    // deliberate choice — the flip this test observes cannot have come from a
    // re-save of a pre-checked default.
    await expect(taskListPage.statusRadioInput(true)).not.toBeChecked();
    await expect(taskListPage.statusRadioInput(false)).not.toBeChecked();
    await expect(taskListPage.batchModalSubmitButton()).toBeDisabled();

    await taskListPage.pickStatusOption(false);
    await expect(taskListPage.statusRadioInput(true)).not.toBeChecked();
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();

    for (const name of tasks) {
      await expect(taskListPage.columnCell(name, 'status').locator('.badge.nej')).toHaveCount(1);
      await expect(taskListPage.columnCell(name, 'status').locator('.badge.ja')).toHaveCount(0);
      // #1129: an inactive row's Compliance cell renders `--`, not a badge —
      // so assert the ABSENCE of both badges rather than expecting either one.
      await expect(taskListPage.columnCell(name, 'compliance').locator('.badge')).toHaveCount(0);
    }
  });

  // =======================================================================
  // ST5 — and back on again, so the action is proved reversible rather than
  // one-way. The assignee assertion is the round-trip regression probe: BC's
  // AreaRulePlanning.PlanningSites must have survived the deactivation, or
  // `BuildUpdateModel` would send `Sites = []` and the wizard would coerce the
  // task straight back to inactive.
  // =======================================================================
  test('ST5: picking "visible on calendar" reactivates both rows and keeps the assignee', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.columnCell(name, 'status').locator('.badge.nej')).toHaveCount(1);
      await taskListPage.selectRow(name);
    }

    await taskListPage.pickBatchAction(new RegExp(LABEL_SET_STATUS));
    await taskListPage.pickStatusOption(true);
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();

    for (const name of tasks) {
      await expect(taskListPage.columnCell(name, 'status').locator('.badge.ja')).toHaveCount(1);
      await expect(taskListPage.columnCell(name, 'status').locator('.badge.nej')).toHaveCount(0);
      const assignedAfter =
        (await taskListPage.columnCell(name, 'assignedTo').innerText()).trim();
      expect(assignedAfter).toBe(assignedBeforeDeactivate[name]);
    }
  });

  // =======================================================================
  // ST6 — persistence across a fresh load. ST4/ST5 read the grid that
  // `loadTasks()` refreshed inside the same page session; this one re-navigates
  // so the assertion is made against a brand-new tasks/index response.
  // =======================================================================
  test('ST6: the flip persists across a reload', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      await taskListPage.selectRow(name);
    }

    await taskListPage.pickBatchAction(new RegExp(LABEL_SET_STATUS));
    await taskListPage.pickStatusOption(false);
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      await expect(taskListPage.columnCell(name, 'status').locator('.badge.nej')).toHaveCount(1);
    }
  });
});
