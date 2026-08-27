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
 * Task list BATCH SET-COMPLIANCE suite (shard e).
 *
 * Covers the `setCompliance` batch action (`batch-compliance-modal`), which
 * POSTs `task-list/set-compliance` and flips `ComplianceEnabled` — whether an
 * overdue occurrence is moved into the property's "00. Overdue tasks" folder
 * by the nightly job, i.e. shown in the app — on EVERY selected task at once.
 *
 * Three properties of the action drive the whole suite:
 *   - It is NOT property-scoped. Unlike assign/reassign/addWorker/copy —
 *     whose option lists (workers, target property) are ambiguous without a
 *     single-property filter — compliance is a per-planning boolean, so
 *     `batchActions` builds it with `disabled: false` unconditionally.
 *   - It requires an EXPLICIT choice. `complianceEnabled` starts `null`, so
 *     neither radio is checked and `#batchModalSubmit` is disabled until the
 *     admin picks one. A radio pair has no "nothing selected" visual state, so
 *     a pre-checked default plus an always-enabled Save would let a "let me
 *     see what this offers" click silently write that default onto every
 *     selected task. Asserted on every fresh modal open below (BC3, BC4).
 *   - It must NOT touch Status. The single-task calendar modal's
 *     `onPickOverdueShown`/`onPickOverdueHidden` force Status active as a side
 *     effect; doing that across a large selection would silently reactivate
 *     dormant tasks, so the batch path deliberately does not. That is the
 *     headline contract of the issue and is asserted after every flip here.
 *
 * Compliance is only RENDERED for an active task: the grid cell templates
 * `<span *ngIf="row.status; else complianceNotApplicableTpl" class="badge">`,
 * so an inactive row shows `--` and no badge at all (see
 * `h/task-list-compliance-inactive.spec.ts`). Both seeded tasks stay active
 * for the whole suite — nothing here ever deactivates one — which is exactly
 * why the Ja/Nej badge is a usable probe for the stored flag.
 *
 * Selector discipline: grid cells are matched by mtx-grid column class
 * (`.mat-column-status` / `.mat-column-compliance`) and badge class
 * (`.badge.ja` / `.badge.nej`), never by the Danish display text; the modal's
 * two options are picked by id (`#batchComplianceOn`/`#batchComplianceOff`),
 * never by their (shared-with-the-calendar-modal) labels.
 *
 * Seed: one property + one worker + TWO calendar-created tasks, so every
 * flip is proved on a batch of two rather than on a degenerate single row.
 * `describe.serial` — BC4-BC6 each mutate the flag the next one reads.
 *
 * BC1: `setCompliance` sits in the Opgaver/Tasks optgroup and is NOT disabled
 *      with NO property filter applied, while the four property-scoped
 *      actions still are.
 * BC2: opening the modal lists BOTH selected tasks in `#batchModalTaskList`.
 * BC3: the modal opens with NEITHER radio checked and Save disabled; picking
 *      an option enables Save; cancelling after that pick still leaves the
 *      selection count and both compliance cells untouched.
 * BC4: picking "not shown in app" and submitting flips BOTH rows Ja -> Nej,
 *      and leaves both Aktiv cells on Ja.
 * BC5: picking "shown in app" flips BOTH rows back Nej -> Ja, Aktiv untouched.
 * BC6: the same flip survives a full page reload (so it was persisted, not
 *      just mutated in the in-memory `tasks` array) with Aktiv still Ja —
 *      the batch never writes Status server-side either.
 */

