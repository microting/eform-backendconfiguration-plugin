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
 * Task list — batch CHANGE START DATE suite (#1122, shard i).
 *
 * The tenth batch action, and the only one whose date input deliberately has
 * NO `minDate`: re-anchoring a series to a date in the PAST is the whole
 * point (a yearly task moved back to 01-01 is supposed to produce an overdue
 * occurrence there). It is also the only batch modal whose Save button is
 * gated on a server round-trip rather than on local field state —
 * `BatchStartDateModalComponent.valid` requires `previewState === 'resolved'`,
 * because a past re-anchor retracts open occurrences and can deploy an
 * unbounded number of overdue ones, so the admin must see the magnitude
 * first.
 *
 * The preview panel exposes its state as `#batchStartDatePreview[data-state]`
 * (`idle` | `loading` | `resolved` | `failed`) precisely so this suite can
 * assert the gate by attribute instead of by translated text.
 *
 * Like `setCompliance`, this action is NOT property-scoped, so no property
 * filter is needed to reach it — `y/task-list-dropdown-gating.spec.ts` pins
 * the disabled counts at 4/0 to keep it that way.
 *
 * SD1: the modal opens from the Opgaver group, lists the selected task in
 *      `#batchModalTaskList`, and Save is disabled while the preview is idle
 *      (no date picked yet).
 * SD2: Save stays disabled while a preview is IN FLIGHT and only becomes
 *      enabled once it resolves; the four counts then render. The in-flight
 *      window is made observable by delaying the preview response with
 *      `page.route`, rather than by racing the component's 400 ms debounce.
 * SD3: cancelling after picking a past date changes nothing — the grid's
 *      Start date cell and the selection count are exactly as before.
 * SD4: applying a past date updates the grid's Start date column to that
 *      date.
 *
 * Order matters: SD3 (cancel) must run before SD4 (apply), since SD4 is the
 * one that actually mutates the seeded task. `describe.serial` guarantees it.
 *
 * Seed: one property + one worker + one calendar-created task, mirroring
 * `x/task-list-page.spec.ts` (including its 60s-guarded `afterAll` cleanup).
 */

const property: PropertyCreateUpdate = {
  name: `tsd-${generateRandmString(5)}`,
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
const task = `sd-task-${rand}`;

// Danish labels of the batch-action dropdown entries (BackendConfiguration
// da.ts: `Tasks: 'Opgaver'`, `'Change start date': 'Skift startdato'`) — the
// dropdown is the one place these suites match on translated text, since
// ng-select options carry no stable per-option id. Note the label is distinct
// from 'Skift eForm' by more than its first word, so the substring match
// below cannot cross-hit it.
const GROUP_TASKS = 'Opgaver';
const LABEL_CHANGE_START_DATE = 'Skift startdato';

// How far back the picked date is. Two months guarantees "the 1st of that
// month" is in the past no matter what day or hour CI runs at (one month
// would not: on the 1st of a month, "the 1st of last month" is still fine,
// but the arithmetic is only obviously safe with a margin).
const MONTHS_BACK = 2;

const PREVIEW_URL = '**/api/backend-configuration-pn/task-list/change-start-date/preview';

let seeded = false;

test.describe.serial('Task list — batch change start date', () => {
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
  // Seed — property + worker + one calendar-created task.
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
  // SD1 — the action sits in the Opgaver group, is selectable with NO
  // property filter, and its modal opens listing the selected task with
  // Save disabled (preview still idle).
  // =======================================================================
  test('SD1: modal opens from the Tasks group, lists the selected task, Save disabled while idle', async ({ page }) => {
    expect(seeded).toBe(true);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.search(rand);
    await expect(taskListPage.row(task)).toBeVisible();
    await taskListPage.selectRow(task);

    await taskListPage.openBatchActionPanel();
    const tasksGroupLabels = await taskListPage.batchActionLabelsInGroup(GROUP_TASKS);
    expect(tasksGroupLabels).toContain(LABEL_CHANGE_START_DATE);

    const option = taskListPage.batchActionOption(new RegExp(LABEL_CHANGE_START_DATE));
    await expect(option).toHaveCount(1);
    // Not property-scoped: enabled even though no property filter is set.
    expect((await option.getAttribute('class')) ?? '').not.toContain('ng-option-disabled');
    await page.keyboard.press('Escape');

    await taskListPage.pickBatchAction(new RegExp(LABEL_CHANGE_START_DATE));

    await expect(taskListPage.getModalTaskList()).toBeVisible();
    await expect(taskListPage.getModalTaskList().locator('li')).toHaveCount(1);
    await expect(taskListPage.getModalTaskList()).toContainText(task);

    // Nothing picked yet -> idle -> Save disabled. Also proves the modal does
    // NOT pre-seed today's date the way the copy modal does.
    expect(await taskListPage.startDatePreviewState()).toBe('idle');
    await expect(taskListPage.batchModalSubmitButton()).toBeDisabled();

    await taskListPage.cancelModal();
  });

  // =======================================================================
  // SD2 — the preview gate. The response is deliberately delayed so the
  // in-flight window is a real, assertable state instead of a race against
  // the component's debounce + a fast local API.
  // =======================================================================
  test('SD2: Save stays disabled until the preview resolves, then the counts render', async ({ page }) => {
    expect(seeded).toBe(true);
    const taskListPage = new TaskListPage(page);

    await page.route(PREVIEW_URL, async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 4000));
      // If the page tore the request down while this handler slept (the
      // component's switchMap superseding a preview, or teardown), continue()
      // rejects. That is bookkeeping, not a test signal - swallow it so it
      // cannot surface as a second, misleading error.
      await route.continue().catch(() => {});
    });

    try {
      await taskListPage.goto();
      await taskListPage.search(rand);
      await expect(taskListPage.row(task)).toBeVisible();
      await taskListPage.selectRow(task);
      await taskListPage.pickBatchAction(new RegExp(LABEL_CHANGE_START_DATE));

      await taskListPage.pickPastStartDate(MONTHS_BACK);

      // Debounce (400 ms) + the 4 s delay above leave a wide window in which
      // a date IS picked but no preview has resolved. Save must be disabled
      // for all of it.
      await expect(taskListPage.startDatePreview()).toHaveAttribute('data-state', 'loading');
      await expect(taskListPage.batchModalSubmitButton()).toBeDisabled();

      await taskListPage.waitForStartDatePreviewResolved();
      await expect(taskListPage.batchModalSubmitButton()).toBeEnabled();

      // All four counts render. Asserted by id and by "contains a number",
      // never by the translated sentence around it.
      for (const which of ['Tasks', 'Retract', 'Completed', 'Overdue'] as const) {
        await expect(taskListPage.startDatePreviewCount(which)).toBeVisible();
        await expect(taskListPage.startDatePreviewCount(which)).toContainText(/\d+/);
      }
      // One task is selected, so the task count is exactly 1.
      await expect(taskListPage.startDatePreviewCount('Tasks')).toContainText(/\b1\b/);

      await taskListPage.cancelModal();
    } finally {
      // Cleanup must never mask the real failure: once a test has timed out the
      // page is already closed and a bare unroute() throws "Target page ... has
      // been closed" on top of the actual error.
      await page.unroute(PREVIEW_URL).catch(() => {});
    }
  });

  // =======================================================================
  // SD3 — cancel is inert: `hide()` closes with no result, which
  // `openBatchModal`'s afterClosed treats as falsy, so neither the selection
  // nor the grid is touched.
  // =======================================================================
  test('SD3: cancelling after picking a past date changes nothing', async ({ page }) => {
    expect(seeded).toBe(true);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.search(rand);
    await expect(taskListPage.row(task)).toBeVisible();
    const before = (await taskListPage.columnCell(task, 'taskDate').innerText()).trim();

    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_CHANGE_START_DATE));
    await taskListPage.pickPastStartDate(MONTHS_BACK);
    await taskListPage.waitForStartDatePreviewResolved();

    await taskListPage.cancelModal();
    await taskListPage.waitForModalClosed();

    // Grid untouched...
    expect((await taskListPage.columnCell(task, 'taskDate').innerText()).trim()).toBe(before);
    // ...and the row is still selected (cancel does not clear the selection).
    await expect(page.locator('#taskListSelectionCount')).toBeVisible();
  });

  // =======================================================================
  // SD4 — the apply path. A PAST date is accepted (this modal has no
  // `minDate`) and lands in the grid's Start date column, which renders
  // `taskDate` as "dd-MM-yyyy".
  // =======================================================================
  test('SD4: applying a past start date updates the grid Start date column', async ({ page }) => {
    expect(seeded).toBe(true);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.search(rand);
    await expect(taskListPage.row(task)).toBeVisible();
    const before = (await taskListPage.columnCell(task, 'taskDate').innerText()).trim();

    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_CHANGE_START_DATE));

    const expectedDate = await taskListPage.pickPastStartDate(MONTHS_BACK);
    // Sanity: the seeded task starts in the future, so the target must differ
    // from what the grid already shows — otherwise SD4 would pass vacuously.
    expect(expectedDate).not.toBe(before);

    await taskListPage.waitForStartDatePreviewResolved();
    await expect(taskListPage.batchModalSubmitButton()).toBeEnabled();
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();

    // A successful submit closes with `true`, which clears the selection and
    // re-runs loadTasks(); the search filter survives the reload.
    await expect(taskListPage.row(task)).toBeVisible();
    await expect(taskListPage.columnCell(task, 'taskDate')).toHaveText(expectedDate);
  });
});
