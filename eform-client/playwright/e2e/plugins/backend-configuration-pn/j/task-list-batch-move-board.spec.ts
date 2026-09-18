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
import { API_TIMEOUT, ignoreUnhandledRejections, waitForApiResponse } from '../wait-helpers';

/**
 * Task list BATCH "FLYT TIL KALENDER" suite (#1297, shard j).
 *
 * Covers the `moveToBoard` batch action (`batch-board-modal`), which POSTs
 * `task-list/move-to-board` and moves every selected task to another calendar
 * of the SAME property (the task adopts that calendar's colour; every
 * per-occurrence calendar override is cleared server-side — pinned by the
 * `TaskListBatchMoveBoardTest` integration fixture, not here).
 *
 * Seed: property + worker + two tasks created from the calendar, which land on
 * the property's auto-created "Default" calendar; THEN a second calendar is
 * created, so the tasks cannot have landed on it.
 *
 * MB1: the action sits in the Opgaver/Tasks optgroup and is property-scoped —
 *      disabled with no property filter, enabled with exactly one.
 * MB2: the modal lists both tasks, leaves out the calendar they already share,
 *      offers the other one, and keeps Save disabled until one is picked.
 * MB3: picking the new calendar and saving moves BOTH rows (Kalender column),
 *      and the move survives a full reload (written server-side).
 */

