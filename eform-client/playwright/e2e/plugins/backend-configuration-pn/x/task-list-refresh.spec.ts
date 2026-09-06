import { test, expect, Request } from '@playwright/test';
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
 * Task list — REFRESH BUTTON suite (#1194, shard x).
 *
 * `#taskListRefreshBtn` re-fetches the grid from the database (one
 * `POST calendar/tasks/index` with the CURRENT filters, plus a tags GET) while
 * keeping the UI state: the filters live in the filter component / the page's
 * `currentFilters` and are never touched by `loadTasks()`; the column sort
 * and page index survive because the grid sorts client-side and mtx-grid
 * re-attaches the SAME `MatSort`/`MatPaginator` when it rebuilds its data
 * source. The one thing a refresh deliberately does NOT keep is the batch
 * selection — every reload path clears it, so the batch dropdown returns to
 * disabled and `#taskListSelectionCount` disappears.
 *
 * "Sort survives" is asserted explicitly, not assumed: it holds only while
 * sorting stays client-side, and a later move to server-side sorting would
 * break it silently.
 *
 * RF1: placement — the button is a `mat-icon-button` with an `aria-label`,
 *      rendered as the FIRST element of the toolbar's right-aligned group,
 *      i.e. left of `#taskListManageTagsBtn` (bounding boxes compared) and
 *      vertically centred with it.
 * RF2: behaviour — with a property filter + an ascending Task name sort + a
 *      checked row, a task created from ANOTHER browser session does not show
 *      up until refresh; one click issues exactly one tasks/index POST that
 *      still carries the property filter, after which the chip is still
 *      selected, the header is still `aria-sort="ascending"` with rows in
 *      ascending order, the new task is in the grid, the selection is gone
 *      and the button is enabled again.
 *
 * The mid-test task is created in a SEPARATE browser context
 * (`browser.newPage()`, like the `afterAll` cleanup) rather than by
 * navigating this tab away and back: leaving the route re-instantiates the
 * page component, which would reset the very filter/sort state under test —
 * and a same-context second tab would auto-login from localStorage, so
 * `LoginPage.login()` (which waits for the login form to appear) would find
 * no form and throw once its 60 s `waitFor` timeout expires.
 *
 * Seed: one property + one worker + TWO calendar-created tasks whose titles
 * bracket the mid-test one (`aa-*` < `mm-*` < `zz-*`), all sharing one random
 * token.
 */

const property: PropertyCreateUpdate = {
  name: `tlr-${generateRandmString(5)}`,
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
const taskA = `aa-ref-${rand}`; // seeded — sorts FIRST ascending
const taskZ = `zz-ref-${rand}`; // seeded — sorts LAST ascending
const taskM = `mm-ref-${rand}`; // created mid-RF2 from another session — lands between

const TASKS_INDEX = '/api/backend-configuration-pn/calendar/tasks/index';

let seeded = false;

test.describe.serial('Task list — refresh button', () => {
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
  // Seed — property + worker + two tasks on the SAME week (the second via
  // `openCreateModalOnCurrentWeek`, because `openCreateModalAtSlot` advances
  // the calendar a week each time it runs).
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
    await calendarPage.fillAndSaveEvent(taskZ);

    await calendarPage.openCreateModalOnCurrentWeek(1, 10);
    await calendarPage.fillAndSaveEvent(taskA);

    seeded = true;
  });

  // =======================================================================
  // RF1 — placement and affordance. Layout is asserted geometrically (the
  // refresh box ends before the Manage-tags box starts, centres aligned)
  // rather than by DOM order alone, because the SCSS `margin-left: auto`
  // that right-aligns the group sits on THIS button — if it were left on
  // Manage tags, the refresh button would be pushed to the left group.
  // =======================================================================
  test('RF1: refresh button renders first in the right-aligned group, left of Manage tags', async ({ page }) => {
    expect(seeded).toBe(true);
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();

    const refresh = taskListPage.refreshButton();
    await expect(refresh).toBeVisible();
    await expect(refresh).toBeEnabled();
    await expect(refresh.locator('mat-icon')).toHaveText(/^\s*refresh\s*$/);
    // Screen-reader name present (translated — asserted non-empty, not by text).
    expect(((await refresh.getAttribute('aria-label')) ?? '').trim().length).toBeGreaterThan(0);

    const manageTags = taskListPage.manageTagsButton();
    const csvExport = page.locator('#taskListCsvExportBtn');
    const batchAction = page.locator('#taskListBatchAction');
    const [rBox, mBox, cBox, bBox] = await Promise.all([
      refresh.boundingBox(), manageTags.boundingBox(), csvExport.boundingBox(), batchAction.boundingBox(),
    ]);
    expect(rBox).not.toBeNull();
    expect(mBox).not.toBeNull();
    expect(cBox).not.toBeNull();
    expect(bBox).not.toBeNull();

    // Left of Manage tags, which is left of Export CSV — one contiguous group.
    expect(rBox!.x + rBox!.width).toBeLessThanOrEqual(mBox!.x);
    expect(mBox!.x + mBox!.width).toBeLessThanOrEqual(cBox!.x);
    // Right of the left-hand group (the batch dropdown), i.e. pushed right by
    // the auto margin — not sitting next to the selection count.
    expect(rBox!.x).toBeGreaterThan(bBox!.x + bBox!.width);
    // The 16px flex gap separates it from Manage tags: adjacent, not detached
    // at the far end of the toolbar.
    expect(mBox!.x - (rBox!.x + rBox!.width)).toBeLessThan(40);
    // Vertically centred with its 36px raised neighbours (it is 40px itself).
    const centreY = (b: { y: number; height: number }) => b.y + b.height / 2;
    expect(Math.abs(centreY(rBox!) - centreY(mBox!))).toBeLessThan(6);
  });

  // =======================================================================
  // RF2 — filters, sort and page survive; rows come from the database;
  // selection is cleared.
  // =======================================================================
  test('RF2: refresh reloads rows keeping the property filter and the title sort, clearing the selection', async ({ page, browser }) => {
    expect(seeded).toBe(true);
    test.setTimeout(300000);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(taskA)).toBeVisible();
    await expect(taskListPage.row(taskZ)).toBeVisible();
    await expect(taskListPage.row(taskM)).toHaveCount(0);

    const rowsText = async () => (await taskListPage.getGrid().locator('.mat-mdc-row').allInnerTexts());
    const indexOf = (texts: string[], name: string) => {
      const i = texts.findIndex(t => t.includes(name));
      expect(i).toBeGreaterThanOrEqual(0);
      return i;
    };

    // Ascending Task name sort.
    const titleHeader = taskListPage.columnHeader('title');
    const titleSortHeader = taskListPage.sortHeader('title');
    await titleHeader.click();
    await page.waitForTimeout(500);
    await expect(titleSortHeader).toHaveAttribute('aria-sort', 'ascending');
    let texts = await rowsText();
    expect(indexOf(texts, taskA)).toBeLessThan(indexOf(texts, taskZ));

    // A checked row, so the clearing can be observed.
    await taskListPage.selectRow(taskA);
    await expect(page.locator('#taskListSelectionCount')).toBeVisible();
    await expect(page.locator('#taskListBatchAction .ng-select-disabled')).toHaveCount(0);

    // Create the third task from ANOTHER browser session (see file header).
    const other = await browser.newPage();
    try {
      await other.goto('http://localhost:4200');
      await new LoginPage(other).login();
      const calendarPage = new CalendarUiEnhancementsPage(other);
      await calendarPage.goToCalendar();
      await calendarPage.ensureSidebarOpen();
      await calendarPage.selectProperty(property.name);
      await other.waitForTimeout(1000);
      // A FREE slot: the seed put taskZ at (0, 9) and taskA at (1, 10) on this
      // same week — clicking (1, 10) would land on taskA's block and open the
      // preview instead of the create modal.
      await calendarPage.openCreateModalAtSlot(2, 11);
      await calendarPage.fillAndSaveEvent(taskM);
    } finally {
      await other.close().catch(() => {});
    }

    // This tab has not been told: the grid still shows the pre-refresh rows.
    await expect(taskListPage.row(taskM)).toHaveCount(0);
    await expect(taskListPage.row(taskA)).toBeVisible();

    // Count tasks/index POSTs and capture the body of the one the button fires.
    const posts: Request[] = [];
    const onRequest = (req: Request) => {
      if (req.url().includes(TASKS_INDEX) && req.method() === 'POST') {
        posts.push(req);
      }
    };
    page.on('request', onRequest);
    try {
      await taskListPage.clickRefresh();
      // Grace for any stray second request to show itself before counting.
      await page.waitForTimeout(1500);
    } finally {
      page.off('request', onRequest);
    }
    expect(posts).toHaveLength(1);
    // The request carried the CURRENT filters: exactly one property id.
    const body = posts[0].postDataJSON() as { filters?: { propertyIds?: number[] } } | null;
    expect(body?.filters?.propertyIds).toHaveLength(1);

    // (b) property chip retained — `.ng-value-label` is the text-only child;
    // `.ng-value` itself includes the × clear-icon glyph.
    const chips = page.locator('#taskListPropertyFilter .ng-value-label');
    await expect(chips).toHaveCount(1);
    await expect(chips.first()).toHaveText(property.name);

    // (c) sort retained — header state AND row order, with the new row in between.
    await expect(titleSortHeader).toHaveAttribute('aria-sort', 'ascending');
    // (d) the task created elsewhere is now in the grid.
    await expect(taskListPage.row(taskM)).toBeVisible();
    texts = await rowsText();
    expect(indexOf(texts, taskA)).toBeLessThan(indexOf(texts, taskM));
    expect(indexOf(texts, taskM)).toBeLessThan(indexOf(texts, taskZ));

    // (e) selection cleared: no counter, batch dropdown back to disabled.
    await expect(page.locator('#taskListSelectionCount')).toHaveCount(0);
    await expect(page.locator('#taskListBatchAction .ng-select-disabled')).toHaveCount(1);
    await expect(taskListPage.rowCheckbox(taskA).locator('input[type="checkbox"]')).not.toBeChecked();

    // The in-flight guard released the button again.
    await expect(taskListPage.refreshButton()).toBeEnabled();
  });
});
