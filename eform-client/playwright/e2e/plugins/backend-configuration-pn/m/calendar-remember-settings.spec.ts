import { test, expect, Page, Request } from '@playwright/test';
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
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';

// #1303: the calendar remembers, per user and in the browser (localStorage),
// the last property, calendar(s), worker(s) and week. A full page reload must
// reopen exactly that, validated against what still exists: a remembered
// calendar that was deleted elsewhere falls back to the property's default
// calendar (lowest id), as on a first visit.
//
// Everything runs in ONE test: Playwright gives each test a fresh browser
// context, i.e. empty localStorage, so the remembered settings only survive
// reloads within a test.

const rand = generateRandmString(5);

// Two properties, named so the one we pick sorts LAST (Danish collation: B
// before Y; never AAA/ZZZ, `aa` sorts last in da). The first-visit fallback
// (the first property) can therefore never produce the restored property by
// accident.
const propertyA: PropertyCreateUpdate = {
  name: `Bbb Husk ${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};
const propertyB: PropertyCreateUpdate = {
  name: `Yyy Husk ${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// Displayed in the employee filter as "<first> <last>".
const workerFirst = `Wil${rand}`;
const workerLast = `Husk${rand}`;
const workerName = `${workerFirst} ${workerLast}`;
const worker: PropertyWorker = {
  name: workerFirst,
  surname: workerLast,
  language: 'Dansk',
  properties: [propertyB.name],
  workerEmail: `${generateRandmString(5)}@test.com`,
};

// A second, non-default calendar: selecting ONLY it makes the pill show its
// name, which neither a reset to the default calendar nor "all calendars"
// can produce.
const boardX = `Husk-${rand}`;

const TASKS_WEEK = '/api/backend-configuration-pn/calendar/tasks/week';

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function exactText(s: string): RegExp {
  return new RegExp(`^\\s*${escapeRegExp(s)}\\s*$`);
}

function weekStartOf(req: Request): string | undefined {
  try {
    return req.postDataJSON()?.weekStart;
  } catch {
    return undefined;
  }
}

/**
 * Full page reload of the calendar, resolving once a week-tasks request for
 * `weekStart` has gone out — i.e. the remembered week was loaded, not today's.
 * Armed before the reload so a fast restore cannot slip past it.
 */
async function reloadExpectingWeek(page: Page, weekStart: string): Promise<void> {
  const restoredWeek = page.waitForRequest(
    r => r.url().includes(TASKS_WEEK) && r.method() === 'POST' && weekStartOf(r) === weekStart,
    { timeout: API_TIMEOUT }
  );
  restoredWeek.catch(() => undefined);
  await page.reload();
  // Same guard as w/calendar-attachments.spec.ts: on a slow runner the login
  // form can flash before the auth store rehydrates.
  if (await page.locator('#loginBtn').isVisible({ timeout: 3000 }).catch(() => false)) {
    await new LoginPage(page).login();
    await new CalendarUiEnhancementsPage(page).goToCalendar();
  }
  await page.locator('app-calendar-container').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await restoredWeek;
}

test.describe.serial('Calendar: remembers property, calendar, worker and week (#1303)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);
  });

  test.afterAll(async ({ browser }) => {
    // Non-fatal teardown, same shape as m/calendar-reentry.spec.ts.
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

  test('seed: create two properties + a worker on the second', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(propertyA);
    await propertiesPage.createProperty(propertyB);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);
  });

  test('a reload reopens the last property, calendar, worker and week; a deleted calendar falls back to the default', async ({ page, browser }) => {
    test.setTimeout(400000);

    const calendar = new CalendarUiEnhancementsPage(page);
    const propertyPill = page.locator('#calendarPropertyButton .pill-label');
    const boardsPill = page.locator('#calendarBoardsButton .pill-label');

    // --- Pick property B, calendar X, worker W and a week two weeks ahead ---
    await calendar.goToCalendar();
    await calendar.selectProperty(propertyB.name as string);
    await expect(propertyPill).toHaveText(exactText(propertyB.name as string), { timeout: UI_TIMEOUT });

    // Picking a property auto-selects its default calendar (lowest id); its
    // name is what the fallback at the end must land on.
    await calendar.openBoardMenu();
    const activeBoard = calendar.boardMenuPanel()
      .locator('.board-item')
      .filter({ has: page.locator('.board-checkbox.active') });
    await expect(activeBoard).toHaveCount(1, { timeout: UI_TIMEOUT });
    const defaultBoardName = ((await activeBoard.locator('.board-name').textContent()) ?? '').trim();
    await calendar.closeBoardMenu();
    expect(defaultBoardName).not.toBe('');
    await expect(boardsPill).toHaveText(exactText(defaultBoardName), { timeout: UI_TIMEOUT });

    await calendar.createBoard(boardX);
    // Only calendar X: "Ryd" empties the selection, then tick X.
    await calendar.openBoardMenu();
    await page.locator('#calendarBoardsClear').click();
    await calendar.boardItem(boardX).locator('.board-name').click();
    await expect(calendar.boardItem(boardX).locator('.board-checkbox.active'))
      .toBeVisible({ timeout: API_TIMEOUT });
    await calendar.closeBoardMenu();
    await expect(boardsPill).toHaveText(exactText(boardX), { timeout: UI_TIMEOUT });

    await calendar.openAssigneeFilter();
    await calendar.toggleFilterEmployee(workerName);
    await calendar.closeAssigneeFilter();
    expect(await calendar.assigneeFilterLabel()).toBe(workerName);

    await calendar.navigateToNextWeek();
    const twoWeeksAhead = await calendar.captureNextWeekRequest(() => calendar.navigateToNextWeek());
    const savedWeekStart: string = twoWeeksAhead.weekStart;
    expect(savedWeekStart, 'the week-tasks request carries the displayed week').toMatch(/^\d{4}-\d{2}-\d{2}$/);
    // Sanity: it really is the calendar filter that went out with it.
    expect(twoWeeksAhead.siteIds).toHaveLength(1);

    // --- Reload: all four come back ----------------------------------------
    await reloadExpectingWeek(page, savedWeekStart);

    await expect(propertyPill).toHaveText(exactText(propertyB.name as string), { timeout: UI_TIMEOUT });
    await expect(boardsPill).toHaveText(exactText(boardX), { timeout: UI_TIMEOUT });
    await expect(page.locator('#calendarAssigneesButton .pill-label'))
      .toHaveText(exactText(workerName), { timeout: UI_TIMEOUT });
    await calendar.openBoardMenu();
    await expect(calendar.boardItem(boardX).locator('.board-checkbox.active'))
      .toBeVisible({ timeout: UI_TIMEOUT });
    await calendar.closeBoardMenu();
    await calendar.openAssigneeFilter();
    expect(await calendar.checkedFilterCount()).toBe(1);
    await expect(calendar.filterEmployeeRow(workerName)).toHaveAttribute('aria-checked', 'true', { timeout: UI_TIMEOUT });
    await calendar.closeAssigneeFilter();

    // --- Delete calendar X from ANOTHER session ---------------------------
    // Deleting it here would also drop it from this tab's selection (and so
    // from what is remembered). Deleting it elsewhere leaves it remembered,
    // which is the case the fallback exists for.
    const other = await browser.newPage();
    try {
      await other.goto('http://localhost:4200');
      await new LoginPage(other).login();
      const otherCalendar = new CalendarUiEnhancementsPage(other);
      await otherCalendar.goToCalendar();
      await otherCalendar.selectProperty(propertyB.name as string);
      await otherCalendar.runBoardAction(boardX, 'delete');
      const dialog = otherCalendar.boardDialog();
      await dialog.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      await expect(dialog.locator('#calendarBoardDeleteConfirmBtn')).toBeEnabled({ timeout: API_TIMEOUT });
      await dialog.locator('#calendarBoardDeleteConfirmBtn').click();
      await dialog.waitFor({ state: 'detached', timeout: API_TIMEOUT });
      await otherCalendar.openBoardMenu();
      await expect(otherCalendar.boardItem(boardX)).toHaveCount(0, { timeout: UI_TIMEOUT });
    } finally {
      await other.close().catch(() => {});
    }

    // --- Reload: the default calendar replaces the deleted one -------------
    await reloadExpectingWeek(page, savedWeekStart);

    await expect(propertyPill).toHaveText(exactText(propertyB.name as string), { timeout: UI_TIMEOUT });
    await expect(boardsPill).toHaveText(exactText(defaultBoardName), { timeout: UI_TIMEOUT });
    await calendar.openBoardMenu();
    await expect(calendar.boardItem(boardX)).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(calendar.boardItem(defaultBoardName).locator('.board-checkbox.active'))
      .toBeVisible({ timeout: UI_TIMEOUT });
    await calendar.closeBoardMenu();
    // The worker and the week are untouched by the calendar fallback.
    await expect(page.locator('#calendarAssigneesButton .pill-label'))
      .toHaveText(exactText(workerName), { timeout: UI_TIMEOUT });
  });
});