const property: PropertyCreateUpdate = {
  name: `mb-${generateRandmString(5)}`,
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
const taskOne = `mba-task-${rand}`;
const taskTwo = `mbb-task-${rand}`;
const tasks = [taskOne, taskTwo];

// GetBoards auto-creates this calendar for a property that has none.
const DEFAULT_BOARD = 'Default';
const targetBoard = `MB-target-${rand}`;

// Danish labels (BackendConfiguration da.ts: `Tasks: 'Opgaver'`,
// `'Move to calendar': 'Flyt til kalender'`). ng-select options carry no
// stable per-option id, so the dropdown is matched on its translated text.
const GROUP_TASKS = 'Opgaver';
const LABEL_MOVE_TO_BOARD = 'Flyt til kalender';

const exactly = (text: string) =>
  new RegExp(`^\\s*${text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*$`);

let seeded = false;

test.describe.serial('Task list — batch move to calendar', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(1500);
  });

  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage().catch((err: any) => {
      console.log(`afterAll cleanup failed (non-fatal): could not open a cleanup page: ${err?.message ?? err}`);
      return undefined;
    });
    if (!page) {
      return;
    }
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

  // `openCreateModalOnCurrentWeek` for the second task: `openCreateModalAtSlot`
  // advances the calendar a week each time it runs (see the j/ status suite).
  test('seed: property + worker + two tasks on Default, then a second calendar', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.selectProperty(property.name);
    await page.waitForTimeout(1000);

    await calendarPage.openCreateModalAtSlot(0, 9);
    await calendarPage.fillAndSaveEvent(taskOne);

    await calendarPage.openCreateModalOnCurrentWeek(1, 10);
    await calendarPage.fillAndSaveEvent(taskTwo);

    await calendarPage.createBoard(targetBoard);
    await calendarPage.closeBoardMenu();

    seeded = true;
  });

  // =======================================================================
  // MB1 — Opgaver group, property-scoped gating.
  // =======================================================================
  test('MB1: moveToBoard is in the Tasks group, disabled without and enabled with one property', async ({ page }) => {
    expect(seeded).toBe(true);
    const taskListPage = new TaskListPage(page);

    // No property filter: narrow by free text so a row can still be selected.
    await taskListPage.goto();
    await taskListPage.search(rand);
    await expect(taskListPage.row(taskOne)).toBeVisible();
    await taskListPage.selectRow(taskOne);

    await taskListPage.openBatchActionPanel();
    const tasksGroupLabels = await taskListPage.batchActionLabelsInGroup(GROUP_TASKS);
    expect(tasksGroupLabels).toContain(LABEL_MOVE_TO_BOARD);
    const option = taskListPage.batchActionOption(exactly(LABEL_MOVE_TO_BOARD));
    await expect(option).toHaveCount(1);
    expect((await option.getAttribute('class')) ?? '').toContain('ng-option-disabled');
    await page.keyboard.press('Escape');

    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(taskOne)).toBeVisible();
    await taskListPage.selectRow(taskOne);
    await taskListPage.openBatchActionPanel();
    expect((await taskListPage.batchActionOption(exactly(LABEL_MOVE_TO_BOARD))
      .getAttribute('class')) ?? '').not.toContain('ng-option-disabled');
    await page.keyboard.press('Escape');
  });

  // =======================================================================
  // MB2 — the modal: task summary, current calendar hidden, Save gated.
  // =======================================================================
  test('MB2: the modal hides the shared current calendar and gates Save on a pick', async ({ page }) => {
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      // Premise: both tasks sit on the property's Default calendar.
      await expect(taskListPage.columnCell(name, 'board')).toHaveText(exactly(DEFAULT_BOARD));
      await taskListPage.selectRow(name);
    }

    await taskListPage.pickBatchAction(exactly(LABEL_MOVE_TO_BOARD));
    const items = taskListPage.getModalTaskList().locator('li');
    await expect(items).toHaveCount(2);
    await expect(items.filter({ hasText: taskOne })).toHaveCount(1);
    await expect(items.filter({ hasText: taskTwo })).toHaveCount(1);
    await expect(taskListPage.batchModalSubmitButton()).toBeDisabled();

    await page.locator('#batchBoardSelect').click();
    const options = page.locator('.ng-dropdown-panel .ng-option');
    await expect(options.filter({ hasText: exactly(targetBoard) })).toHaveCount(1);
    await expect(options.filter({ hasText: exactly(DEFAULT_BOARD) })).toHaveCount(0);
    // Close the panel without Escape (that would close the dialog too).
    await page.locator('mat-dialog-container [mat-dialog-title]').click({ position: { x: 5, y: 5 } });
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'hidden', timeout: 5000 }).catch(() => {});

    await taskListPage.selectModalOption('batchBoardSelect', exactly(targetBoard));
    await expect(taskListPage.batchModalSubmitButton()).toBeEnabled();

    // Leave no mutation behind for MB3.
    await taskListPage.cancelModal();
    await expect(page.locator('mat-dialog-container')).toHaveCount(0);
    for (const name of tasks) {
      await expect(taskListPage.columnCell(name, 'board')).toHaveText(exactly(DEFAULT_BOARD));
    }
  });

  // =======================================================================
  // MB3 — the happy path, persisted.
  // =======================================================================
  test('MB3: saving moves both tasks to the picked calendar, and it persists', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      await taskListPage.selectRow(name);
    }

    await taskListPage.pickBatchAction(exactly(LABEL_MOVE_TO_BOARD));
    await taskListPage.selectModalOption('batchBoardSelect', exactly(targetBoard));

    const moved = waitForApiResponse(
      page,
      'the task-list move-to-board POST',
      (r) => r.url().includes('/api/backend-configuration-pn/task-list/move-to-board')
        && r.request().method() === 'POST',
      API_TIMEOUT,
    );
    // submitModal() is awaited first, so this bounded wait can reject before we
    // reach its `await`; the handler keeps that from failing the run early.
    ignoreUnhandledRejections(moved);
    await taskListPage.submitModal();
    const response = await moved;
    expect(response.ok()).toBe(true);
    expect((await response.json()).success).toBe(true);
    await taskListPage.waitForModalClosed();

    for (const name of tasks) {
      await expect(taskListPage.columnCell(name, 'board')).toHaveText(exactly(targetBoard), { timeout: 15000 });
    }

    // Full reload: the grid re-reads tasks/index, so this is the server's state.
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      await expect(taskListPage.columnCell(name, 'board')).toHaveText(exactly(targetBoard));
    }
  });
});
