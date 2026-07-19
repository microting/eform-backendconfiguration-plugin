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
 * Task list BATCH eForm/TAGS suite (backend-configuration-task-list-page
 * feature): changeEform (two-phase, requires an explicit confirm step) and
 * addTags/removeTags, wired through `BackendConfigurationTaskListService`
 * (host app source, read while writing this suite):
 *   - ChangeEform: `update.EformId = model.EformId` then `UpdateTask` —
 *     one-phase server-side, but the MODAL itself is two-phase
 *     (`#batchModalSubmit` -> shows `#batchEformConfirmText` +
 *     `#batchModalConfirm` -> actually calls the endpoint). Only
 *     changeEform's modal has `#batchModalConfirm`; every other batch modal
 *     commits directly on `#batchModalSubmit`.
 *   - AddTags: `update.TagIds = update.TagIds.Union(model.TagIds)`.
 *   - RemoveTags: `update.TagIds = update.TagIds.Except(model.TagIds)`; the
 *     modal's own tag list (`data.tags`) is pre-scoped by the PAGE to the
 *     union of tags already present on the selected rows
 *     (`task-list-page.component.ts` `openBatchModal`'s `'removeTags'`
 *     case), so RemoveTags always offers exactly what's addable-back.
 *
 * These three actions are NOT property-scoped (`batchActions` getter keeps
 * them regardless of the property filter), but the suite still filters to
 * the seeded property to isolate the seeded row from any other CI-DB data.
 *
 * Seed: one task (`fillAndSaveEvent` leaves `TagIds` empty — it never
 * touches `#calendarEventTags` — so the row starts with an empty Tags cell,
 * which is exactly what RemoveTags-after-AddTags needs to be observable).
 *
 * ET1 (changeEform): pick any eForm option OTHER than the row's current one
 *     (content-driven — never index-driven against this dynamic, seeded
 *     eForms list) -> submit -> confirm -> eForm cell updates.
 * ET2 (addTags):    pick the first available tag -> Tags cell gains it.
 * ET3 (removeTags): pick it back off (the only offered option) -> Tags
 *     cell empties again.
 */

const property: PropertyCreateUpdate = {
  name: `tlt-${generateRandmString(5)}`,
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
const task = `et-task-${rand}`;

// Danish translations of the relevant batch-action dropdown labels
// (BackendConfiguration da.ts, lines 574/576/577).
const LABEL_CHANGE_EFORM = 'Skift eForm';
const LABEL_ADD_TAGS = 'Tilføj tags';
const LABEL_REMOVE_TAGS = 'Fjern tags';

let seeded = false;
let originalEformLabel = '';
let addedTagLabel = '';

test.describe.serial('Task list — batch eForm/tags actions', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(1500);

    if (seeded) {
      const taskListPage = new TaskListPage(page);
      await taskListPage.goto();
      await taskListPage.selectProperty(property.name);
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
  // Seed — property + worker + one task.
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
  // ET1 — changeEform: two-phase modal (submit -> confirm text -> confirm).
  // =======================================================================
  test('ET1: change eForm updates the eForm cell after confirm', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    originalEformLabel = (await taskListPage.columnCell(task, 'eform').innerText()).trim();
    expect(originalEformLabel.length).toBeGreaterThan(0);

    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_CHANGE_EFORM));
    const pickedLabel = await taskListPage.selectAnyOptionExcept('batchEformSelect', originalEformLabel);

    // Phase 1: submit reveals the confirm text + #batchModalConfirm, does
    // NOT call the endpoint yet.
    await taskListPage.submitModal();
    await expect(page.locator('#batchEformConfirmText')).toBeVisible();

    // Phase 2: confirm actually changes it.
    await taskListPage.confirmModal();
    await taskListPage.waitForModalClosed();
    await page.waitForTimeout(1000);

    const newEformLabel = (await taskListPage.columnCell(task, 'eform').innerText()).trim();
    expect(newEformLabel).toBe(pickedLabel);
    expect(newEformLabel).not.toBe(originalEformLabel);
  });

  // =======================================================================
  // ET2 — addTags: the seeded task starts with an empty Tags cell.
  // =======================================================================
  test('ET2: add tags adds a tag to the selected row', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    const before = (await taskListPage.columnCell(task, 'tags').innerText()).trim();
    // mtx-grid's `MtxGridCell._getText()` substitutes its `placeholder`
    // ('--' by default) for any formatter result it considers "empty"
    // (`_utils.isEmpty(value)`, which treats '' as empty) — so a tag-less
    // task's cell renders literal "--", never "".
    expect(before).toBe('--');

    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_ADD_TAGS));
    await page.locator('#batchTagsSelect').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    addedTagLabel = ((await page.locator('.ng-dropdown-panel .ng-option').first().innerText()) ?? '').trim();
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.keyboard.press('Escape');
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();
    await page.waitForTimeout(1000);

    const after = (await taskListPage.columnCell(task, 'tags').innerText()).trim();
    expect(after).toContain(addedTagLabel);
  });

  // =======================================================================
  // ET3 — removeTags: the modal only offers tags already on the selection,
  // so the sole option IS the tag ET2 just added.
  // =======================================================================
  test('ET3: remove tags removes it again', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    expect(addedTagLabel).not.toBe('');

    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_REMOVE_TAGS));
    await page.locator('#batchTagsSelect').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await expect(page.locator('.ng-dropdown-panel .ng-option')).toHaveCount(1);
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.keyboard.press('Escape');
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();
    await page.waitForTimeout(1000);

    const after = (await taskListPage.columnCell(task, 'tags').innerText()).trim();
    // Same mtx-grid empty-cell placeholder as ET2's "before" assertion.
    expect(after).toBe('--');
  });
});