const property: PropertyCreateUpdate = {
  name: `bc-${generateRandmString(5)}`,
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

// Shared random token so BC1 can narrow the grid to exactly these two rows by
// free-text search — the only way to get a non-empty selection (which is what
// enables `#taskListBatchAction`) while leaving the property filter empty.
const rand = generateRandmString(6);
const taskOne = `bca-task-${rand}`;
const taskTwo = `bcb-task-${rand}`;
const tasks = [taskOne, taskTwo];

// Danish labels of the batch-action dropdown entries (BackendConfiguration
// da.ts: `Tasks: 'Opgaver'`, `'Set compliance': 'Sæt compliance'`) — the
// dropdown is the one place the sibling suites do match on translated text,
// since ng-select options carry no stable per-option id.
const GROUP_TASKS = 'Opgaver';
const LABEL_SET_COMPLIANCE = 'Sæt compliance';

let seeded = false;

test.describe.serial('Task list — batch set compliance', () => {
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
  // Seed — property + worker + two tasks. The second is created with
  // `openCreateModalOnCurrentWeek` because `openCreateModalAtSlot` advances
  // the calendar a week each time it runs; both tasks must land on the SAME
  // future week or the second click would target a different, unrelated week.
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
  // BC1 — the action is offered in the Opgaver group and is selectable with
  // NO property filter. Asserted alongside the still-disabled count of 4, so
  // the test would fail just as loudly if the gating logic collapsed and
  // everything became enabled for the wrong reason.
  // =======================================================================
  test('BC1: setCompliance is in the Tasks group and enabled without a property filter', async ({ page }) => {
    expect(seeded).toBe(true);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.search(rand);
    await expect(taskListPage.row(taskOne)).toBeVisible();
    await taskListPage.selectRow(taskOne);

    await taskListPage.openBatchActionPanel();

    const tasksGroupLabels = await taskListPage.batchActionLabelsInGroup(GROUP_TASKS);
    expect(tasksGroupLabels).toContain(LABEL_SET_COMPLIANCE);

    const option = taskListPage.batchActionOption(new RegExp(LABEL_SET_COMPLIANCE));
    await expect(option).toHaveCount(1);
    expect((await option.getAttribute('class')) ?? '').not.toContain('ng-option-disabled');

    // Only the four property-scoped actions are grayed out here; compliance
    // is not one of them.
    expect(await taskListPage.countDisabledBatchActions()).toBe(4);
    await page.keyboard.press('Escape');
  });

  // =======================================================================
  // BC2 — the modal's task summary enumerates the whole selection, which is
  // the user's only confirmation of what a batch is about to hit.
  // =======================================================================
  test('BC2: the modal lists every selected task', async ({ page }) => {
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      await taskListPage.selectRow(name);
    }

    await taskListPage.pickBatchAction(new RegExp(LABEL_SET_COMPLIANCE));
    const items = taskListPage.getModalTaskList().locator('li');
    await expect(items).toHaveCount(2);
    await expect(items.filter({ hasText: taskOne })).toHaveCount(1);
    await expect(items.filter({ hasText: taskTwo })).toHaveCount(1);

    // Leave no dialog (and no mutation) behind for BC3.
    await taskListPage.cancelModal();
    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
  });

  // =======================================================================
  // BC3 — the empty-on-open contract plus the cancel path.
  //
  // On open neither radio is checked and Save is disabled, so there is no
  // value a stray Save click could commit. Picking one option enables Save,
  // which is the only way the button ever becomes reachable.
  //
  // Then cancel: `hide()` closes with NO result, so `openBatchModal`'s
  // `afterClosed()` never enters its `if (result)` branch — the selection is
  // not cleared and `loadTasks()` is not re-run. Cancelling only AFTER a real
  // pick proves an in-progress, otherwise-submittable choice is discarded too,
  // not merely an untouched form.
  // =======================================================================
  test('BC3: the modal opens empty, a pick enables Save, and cancelling changes nothing', async ({ page }) => {
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
      before.push((await taskListPage.columnCell(name, 'compliance').innerText()).trim());
    }

    await taskListPage.pickBatchAction(new RegExp(LABEL_SET_COMPLIANCE));
    await expect(taskListPage.getModalTaskList()).toBeVisible();
    await expect(taskListPage.complianceRadioInput(true)).not.toBeChecked();
    await expect(taskListPage.complianceRadioInput(false)).not.toBeChecked();
    await expect(taskListPage.batchModalSubmitButton()).toBeDisabled();

    await taskListPage.pickComplianceOption(false);
    await expect(taskListPage.batchModalSubmitButton()).toBeEnabled();
    await taskListPage.cancelModal();

    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
    for (let i = 0; i < tasks.length; i++) {
      const after = (await taskListPage.columnCell(tasks[i], 'compliance').innerText()).trim();
      expect(after).toBe(before[i]);
      await expect(taskListPage.columnCell(tasks[i], 'compliance').locator('.badge.ja')).toHaveCount(1);
    }
    const countAfter = (await page.locator('#taskListSelectionCount').innerText()).trim();
    expect(countAfter).toBe(countBefore);
  });

  // =======================================================================
  // BC4 — the happy path in the risky direction: turning compliance OFF for
  // a batch of two. Aktiv is re-asserted on both rows because this is the
  // flip that would expose an accidental Status write.
  // =======================================================================
  test('BC4: setting "not shown in app" flips both rows to Nej without touching Aktiv', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      // Baseline: a freshly created task is active with compliance enabled.
      await expect(taskListPage.columnCell(name, 'status').locator('.badge.ja')).toHaveCount(1);
      await expect(taskListPage.columnCell(name, 'compliance').locator('.badge.ja')).toHaveCount(1);
      await taskListPage.selectRow(name);
    }

    await taskListPage.pickBatchAction(new RegExp(LABEL_SET_COMPLIANCE));
    // The modal opens with nothing selected (`complianceEnabled = null`) and
    // Save disabled, so the off option below is necessarily a deliberate
    // choice — it cannot be a re-save of a pre-checked default, and the flip
    // this test then observes cannot have come from anywhere else.
    await expect(taskListPage.complianceRadioInput(true)).not.toBeChecked();
    await expect(taskListPage.complianceRadioInput(false)).not.toBeChecked();
    await expect(taskListPage.batchModalSubmitButton()).toBeDisabled();

    await taskListPage.pickComplianceOption(false);
    await expect(taskListPage.complianceRadioInput(true)).not.toBeChecked();
    await expect(taskListPage.batchModalSubmitButton()).toBeEnabled();
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();

    for (const name of tasks) {
      await expect(taskListPage.columnCell(name, 'compliance').locator('.badge.nej')).toHaveCount(1);
      await expect(taskListPage.columnCell(name, 'compliance').locator('.badge.ja')).toHaveCount(0);
      await expect(taskListPage.columnCell(name, 'status').locator('.badge.ja')).toHaveCount(1);
    }
  });

  // =======================================================================
  // BC5 — and back on again, so the action is proved reversible rather than
  // one-way (a server-side `|=` style bug would pass BC4 alone).
  // =======================================================================
  test('BC5: setting "shown in app" flips both rows back to Ja', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.columnCell(name, 'compliance').locator('.badge.nej')).toHaveCount(1);
      await taskListPage.selectRow(name);
    }

    await taskListPage.pickBatchAction(new RegExp(LABEL_SET_COMPLIANCE));
    await taskListPage.pickComplianceOption(true);
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();

    for (const name of tasks) {
      await expect(taskListPage.columnCell(name, 'compliance').locator('.badge.ja')).toHaveCount(1);
      await expect(taskListPage.columnCell(name, 'compliance').locator('.badge.nej')).toHaveCount(0);
      await expect(taskListPage.columnCell(name, 'status').locator('.badge.ja')).toHaveCount(1);
    }
  });

  // =======================================================================
  // BC6 — persistence + the Status contract across a fresh load. BC4/BC5
  // read the grid that `loadTasks()` refreshed inside the same page session;
  // this one re-navigates so the assertion is made against a brand-new
  // tasks/index response, which is where a Status side effect written
  // server-side (but not reflected in the response BC4 saw) would surface.
  // =======================================================================
  test('BC6: the flip persists across a reload and Aktiv is still Ja', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      await taskListPage.selectRow(name);
    }

    await taskListPage.pickBatchAction(new RegExp(LABEL_SET_COMPLIANCE));
    await taskListPage.pickComplianceOption(false);
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      await expect(taskListPage.columnCell(name, 'compliance').locator('.badge.nej')).toHaveCount(1);
      await expect(taskListPage.columnCell(name, 'status').locator('.badge.ja')).toHaveCount(1);
    }
  });
});
