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
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';

// #1292: the calendar filters live in a module-level ngrx store that outlives
// the calendar component. Leaving Kalender and coming back inside the SPA (no
// page reload) used to find the property already selected, so loadProperties()
// skipped every property-scoped load: the property pill named the property,
// but the calendars list was empty (pill read "Alle kalendere"), the employee
// list was empty and the week was blank.
//
// The navigation back is `page.goBack()`: the browser's back button pops the
// history entry and Angular's router handles the popstate in-app, which is
// exactly the "no reload" path. A `page.goto()` would reload the bundle and
// reset the store, hiding the bug. A window marker set before leaving proves
// no reload happened.

const property: PropertyCreateUpdate = {
  name: 'Reentry ' + generateRandmString(5),
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

// A second, non-default calendar: selecting ONLY it makes the pill show its
// name, which neither the broken state ("Alle kalendere") nor a silent reset
// to the default calendar can produce.
const boardName = 'Reentry-' + generateRandmString(5);
const eventTitle = 'Reentry event ' + generateRandmString(5);

test.describe.serial('Calendar: re-entering without a page reload (#1292)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);
  });

  test.afterAll(async ({ browser }) => {
    // Non-fatal teardown, same shape as r/calendar-ui-enhancements.spec.ts.
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

  test('seed: create property + worker', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);
  });

  test('coming back to Kalender keeps its property, calendar, workers and tasks', async ({ page }) => {
    test.setTimeout(300000);

    const calendar = new CalendarUiEnhancementsPage(page);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);

    // --- Set up a non-default selection with a task on screen --------------
    await calendar.goToCalendar();
    await calendar.selectProperty(property.name as string);

    await calendar.createBoard(boardName);
    // Only the new calendar: "Ryd" empties the selection, then tick ours.
    await calendar.openBoardMenu();
    await page.locator('#calendarBoardsClear').click();
    await calendar.boardItem(boardName).locator('.board-name').click();
    await expect(calendar.boardItem(boardName).locator('.board-checkbox.active'))
      .toBeVisible({ timeout: API_TIMEOUT });
    await calendar.closeBoardMenu();

    // Next week, so the slot is in the future. The store keeps the week too,
    // so the event is expected on screen again after the round trip.
    await calendar.openCreateModalAtSlot(0, 9);
    await calendar.fillAndSaveEvent(eventTitle);
    await expect(calendar.findEventBlock(eventTitle)).toBeVisible({ timeout: UI_TIMEOUT });

    const boardsPill = page.locator('#calendarBoardsButton .pill-label');
    const propertyPill = page.locator('#calendarPropertyButton .pill-label');
    await expect(boardsPill).toHaveText(new RegExp(`^\\s*${boardName}\\s*$`), { timeout: UI_TIMEOUT });
    await expect(propertyPill).toHaveText(new RegExp(`^\\s*${property.name}\\s*$`), { timeout: UI_TIMEOUT });

    // --- Leave within the SPA ---------------------------------------------
    await page.evaluate(() => { (window as any).__calendarReentryNoReload = true; });
    await propertiesPage.goToProperties();
    await expect(page.locator('app-calendar-container')).toHaveCount(0, { timeout: UI_TIMEOUT });

    // --- Come back within the SPA -----------------------------------------
    // Registered before the navigation: the re-entry loads fire as soon as
    // the new component initialises.
    const boardsLoaded = page
      .waitForResponse(r => r.url().includes('/api/backend-configuration-pn/calendar/boards/'), { timeout: API_TIMEOUT })
      .catch(() => null);
    const tasksLoaded = page
      .waitForResponse(r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/week'), { timeout: API_TIMEOUT })
      .catch(() => null);
    await page.goBack();
    await page.locator('app-calendar-container').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await Promise.all([boardsLoaded, tasksLoaded]);

    // Precondition: this really was an in-app navigation, not a reload (which
    // would reset the store and pass for the wrong reason).
    expect(await page.evaluate(() => (window as any).__calendarReentryNoReload === true)).toBe(true);

    // The property, the chosen calendar (not "Alle kalendere", not the
    // default calendar) and the task are all back.
    await expect(propertyPill).toHaveText(new RegExp(`^\\s*${property.name}\\s*$`), { timeout: UI_TIMEOUT });
    await expect(boardsPill).toHaveText(new RegExp(`^\\s*${boardName}\\s*$`), { timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(eventTitle)).toBeVisible({ timeout: UI_TIMEOUT });

    // The calendars list is populated and our calendar is still the ticked one.
    await calendar.openBoardMenu();
    await expect(calendar.boardItem(boardName).locator('.board-checkbox.active'))
      .toBeVisible({ timeout: UI_TIMEOUT });
    expect(await calendar.boardMenuPanel().locator('.board-item').count()).toBeGreaterThanOrEqual(2);
    await calendar.closeBoardMenu();

    // The property's employee list is populated again.
    await calendar.openAssigneeFilter();
    await expect(calendar.assigneeFilterPanel().locator('.employee-row').first())
      .toBeVisible({ timeout: UI_TIMEOUT });
    await calendar.closeAssigneeFilter();
  });
});
