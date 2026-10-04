import { test, expect, Page } from '@playwright/test';
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
import { customerDatabase, runMariadbSql } from '../db-helpers';
import { API_TIMEOUT, UI_TIMEOUT, ignoreUnhandledRejections, waitForApiResponse } from '../wait-helpers';

/**
 * Active tasks without a planning are not listed (#1376, shard h).
 *
 * THE BUG: the task list (`calendar/tasks/index`) listed every non-removed
 * AreaRulePlanning, so legacy rows that are "active" but have no items-planning
 * Planning (ItemPlanningId 0, or a removed/missing one) showed up as Aktiv = Ja
 * tasks with no eForm that could not be opened.
 *
 * No UI or API produces such a row any more, so TLP02 writes one straight into CI's
 * MariaDB (see `../db-helpers.ts`): a copy of the seeded task's row with
 * ItemPlanningId 0. The copy keeps the AreaRule, so it carries the SAME title — with
 * the bug the grid shows the title twice, with the fix once.
 *
 * Runs in CI only (CLAUDE.md): it needs the app container, MariaDB and the
 * `mariadbtest` container name from `.github/workflows/dotnet-core-pr.yml`.
 */

const BASE_URL = 'http://localhost:4200';
const TASKS_INDEX = '/api/backend-configuration-pn/calendar/tasks/index';
const BC_DB = customerDatabase('eform-backend-configuration-plugin');

const property: PropertyCreateUpdate = {
  name: `tlp-${generateRandmString(5)}`,
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
const task = `tlp-task-${rand}`;

// State handed from test to test (describe.serial runs them in order, in one worker).
let seeded = false;
let liveArpId = 0;
let orphanArpId = 0;

function requireId(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value <= 0) {
    throw new Error(`Refusing to build SQL: ${what} is not a positive integer (${JSON.stringify(value)})`);
  }
  return value;
}

/** `-N -B` output as rows of tab-separated columns. */
function parseRows(stdout: string): string[][] {
  return stdout
    .trim()
    .split('\n')
    .filter(line => line.length > 0)
    .map(line => line.split('\t'));
}

/**
 * Opens the task list, types the suite's random token into the search box and
 * returns the ids of the `tasks/index` response for that search. The wait is armed
 * before typing and matches the request body, so an earlier unfiltered load is
 * never mistaken for it.
 */
async function searchTaskList(page: Page, taskListPage: TaskListPage): Promise<number[]> {
  await taskListPage.goto();
  const indexResponse = waitForApiResponse(
    page,
    `POST calendar/tasks/index searching "${rand}"`,
    r => {
      if (!r.url().includes(TASKS_INDEX) || r.request().method() !== 'POST') {
        return false;
      }
      try {
        return r.request().postDataJSON()?.filters?.nameFilter === rand;
      } catch {
        return false;
      }
    },
    API_TIMEOUT
  );
  ignoreUnhandledRejections(indexResponse);
  await page.locator('#taskListSearch').fill(rand, { timeout: UI_TIMEOUT });

  const response = await indexResponse;
  expect(response.ok(), 'tasks/index must return 2xx').toBeTruthy();
  const body = await response.json();
  expect(body?.success, `tasks/index must succeed: ${JSON.stringify(body?.message ?? '')}`).toBe(true);
  return ((body?.model ?? []) as { id: number }[]).map(r => r.id);
}

