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
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';

/**
 * #1334 — a task's tags are listed alphabetically with Danish collation
 * (a…z, then æ, ø, å) in the task list's Tags (ETIKETTER) cell and in the
 * CSV export, whatever order they were attached in.
 *
 * The API sorts (`BackendConfigurationCalendarService.Index`); the grid
 * formatter and `exportCsv` only `join(', ')`, so both surfaces are asserted.
 *
 * The five tags are bulk-created in a NON-alphabetical order (so their ids,
 * and the attach order below, disagree with the expected order) and deleted
 * again at the end — tags are global in the shared CI DB and `clearTable()`
 * never touches them.
 */

const property: PropertyCreateUpdate = {
  name: `tlda-${generateRandmString(5)}`,
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
const task = `da-task-${rand}`;

// Attach order = creation order: deliberately not alphabetical.
const attachOrder = ['Øko', 'Beta', 'Åben', 'Alfa', 'Æble'].map(p => `${p} ${rand}`);
// Danish collation: æ, ø, å after z, in that order (ordinal would put Å first).
const expectedOrder = ['Alfa', 'Beta', 'Æble', 'Øko', 'Åben'].map(p => `${p} ${rand}`);

// Danish label of the addTags batch action (BackendConfiguration da.ts).
const LABEL_ADD_TAGS = 'Tilføj tags';

test.describe.serial('Task list — tags in Danish alphabetical order', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
  });

  test.afterAll(async ({ browser }) => {
    // Non-fatal teardown, same shape as the neighbouring task-list specs.
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
      await workersPage.clearTable();

      const propertiesPage = new BackendConfigurationPropertiesPage(page);
      await propertiesPage.goToProperties();
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

  test('seed: property + worker + task + five tags', async ({ page }) => {
    // Property create, device-user create (SDK-backed, the slow part), one
    // calendar event and one bulk tag create — each bounded by its own helper.
    test.setTimeout(240000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.selectProperty(property.name);
    await calendarPage.openCreateModalAtSlot(0, 9);
    await calendarPage.fillAndSaveEvent(task);

    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.openManageTagsDialog();
    await taskListPage.bulkCreateTags(attachOrder.join('\n'));
    for (const name of attachOrder) {
      await expect(taskListPage.tagRow(name)).toHaveCount(1, { timeout: UI_TIMEOUT });
    }
    await taskListPage.closeManageTagsDialog();
  });

  test('tags attached out of order are listed alphabetically in the cell and the CSV', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(task)).toBeVisible({ timeout: UI_TIMEOUT });

    // One addTags batch, picking the tags in attachOrder. The multi-select's
    // panel stays open between picks; Escape closes it (ng-select consumes
    // the key before the dialog sees it).
    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_ADD_TAGS));
    await page.locator('#batchTagsSelect').click();
    const panel = page.locator('.ng-dropdown-panel');
    await panel.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    for (const name of attachOrder) {
      await page.locator('#batchTagsSelect input[type="text"]').fill(name);
      await panel.locator('.ng-option', { hasText: name }).click({ timeout: UI_TIMEOUT });
    }
    await page.keyboard.press('Escape');
    await expect(page.locator('#batchTagsSelect .ng-value')).toHaveCount(attachOrder.length, { timeout: UI_TIMEOUT });
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();

    // ETIKETTER cell — the grid's formatter joins the API's list with ', '.
    await expect(taskListPage.columnCell(task, 'tags'))
      .toHaveText(expectedOrder.join(', '), { timeout: API_TIMEOUT });

    // CSV export — the same list, same order.
    const lines = await taskListPage.exportCsvAndReadLines();
    const taskLine = lines.find(l => l.includes(task));
    expect(taskLine, `CSV has no line for ${task}`).toBeDefined();
    expect(taskLine).toContain(expectedOrder.join(', '));
  });

  test('cleanup: delete the five tags', async ({ page }) => {
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.openManageTagsDialog();
    for (const name of attachOrder) {
      await taskListPage.deleteTag(name);
      await expect(taskListPage.tagRow(name)).toHaveCount(0, { timeout: UI_TIMEOUT });
    }
    await taskListPage.closeManageTagsDialog();
  });
});
