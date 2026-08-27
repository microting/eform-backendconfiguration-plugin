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
 * Task list TAG MANAGEMENT suite (`#taskListManageTagsBtn`).
 *
 * The toolbar button mounts the SHARED tag dialogs from
 * `common/modules/eform-shared-tags` — the same list/create/rename/delete/
 * bulk-create set the task wizard and the items-planning plannings page use —
 * driven by `TaskListTagsComponent`, a render-nothing controller that calls
 * `ItemsPlanningPnTagsService` and emits `tagsChanged`.
 *
 * Two behaviours are load-bearing and specific to THIS page, so they are
 * asserted rather than assumed:
 *
 *  1. **Both** reloads run after every successful change
 *     (`TaskListPageComponent.onUpdateTags()`): `loadTags()` refills the tag
 *     list/filter bar, and `loadTasks()` is the ONLY thing that refreshes the
 *     grid's **Tags** column — those values are tag NAMES resolved
 *     server-side, so a rename or delete stays stale in the grid until the
 *     tasks are re-fetched. MT4/MT5 assert the grid cell, not just the dialog.
 *
 *  2. **Bulk create** is one textarea, one name per line
 *     (`SharedTagMultipleCreateComponent`). Blank and whitespace-only lines
 *     are dropped and every name is trimmed, so the trailing newline a user
 *     inevitably leaves behind cannot post a `""` name — `PlanningTag.Name` is
 *     `[Required]` and the bulk endpoint wraps its whole create loop in ONE
 *     try/catch, which would commit the earlier names and STILL answer
 *     `success = false` (a partial write reported as a total failure).
 *     MT2 covers exactly that shape of input.
 *
 * Tag names are global (not property-scoped) and the CI DB is shared, so every
 * name here carries a per-run random suffix and the suite deletes everything it
 * created (MT5 + MT6) — `clearTable()` in `afterAll` cleans properties and
 * workers, never tags.
 *
 * MT1: `#taskListManageTagsBtn` opens the tag list dialog with BOTH create
 *      affordances (`#newTagBtn` single, `#newTagsBtn` bulk).
 * MT2: bulk-create adds SEVERAL tags from ONE textarea submission; the blank
 *      line and the trailing newline add nothing, names are trimmed, and a
 *      whitespace-only textarea leaves the Save button disabled.
 * MT3: single create adds exactly one tag.
 * MT4: rename — with the tag attached to the seeded task, renaming it updates
 *      the dialog list AND the grid's Tags cell.
 * MT5: delete — removes it from the dialog list AND from the grid's Tags cell.
 * MT6: cleanup + delete of the remaining spec-created tags.
 */