test.describe.serial('Task list — active tasks without a planning are not listed (#1376)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto(BASE_URL);
    // login() waits for the app shell, so the app is loaded when it returns.
    await new LoginPage(page).login();
  });

  test.afterAll(async ({ browser }) => {
    // Non-fatal teardown, same shape as i/task-list-eform-change-overdue-compliance.spec.ts.
    const page = await browser.newPage().catch((err: any) => {
      console.log(`afterAll cleanup failed (non-fatal): could not open a cleanup page: ${err?.message ?? err}`);
      return undefined;
    });
    if (!page) {
      return;
    }
    const cleanup = async () => {
      await page.goto(BASE_URL);
      await new LoginPage(page).login();

      const workersPage = new BackendConfigurationPropertyWorkersPage(page);
      await workersPage.goToPropertyWorkers();
      await workersPage.clearTable();

      const propertiesPage = new BackendConfigurationPropertiesPage(page);
      await propertiesPage.goToProperties();
      await propertiesPage.clearTable();
    };
    let cleanupTimer: ReturnType<typeof setTimeout> | undefined;
    try {
      await Promise.race([
        cleanup(),
        new Promise(resolve => {
          cleanupTimer = setTimeout(resolve, 60000);
        }),
      ]);
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    } finally {
      clearTimeout(cleanupTimer);
    }
    try { await page.close(); } catch {}
  });

  test('seed: create property + worker + calendar event', async ({ page }) => {
    // 6 min: login (up to 2 min on a cold app), a property create, a device-user
    // create (SDK provisioning, SLOW_API_TIMEOUT alone) with its list refreshes,
    // then the calendar load and one event create — the same seed as
    // i/task-list-eform-change-overdue-compliance.spec.ts.
    test.setTimeout(360000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    // selectProperty waits for the boards + week responses itself.
    await calendarPage.selectProperty(property.name);
    await calendarPage.openCreateModalAtSlot(0, 9);
    await calendarPage.fillAndSaveEvent(task);

    seeded = true;
  });

  test('TLP01: the seeded task is listed once', async ({ page }) => {
    // 3 min: login (up to 2 min) plus the task-list load and one search (API_TIMEOUT).
    test.setTimeout(180000);
    expect(seeded, 'the seed test must have passed').toBe(true);

    const taskListPage = new TaskListPage(page);
    const ids = await searchTaskList(page, taskListPage);
    expect(ids.length, `the search must return exactly the seeded task; got ${JSON.stringify(ids)}`).toBe(1);
    liveArpId = requireId(ids[0], 'the seeded task id');
    await expect(taskListPage.row(task)).toHaveCount(1, { timeout: UI_TIMEOUT });
  });

  test('TLP02: write an active copy of the task with no planning into the database', async () => {
    // 3 min: login (up to 2 min, run by beforeEach) plus two docker exec calls of
    // API_TIMEOUT each.
    test.setTimeout(180000);
    const sourceId = requireId(liveArpId, 'TLP01\'s task id');

    // A full-row copy, so the statement does not depend on the column list; Id 0
    // makes MariaDB assign a new one. Run inside the plugin's schema: the temporary
    // table needs a selected database.
    const insertSql = [
      `CREATE TEMPORARY TABLE tlp_arp AS SELECT * FROM AreaRulePlannings WHERE Id = ${sourceId};`,
      'UPDATE tlp_arp SET Id = 0, ItemPlanningId = 0, Status = 1;',
      'INSERT INTO AreaRulePlannings SELECT * FROM tlp_arp;',
      'SELECT LAST_INSERT_ID();',
    ].join(' ');
    const inserted = parseRows(
      await runMariadbSql(insertSql, `copy AreaRulePlanning ${sourceId} without its planning`, BC_DB)
    );
    expect(inserted.length, `the insert must print the new id; got ${JSON.stringify(inserted)}`).toBe(1);
    orphanArpId = requireId(Number(inserted[0][0]), 'the inserted AreaRulePlanning id');

    // WorkflowState is compared null-safely: the copied row keeps the source row's value,
    // and a calendar-created row can carry NULL there (live, as far as every reader is
    // concerned — they all filter `<> 'removed'` with EF's null semantics).
    const verify = parseRows(await runMariadbSql(
      `SELECT ItemPlanningId, Status, IFNULL(WorkflowState, '') = 'removed' ` +
        `FROM \`${BC_DB}\`.AreaRulePlannings WHERE Id = ${orphanArpId};`,
      `read back AreaRulePlanning ${orphanArpId}`
    ));
    expect(verify, `AreaRulePlanning ${orphanArpId} must be an active, live row with no planning`)
      .toEqual([['0', '1', '0']]);
  });

  test('TLP03: the task list leaves the active row without a planning out', async ({ page }) => {
    // 3 min: login (up to 2 min) plus the task-list load and one search (API_TIMEOUT).
    test.setTimeout(180000);
    requireId(orphanArpId, 'TLP02\'s inserted row id');

    const taskListPage = new TaskListPage(page);
    const ids = await searchTaskList(page, taskListPage);
    expect(ids, 'the active row without a planning must not be listed').not.toContain(orphanArpId);
    expect(ids, 'the real task must still be listed').toContain(liveArpId);
    await expect(taskListPage.row(task), 'the title must show once, not once per row').toHaveCount(1, {
      timeout: UI_TIMEOUT,
    });
  });
});
