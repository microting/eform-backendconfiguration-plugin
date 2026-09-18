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
import { API_TIMEOUT, UI_TIMEOUT, ignoreUnhandledRejections, waitForApiResponse } from '../wait-helpers';

/**
 * Task list BATCH "SKIFT RAPPORTOVERSKRIFT" suite (#1298, shard j).
 *
 * Covers the `changeReportHeadline` batch action
 * (`batch-report-headline-modal`), which POSTs
 * `task-list/change-report-headline` and sets the report headline
 * (AreaRulePlanning.ItemPlanningTagId, mirrored to
 * Planning.ReportGroupPlanningTagId + PlanningsTags — the three-way agreement,
 * inactive tasks included, is pinned by the `TaskListBatchReportHeadlineTest`
 * integration fixture, not here) of every selected task.
 *
 * Seed: property + worker + two tasks created from the calendar (each gets the
 * first existing headline as its report headline).
 *
 * RH1: the action sits in the Opgaver/Tasks optgroup and is NOT property-scoped
 *      — enabled with no property filter (headlines are global).
 * RH2: the modal lists both tasks and keeps Save disabled until a headline is
 *      picked (a headline is required); Cancel changes nothing.
 * RH3: a headline CREATED in the dialog (addTag -> items-planning tag POST) is
 *      applied to one task, and persists across a reload.
 * RH4: picking that now-existing headline for BOTH tasks re-headlines both,
 *      and it persists across a reload.
 */