const property: PropertyCreateUpdate = {
  name: `tlmt-${generateRandmString(5)}`,
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
const task = `mt-task-${rand}`;

// Bulk-created in ONE submission (MT2), single-created in MT3.
const bulkA = `mt-bulk-a-${rand}`;
const bulkB = `mt-bulk-b-${rand}`;
const bulkC = `mt-bulk-c-${rand}`;
const single = `mt-single-${rand}`;
const renamed = `mt-renamed-${rand}`;

// Danish label of the addTags batch action (BackendConfiguration da.ts).
const LABEL_ADD_TAGS = 'Tilføj tags';

let baselineTagCount = 0;

test.describe.serial('Task list — tag management', () => {
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
  // Seed — property + worker + one calendar-created task. The task starts
  // with NO tags (`fillAndSaveEvent` never touches `#calendarEventTags`),
  // which is what makes MT4/MT5's grid-cell assertions unambiguous.
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
  });

  // =======================================================================
  // MT1 — the toolbar button opens the tag dialog with both create paths.
  // =======================================================================
  test('MT1: manage-tags button opens the tag dialog', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();

    await expect(taskListPage.manageTagsButton()).toBeVisible();
    await taskListPage.openManageTagsDialog();

    // Single create, bulk create and close are all offered.
    await expect(page.locator('#newTagBtn')).toBeVisible();
    await expect(page.locator('#newTagsBtn')).toBeVisible();
    await expect(page.locator('#tagsModalCloseBtn')).toBeVisible();

    // Baseline for MT2/MT3's exact-delta assertions. Never assert an
    // ABSOLUTE tag count — the CI DB is shared and carries other suites' tags.
    baselineTagCount = (await taskListPage.tagNames()).length;
    expect(baselineTagCount).toBeGreaterThan(0);

    await taskListPage.closeManageTagsDialog();
  });

  // =======================================================================
  // MT2 — MULTI-CREATE: several tags from one textarea submission, with a
  // blank line and a trailing newline that must contribute nothing, and a
  // padded name that must arrive trimmed.
  // =======================================================================
  test('MT2: bulk create adds several tags from one submission', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.openManageTagsDialog();

    // A whitespace-only textarea must not be submittable at all — that is the
    // guard standing between a stray newline and a `""` name server-side.
    await taskListPage.openBulkCreateTags('   \n  \n');
    await expect(taskListPage.bulkCreateSubmitButton()).toBeDisabled();

    // Three real names, one padded, plus a blank line and a trailing newline.
    await page.locator('#newTagsName').fill(`${bulkA}\n  ${bulkB}  \n\n${bulkC}\n`);
    await expect(taskListPage.bulkCreateSubmitButton()).toBeEnabled();
    await taskListPage.submitBulkCreateTags();

    const names = await taskListPage.tagNames();
    // Exactly three added — the blank line and the trailing newline added
    // nothing, and `bulkB` arrived trimmed (exact-text row match below).
    expect(names.length).toBe(baselineTagCount + 3);
    expect(names.filter(n => n === '')).toHaveLength(0);
    await expect(taskListPage.tagRow(bulkA)).toHaveCount(1);
    await expect(taskListPage.tagRow(bulkB)).toHaveCount(1);
    await expect(taskListPage.tagRow(bulkC)).toHaveCount(1);

    await taskListPage.closeManageTagsDialog();
  });

  // =======================================================================
  // MT3 — single create.
  // =======================================================================
  test('MT3: single create adds one tag', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.openManageTagsDialog();

    await taskListPage.createTag(single);

    expect((await taskListPage.tagNames()).length).toBe(baselineTagCount + 4);
    await expect(taskListPage.tagRow(single)).toHaveCount(1);

    await taskListPage.closeManageTagsDialog();
  });

  // =======================================================================
  // MT4 — RENAME refreshes the grid's Tags column.
  //
  // The tag is first attached to the seeded task via the addTags batch
  // action, so the rename has something observable to change OUTSIDE the
  // dialog. `t.tags` is a server-resolved `string[]` of NAMES — only
  // `loadTasks()` can refresh it, which is the half of `onUpdateTags()` this
  // case exists to protect.
  // =======================================================================
  test('MT4: rename updates the tag list and the grid Tags column', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(task)).toBeVisible();

    // mtx-grid renders its `placeholder` ('--') for an empty cell, never ''.
    expect((await taskListPage.columnCell(task, 'tags').innerText()).trim()).toBe('--');

    // Attach `single` to the seeded task (same flow as
    // x/task-list-batch-eform-tags.spec.ts ET2 — the ng-select is a MULTI
    // select whose panel stays open after a pick, so it is dismissed with
    // Escape, which ng-select consumes before the surrounding dialog sees it).
    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_ADD_TAGS));
    await page.locator('#batchTagsSelect').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('#batchTagsSelect input[type="text"]').fill(single);
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.keyboard.press('Escape');
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();
    await page.waitForTimeout(1000);

    expect((await taskListPage.columnCell(task, 'tags').innerText()).trim()).toContain(single);

    // Now rename it — the dialog stays open on top of the grid, and BOTH
    // must end up showing the new name.
    await taskListPage.openManageTagsDialog();
    await taskListPage.renameTag(single, renamed);

    await expect(taskListPage.tagRow(renamed)).toHaveCount(1);
    await expect(taskListPage.tagRow(single)).toHaveCount(0);

    await taskListPage.closeManageTagsDialog();
    const cell = (await taskListPage.columnCell(task, 'tags').innerText()).trim();
    expect(cell).toContain(renamed);
    expect(cell).not.toContain(single);
  });

  // =======================================================================
  // MT5 — DELETE drops the tag from the list AND from the grid's Tags cell.
  // =======================================================================
  test('MT5: delete removes the tag from the list and the grid', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(task)).toBeVisible();
    expect((await taskListPage.columnCell(task, 'tags').innerText()).trim()).toContain(renamed);

    await taskListPage.openManageTagsDialog();
    await taskListPage.deleteTag(renamed);
    await expect(taskListPage.tagRow(renamed)).toHaveCount(0);

    await taskListPage.closeManageTagsDialog();
    // Back to the empty-cell placeholder: it was the task's only tag.
    expect((await taskListPage.columnCell(task, 'tags').innerText()).trim()).toBe('--');
  });

  // =======================================================================
  // MT6 — delete the three bulk-created tags, returning the shared CI DB to
  // its pre-suite tag count (tags are global; `clearTable()` never touches
  // them). Doubles as a repeated-delete-without-reopening assertion.
  // =======================================================================
  test('MT6: the bulk-created tags can be deleted again', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.openManageTagsDialog();

    await taskListPage.deleteTag(bulkA);
    await taskListPage.deleteTag(bulkB);
    await taskListPage.deleteTag(bulkC);

    await expect(taskListPage.tagRow(bulkA)).toHaveCount(0);
    await expect(taskListPage.tagRow(bulkB)).toHaveCount(0);
    await expect(taskListPage.tagRow(bulkC)).toHaveCount(0);
    expect((await taskListPage.tagNames()).length).toBe(baselineTagCount);

    await taskListPage.closeManageTagsDialog();
  });
});
