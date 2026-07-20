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
 * Task list PAGE suite (backend-configuration-task-list-page feature).
 *
 * `/plugins/backend-configuration-pn/task-list` is an admin-only
 * (`IsAdminGuard`), read-and-batch-action page: an mtx-grid (`#taskListGrid`)
 * over every AreaRulePlanning task across all properties, with property/
 * eForm/board/worker/status/compliance/tags filters + a free-text search
 * (`#taskListSearch`), a batch-action dropdown (`#taskListBatchAction`,
 * gated wider when exactly one property is filtered — not exercised here,
 * see `x/task-list-batch-*.spec.ts`), a CSV export producing `opgaveliste.csv`
 * and a custom `#taskListShowAllToggle` that CSS-hides mtx-grid's own
 * paginator (there are TWO "Vis alle" buttons on the page — the
 * built-in paginator's own plus ours; tests must always target
 * `#taskListShowAllToggle` by id).
 *
 * Seed: one property ("full") with a worker + TWO calendar-created tasks
 * (titles chosen `zz-*`/`aa-*` so the alphabetic sort order is predictable
 * and differs from insertion order), plus a second, task-less property
 * ("empty") used only to prove the property filter actually narrows the
 * grid (an absolute row-count assertion isn't reliable against a shared CI
 * DB that may carry other properties' tasks).
 *
 * PP1: menu navigation (E2EId `backend-configuration-pn-task-list`) + direct
 *      URL both reach the grid.
 * PP2: seeded task renders as a grid row.
 * PP3: property filter narrows — "empty" property shows 0 rows, "full"
 *      shows the seeded rows.
 * PP4: free-text search narrows — matching substring keeps the row, a
 *      nonsense string empties the grid.
 * PP5: sorting by the Task name column reorders rows (alphabetic asc/desc).
 * PP6: pagination controls (`mat-paginator`) render by default.
 * PP7: `#taskListShowAllToggle` hides/reshows the paginator (CSS
 *      `display:none` via mtx-grid's `.mat-paginator-hidden`, never
 *      unmounted from the DOM).
 * PP8: the CSV export button triggers a `download` event named
 *      `opgaveliste.csv`.
 */

const property: PropertyCreateUpdate = {
  name: `tlp-full-${generateRandmString(5)}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const emptyProperty: PropertyCreateUpdate = {
  name: `tlp-empty-${generateRandmString(5)}`,
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
const taskZ = `zz-task-${rand}`; // created first (seed test) — sorts LAST ascending
const taskA = `aa-task-${rand}`; // created second (PP5) — sorts FIRST ascending

let seeded = false;

test.describe.serial('Task list page', () => {
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
  // Seed — two properties (one task-bearing, one empty), one worker, and
  // the FIRST seeded task (taskZ). Runs first via describe.serial.
  // -----------------------------------------------------------------------
  test('seed: create properties + worker + task', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await propertiesPage.createProperty(emptyProperty);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();
    await calendarPage.selectProperty(property.name);
    await page.waitForTimeout(1000);

    await calendarPage.openCreateModalAtSlot(0, 9);
    await calendarPage.fillAndSaveEvent(taskZ);

    seeded = true;
  });

  // =======================================================================
  // PP1 — reachable via the menu (E2EId) and via a direct URL.
  // =======================================================================
  test('PP1: page loads for admin via menu and via direct URL', async ({ page }) => {
    const taskListPage = new TaskListPage(page);

    await taskListPage.goToViaMenu();
    await expect(taskListPage.getGrid()).toBeVisible();
    expect(page.url()).toContain('/plugins/backend-configuration-pn/task-list');

    await taskListPage.goto();
    await expect(taskListPage.getGrid()).toBeVisible();
  });

  // =======================================================================
  // PP2 — the seeded task renders as a grid row once its property is
  // filtered in.
  // =======================================================================
  test('PP2: table shows the seeded task', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(taskZ)).toBeVisible();
  });

  // =======================================================================
  // PP3 — property filter narrows: the task-less property shows 0 rows,
  // the seeded property shows the task.
  // =======================================================================
  test('PP3: property filter narrows the grid', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();

    await taskListPage.selectProperty(emptyProperty.name);
    expect(await taskListPage.rowCount()).toBe(0);
    await expect(taskListPage.row(taskZ)).toHaveCount(0);

    // Reload (resets filters) and select the task-bearing property instead —
    // simpler and less fragile than toggling the multi-select option back off.
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(taskZ)).toBeVisible();
  });

  // =======================================================================
  // PP4 — free-text search narrows.
  // =======================================================================
  test('PP4: search narrows the grid', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(taskZ)).toBeVisible();

    await taskListPage.search(taskZ);
    await expect(taskListPage.row(taskZ)).toBeVisible();

    await taskListPage.search(`no-such-task-${generateRandmString(10)}`);
    await expect(taskListPage.row(taskZ)).toHaveCount(0);
  });

  // =======================================================================
  // PP5 — sort by Task name (creates the second task here, so the two
  // sort tests below aren't order-dependent on PP1-4 having run).
  // =======================================================================
  test('PP5: sort by task name reorders rows', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();
    await calendarPage.selectProperty(property.name);
    await page.waitForTimeout(1000);
    await calendarPage.openCreateModalAtSlot(1, 10);
    await calendarPage.fillAndSaveEvent(taskA);

    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(taskZ)).toBeVisible();
    await expect(taskListPage.row(taskA)).toBeVisible();

    const rowsText = async () => (await taskListPage.getGrid().locator('.mat-mdc-row').allInnerTexts());

    // First click sorts ascending — 'aa-task-*' before 'zz-task-*'.
    await taskListPage.columnHeader('title').click();
    await page.waitForTimeout(500);
    let texts = await rowsText();
    let indexA = texts.findIndex(t => t.includes(taskA));
    let indexZ = texts.findIndex(t => t.includes(taskZ));
    expect(indexA).toBeGreaterThanOrEqual(0);
    expect(indexZ).toBeGreaterThanOrEqual(0);
    expect(indexA).toBeLessThan(indexZ);

    // Second click sorts descending — order flips.
    await taskListPage.columnHeader('title').click();
    await page.waitForTimeout(500);
    texts = await rowsText();
    indexA = texts.findIndex(t => t.includes(taskA));
    indexZ = texts.findIndex(t => t.includes(taskZ));
    expect(indexZ).toBeLessThan(indexA);
  });

  // =======================================================================
  // PP6/PP7 — pagination controls render by default and
  // `#taskListShowAllToggle` hides/reshows them. NOTE: mtx-grid's template
  // keeps `mat-paginator` in the DOM permanently and merely toggles
  // `.mat-paginator-hidden` (`display: none`) when `[showPaginator]` is
  // false (`mtxGrid.mjs`: `[class.mat-paginator-hidden]="!showPaginator"`)
  // — so the hidden state must be asserted with `not.toBeVisible()`, NOT
  // `toHaveCount(0)` (CI round 2 proved count stays 1).
  // =======================================================================
  test('PP6/PP7: paginator renders by default; show-all toggle hides it', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.getPaginator()).toBeVisible();

    await taskListPage.toggleShowAll();
    await expect(taskListPage.getPaginator()).not.toBeVisible();

    await taskListPage.toggleShowAll();
    await expect(taskListPage.getPaginator()).toBeVisible();
  });

  // =======================================================================
  // PP8 — CSV export triggers a download named opgaveliste.csv.
  // =======================================================================
  test('PP8: CSV export downloads opgaveliste.csv', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    const filename = await taskListPage.exportCsvAndGetFilename();
    expect(filename).toBe('opgaveliste.csv');
  });

  // =======================================================================
  // PP9 — the batch-action dropdown ALWAYS shows all 8 options across 3
  // groups (mockup opgaveliste.html #opgavelisteFilterHandling): the 4
  // property-scoped actions (assign/reassign/addWorker/copy) are DISABLED
  // (`.ng-option-disabled`) — never removed — until exactly one property is
  // filtered, at which point they become selectable. Asserted purely by
  // element/class, never by translated label text (i18n-safe).
  // =======================================================================
  test('PP9: batch action dropdown shows all 8 grouped options, property-scoped ones disabled until a single property is filtered', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    const countDisabled = async (): Promise<number> => {
      const options = taskListPage.batchActionOptions();
      const total = await options.count();
      let disabled = 0;
      for (let i = 0; i < total; i++) {
        const cls = (await options.nth(i).getAttribute('class')) ?? '';
        if (cls.includes('ng-option-disabled')) {
          disabled++;
        }
      }
      return disabled;
    };

    await taskListPage.goto();
    // Narrow to the seeded task WITHOUT a property filter, so the dropdown
    // has a non-empty selection (enabling it) while singleSelectedPropertyId
    // stays null.
    await taskListPage.search(taskZ);
    await expect(taskListPage.row(taskZ)).toBeVisible();
    await taskListPage.selectRow(taskZ);

    await taskListPage.openBatchActionPanel();
    await expect(taskListPage.batchActionOptions()).toHaveCount(8);
    await expect(taskListPage.batchActionGroups()).toHaveCount(3);
    expect(await countDisabled()).toBe(4);

    // Clicking a disabled option is a no-op: no batch modal opens.
    // `.ng-option-disabled` is a class on the option div itself (not a
    // descendant), so find the index via getAttribute rather than a
    // descendant-combinator locator.
    const options = taskListPage.batchActionOptions();
    const total = await options.count();
    for (let i = 0; i < total; i++) {
      const cls = (await options.nth(i).getAttribute('class')) ?? '';
      if (cls.includes('ng-option-disabled')) {
        await options.nth(i).click();
        break;
      }
    }
    await page.waitForTimeout(400);
    await expect(taskListPage.getModalTaskList()).toHaveCount(0);
    await page.keyboard.press('Escape');

    // Now filter to exactly one property: all 8 options become enabled.
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(taskZ)).toBeVisible();
    await taskListPage.selectRow(taskZ);
    await taskListPage.openBatchActionPanel();
    await expect(taskListPage.batchActionOptions()).toHaveCount(8);
    expect(await countDisabled()).toBe(0);
    await page.keyboard.press('Escape');
  });
});
