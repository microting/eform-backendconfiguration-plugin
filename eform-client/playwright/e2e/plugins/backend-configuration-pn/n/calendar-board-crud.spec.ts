import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';
import { API_TIMEOUT, UI_TIMEOUT, waitForApiResponse } from '../wait-helpers';

/**
 * E2E coverage for #1210 — calendar create / edit / duplicate / delete driven
 * from the calendars dropdown's per-row `⋮` menu.
 *
 * Lives in the `n/` shard next to `calendar-default-board.spec.ts`, which
 * exercises the same dropdown: the Playwright matrix shards by DIRECTORY, so
 * keeping calendar-dropdown work together keeps one shard's fixed ~6 min setup
 * cost paying for both.
 *
 * Two server behaviours shape what can be asserted here, and neither is a bug
 * this ticket fixes:
 *
 *   - `GetBoards` AUTO-CREATES a "Default" calendar for a property that has
 *     none, so the property always carries one calendar the spec did not make,
 *     and "zero calendars" is not an assertable state.
 *   - `GetBoardEventCount` counts distinct `AreaRulePlanningId`s over the
 *     board's `CalendarConfigurations`, so a calendar with one created event
 *     answers exactly 1 and a freshly duplicated one answers exactly 0. Both
 *     are asserted off the API response rather than off the Danish sentence
 *     the dialog renders, which would make "1" ambiguous against a random
 *     calendar name.
 */