const property: PropertyCreateUpdate = {
  name: `rh-${generateRandmString(5)}`,
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
const taskOne = `rha-task-${rand}`;
const taskTwo = `rhb-task-${rand}`;
const tasks = [taskOne, taskTwo];
const newHeadline = `RH-headline-${rand}`;

// Danish labels (BackendConfiguration da.ts: `Tasks: 'Opgaver'`,
// `'Change report headline': 'Skift rapportoverskrift'`). ng-select options
// carry no stable per-option id, so the dropdown is matched on its text.
const GROUP_TASKS = 'Opgaver';
const LABEL_CHANGE_HEADLINE = 'Skift rapportoverskrift';

const SELECT_ID = 'batchReportHeadlineSelect';

const exactly = (text: string) =>
  new RegExp(`^\\s*${text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*$`);

let seeded = false;

test.describe.serial('Task list — batch change report headline', () => {
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
  test('seed: property + worker + two tasks', async ({ page }) => {
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

    seeded = true;
  });

  // =======================================================================
  // RH1 — Opgaver group, never disabled.
  // =======================================================================
  test('RH1: changeReportHeadline is in the Tasks group and enabled without a property filter', async ({ page }) => {
    expect(seeded).toBe(true);
    const taskListPage = new TaskListPage(page);

    // No property filter: narrow by free text so a row can still be selected.
    await taskListPage.goto();
    await taskListPage.search(rand);
    await expect(taskListPage.row(taskOne)).toBeVisible();
    await taskListPage.selectRow(taskOne);

    await taskListPage.openBatchActionPanel();
    const tasksGroupLabels = await taskListPage.batchActionLabelsInGroup(GROUP_TASKS);
    expect(tasksGroupLabels).toContain(LABEL_CHANGE_HEADLINE);
    const option = taskListPage.batchActionOption(exactly(LABEL_CHANGE_HEADLINE));
    await expect(option).toHaveCount(1);
    expect((await option.getAttribute('class')) ?? '').not.toContain('ng-option-disabled');
    await page.keyboard.press('Escape');
  });

  // =======================================================================
  // RH2 — the modal: task summary, Save gated on a pick, cancel is a no-op.
  // =======================================================================
  test('RH2: the modal requires a headline before Save, and Cancel changes nothing', async ({ page }) => {
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.search(rand);
    const before: Record<string, string> = {};
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      before[name] = (await taskListPage.columnCell(name, 'overskrift').innerText()).trim();
      await taskListPage.selectRow(name);
    }

    await taskListPage.pickBatchAction(exactly(LABEL_CHANGE_HEADLINE));
    const items = taskListPage.getModalTaskList().locator('li');
    await expect(items).toHaveCount(2, { timeout: UI_TIMEOUT });
    await expect(items.filter({ hasText: taskOne })).toHaveCount(1);
    await expect(items.filter({ hasText: taskTwo })).toHaveCount(1);
    await expect(page.locator(`#${SELECT_ID}`)).toBeVisible();
    await expect(page.locator(`#${SELECT_ID} .ng-value-label`)).toHaveCount(0);
    await expect(taskListPage.batchModalSubmitButton()).toBeDisabled();

    await taskListPage.cancelModal();
    await expect(page.locator('mat-dialog-container')).toHaveCount(0, { timeout: UI_TIMEOUT });
    for (const name of tasks) {
      await expect(taskListPage.columnCell(name, 'overskrift')).toHaveText(exactly(before[name]));
    }
  });

  // =======================================================================
  // RH3 — a headline created in the dialog is applied, and persists.
  // =======================================================================
  test('RH3: a headline created in the dialog is applied to the selected task', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.search(rand);
    await expect(taskListPage.row(taskOne)).toBeVisible();
    await expect(taskListPage.row(taskTwo)).toBeVisible();
    const taskTwoBefore = (await taskListPage.columnCell(taskTwo, 'overskrift').innerText()).trim();
    await taskListPage.selectRow(taskOne);

    await taskListPage.pickBatchAction(exactly(LABEL_CHANGE_HEADLINE));
    await page.locator(`#${SELECT_ID}`).click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    const created = waitForApiResponse(
      page,
      'the items-planning tag create POST',
      (r) => /\/api\/items-planning-pn\/tags$/.test(r.url().split('?')[0])
        && r.request().method() === 'POST',
      API_TIMEOUT,
    );
    ignoreUnhandledRejections(created);
    // pressSequentially, not fill(): ng-select's addTag/typeahead path listens
    // to the keyboard events fill() does not dispatch.
    await page.locator(`#${SELECT_ID} input`).pressSequentially(newHeadline, { delay: 30 });
    const addTagOption = page.locator('.ng-dropdown-panel .ng-option:has(.ng-tag-label)');
    await addTagOption.first().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await addTagOption.first().click();
    const createResponse = await created;
    expect(createResponse.ok()).toBe(true);
    expect((await createResponse.json()).success).toBe(true);

    await expect(page.locator(`#${SELECT_ID} .ng-value-label`)).toHaveText(exactly(newHeadline), { timeout: UI_TIMEOUT });
    await expect(taskListPage.batchModalSubmitButton()).toBeEnabled();

    const changed = waitForApiResponse(
      page,
      'the task-list change-report-headline POST',
      (r) => r.url().includes('/api/backend-configuration-pn/task-list/change-report-headline')
        && r.request().method() === 'POST',
      API_TIMEOUT,
    );
    // submitModal() is awaited first, so this bounded wait can reject before we
    // reach its `await`; the handler keeps that from failing the run early.
    ignoreUnhandledRejections(changed);
    await taskListPage.submitModal();
    const response = await changed;
    expect(response.ok()).toBe(true);
    expect((await response.json()).success).toBe(true);
    await taskListPage.waitForModalClosed();

    await expect(taskListPage.columnCell(taskOne, 'overskrift')).toHaveText(exactly(newHeadline), { timeout: UI_TIMEOUT });
    // Not selected -> untouched.
    await expect(taskListPage.columnCell(taskTwo, 'overskrift')).toHaveText(exactly(taskTwoBefore));

    // Full reload: the grid re-reads tasks/index and the tag list.
    await taskListPage.goto();
    await taskListPage.search(rand);
    await expect(taskListPage.row(taskOne)).toBeVisible();
    await expect(taskListPage.columnCell(taskOne, 'overskrift')).toHaveText(exactly(newHeadline));
    await expect(taskListPage.columnCell(taskTwo, 'overskrift')).toHaveText(exactly(taskTwoBefore));
  });

  // =======================================================================
  // RH4 — picking an existing headline re-headlines every selected task.
  // =======================================================================
  test('RH4: picking an existing headline re-headlines both tasks, and it persists', async ({ page }) => {
    test.setTimeout(180000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.search(rand);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      await taskListPage.selectRow(name);
    }

    await taskListPage.pickBatchAction(exactly(LABEL_CHANGE_HEADLINE));
    await taskListPage.selectModalOption(SELECT_ID, exactly(newHeadline));
    await expect(taskListPage.batchModalSubmitButton()).toBeEnabled();

    const changed = waitForApiResponse(
      page,
      'the task-list change-report-headline POST',
      (r) => r.url().includes('/api/backend-configuration-pn/task-list/change-report-headline')
        && r.request().method() === 'POST',
      API_TIMEOUT,
    );
    ignoreUnhandledRejections(changed);
    await taskListPage.submitModal();
    const response = await changed;
    expect(response.ok()).toBe(true);
    expect((await response.json()).success).toBe(true);
    await taskListPage.waitForModalClosed();

    for (const name of tasks) {
      await expect(taskListPage.columnCell(name, 'overskrift')).toHaveText(exactly(newHeadline), { timeout: UI_TIMEOUT });
    }

    await taskListPage.goto();
    await taskListPage.search(rand);
    for (const name of tasks) {
      await expect(taskListPage.row(name)).toBeVisible();
      await expect(taskListPage.columnCell(name, 'overskrift')).toHaveText(exactly(newHeadline));
    }
  });
});