const property: PropertyCreateUpdate = {
  name: 'cal-crud-' + generateRandmString(5),
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

// The calendar the suite works on. `renamedName` is what Rediger turns it into;
// every later test refers to the renamed one.
const originalName = 'CRUD-' + generateRandmString(5);
const renamedName = 'REN-' + generateRandmString(5);
// In the Danish e2e locale the copy suffix is "(kopi)" — key '{{name}} (copy)'.
const copyName = () => `${renamedName} (kopi)`;

let seeded = false;

/** The event-count call the delete dialog fires on open. */
async function readEventCount(page: import('@playwright/test').Page, open: () => Promise<void>) {
  const response = waitForApiResponse(
    page,
    'the delete dialog calendar event count',
    r => r.url().includes('/api/backend-configuration-pn/calendar/boards/')
      && r.url().includes('/event-count'),
    API_TIMEOUT,
  );
  response.catch(() => undefined);
  await open();
  const body = await (await response).json();
  return body.model as number;
}

test.describe.serial('Calendar CRUD from the calendars dropdown (#1210)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();

    if (seeded) {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await calendarPage.goToCalendar();
      await calendarPage.selectProperty(property.name as string);
    }
  });

  test('seed: property, worker and one calendar', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.selectProperty(property.name as string);

    await calendarPage.createBoard(originalName);
    await calendarPage.closeBoardMenu();

    seeded = true;
  });

  test('the row menu offers Rediger, Dupliker and Slet', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);

    await calendarPage.openBoardActions(originalName);

    const panel = calendarPage.boardActionsPanel();
    await expect(panel.locator('.board-action-edit')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(panel.locator('.board-action-duplicate')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(panel.locator('.board-action-delete')).toBeVisible({ timeout: UI_TIMEOUT });
    // The inline rename input that used to sit here is gone — every action now
    // opens a dialog instead.
    await expect(panel.locator('input')).toHaveCount(0, { timeout: UI_TIMEOUT });

    await page.keyboard.press('Escape');
    await panel.waitFor({ state: 'detached', timeout: UI_TIMEOUT });
    await calendarPage.closeBoardMenu();
  });

  test('Rediger opens a pre-filled dialog and PUTs the new name', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);

    await calendarPage.runBoardAction(originalName, 'edit');

    const dialog = calendarPage.boardDialog();
    await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    // Pre-filled from the row, not blank: this is the same component the
    // create action opens, in its edit mode.
    await expect(dialog.locator('#calendarBoardName')).toHaveValue(originalName, { timeout: UI_TIMEOUT });

    await dialog.locator('#calendarBoardName').fill(renamedName);
    await dialog.locator('#calendarBoardSaveBtn').click();
    await dialog.waitFor({ state: 'detached', timeout: API_TIMEOUT });

    await calendarPage.openBoardMenu();
    await expect(calendarPage.boardItem(renamedName)).toBeVisible({ timeout: API_TIMEOUT });
    await expect(calendarPage.boardItem(originalName)).toHaveCount(0, { timeout: UI_TIMEOUT });
    await calendarPage.closeBoardMenu();
  });

  // The guard is client-side only: the server accepts a duplicate name, so the
  // dialog refusing to submit is the whole of the protection.
  test('a name another calendar already has is blocked, case-insensitively', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);

    await calendarPage.openBoardMenu();
    await calendarPage.boardMenuPanel().locator('#calendarCreateBoardBtn').click();

    const dialog = calendarPage.boardDialog();
    await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    await dialog.locator('#calendarBoardName').fill(renamedName);
    await expect(dialog.locator('#calendarBoardNameDuplicateError')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(dialog.locator('#calendarBoardSaveBtn')).toBeDisabled({ timeout: UI_TIMEOUT });

    // Same name, different case — the comparison is case-insensitive.
    await dialog.locator('#calendarBoardName').fill(renamedName.toLowerCase());
    await expect(dialog.locator('#calendarBoardNameDuplicateError')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(dialog.locator('#calendarBoardSaveBtn')).toBeDisabled({ timeout: UI_TIMEOUT });

    // ...and clears as soon as the name is free again.
    await dialog.locator('#calendarBoardName').fill('FREE-' + generateRandmString(5));
    await expect(dialog.locator('#calendarBoardNameDuplicateError')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(dialog.locator('#calendarBoardSaveBtn')).toBeEnabled({ timeout: UI_TIMEOUT });

    await dialog.locator('#calendarBoardCancelBtn').click();
    await dialog.waitFor({ state: 'detached', timeout: UI_TIMEOUT });
    await calendarPage.closeBoardMenu();
  });

  test('an event created on the calendar is counted by the delete dialog', async ({ page }) => {
    test.setTimeout(600000);
    const calendarPage = new CalendarUiEnhancementsPage(page);

    // Narrow the filter to this calendar only, so the create modal defaults to
    // it and the event cannot land on the property's auto-created Default.
    await calendarPage.openBoardMenu();
    await calendarPage.boardMenuPanel().locator('#calendarBoardsClear').click();
    await calendarPage.boardItem(renamedName).locator('.board-name').click();
    await expect(calendarPage.boardItem(renamedName).locator('.board-checkbox.active'))
      .toBeVisible({ timeout: API_TIMEOUT });
    await calendarPage.closeBoardMenu();

    // Next week's Tuesday 09:00 — a future slot, which the create flow requires.
    await calendarPage.openCreateModalAtSlot(1, 9);
    await calendarPage.fillAndSaveEvent('EV-' + generateRandmString(5));

    const count = await readEventCount(page, () => calendarPage.runBoardAction(renamedName, 'delete'));
    expect(count).toBe(1);

    const dialog = calendarPage.boardDialog();
    await expect(dialog.locator('#calendarBoardDeleteCount')).toBeVisible({ timeout: UI_TIMEOUT });
    // Not the last calendar — the property still has its auto-created Default —
    // so the "a new default is created in its place" note stays hidden.
    await expect(dialog.locator('#calendarBoardDeleteLastHint')).toHaveCount(0, { timeout: UI_TIMEOUT });

    await dialog.locator('#calendarBoardDeleteCancelBtn').click();
    await dialog.waitFor({ state: 'detached', timeout: UI_TIMEOUT });
  });

  // Duplicate has no endpoint: the client POSTs a second calendar with the
  // source's colour and a "(kopi)" name, copying NO events. The zero count
  // below is the assertion that keeps it that way.
  test('Dupliker copies the calendar under a "(kopi)" name and copies no events', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);

    await calendarPage.runBoardAction(renamedName, 'duplicate');

    await calendarPage.openBoardMenu();
    await expect(calendarPage.boardItem(copyName())).toBeVisible({ timeout: API_TIMEOUT });
    // The source is untouched.
    await expect(calendarPage.boardItem(renamedName)).toBeVisible({ timeout: UI_TIMEOUT });
    await calendarPage.closeBoardMenu();

    const count = await readEventCount(page, () => calendarPage.runBoardAction(copyName(), 'delete'));
    expect(count).toBe(0);

    const dialog = calendarPage.boardDialog();
    await dialog.locator('#calendarBoardDeleteCancelBtn').click();
    await dialog.waitFor({ state: 'detached', timeout: UI_TIMEOUT });
  });

  test('Slet removes the calendar from the dropdown', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);

    await calendarPage.runBoardAction(copyName(), 'delete');

    const dialog = calendarPage.boardDialog();
    await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await expect(dialog.locator('#calendarBoardDeleteName')).toHaveText(copyName(), { timeout: UI_TIMEOUT });
    // Enabled only once the count has landed: the dialog will not let a
    // destructive action be confirmed before the number that informs it.
    await expect(dialog.locator('#calendarBoardDeleteConfirmBtn')).toBeEnabled({ timeout: API_TIMEOUT });

    await dialog.locator('#calendarBoardDeleteConfirmBtn').click();
    await dialog.waitFor({ state: 'detached', timeout: API_TIMEOUT });

    await calendarPage.openBoardMenu();
    await expect(calendarPage.boardItem(copyName())).toHaveCount(0, { timeout: UI_TIMEOUT });
    // The calendar it was copied from is still there.
    await expect(calendarPage.boardItem(renamedName)).toBeVisible({ timeout: UI_TIMEOUT });
    await calendarPage.closeBoardMenu();
  });

  test.afterAll(async ({ browser }) => {
    // browser.newPage() can itself reject — a browser that crashed or got
    // disconnected during a long run — and an exception thrown here escapes the
    // hook and fails the job, which is exactly what this non-fatal teardown
    // exists to prevent. Record it and give up on cleanup instead.
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
});
