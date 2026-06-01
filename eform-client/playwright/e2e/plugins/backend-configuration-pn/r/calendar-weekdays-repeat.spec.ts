import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from './calendar-ui-enhancements.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';

/**
 * Regression suite for the "Alle hverdage (mandag til fredag)" repeat option.
 *
 * BUG GUARDED AGAINST
 * -------------------
 * The built-in `weekdays` repeat preset (value `'weekdays'` in
 * calendar-repeat.service.buildRepeatSelectOptions) used to ship
 * RepeatType=5 + repeatWeekdaysCsv=null on the create payload, which made
 * the backend persist the planning as a single Monday event — Tue–Fri (and
 * subsequent weeks) never materialised.
 *
 * After the fix, the modal now ships RepeatType=2 (Week) +
 * repeatWeekdaysCsv="1,2,3,4,5" so the recurring planning expands to
 * Mon-Fri of every week, starting from the event's start date.
 *
 * The three test cases below assert the post-fix behaviour from a black-box
 * angle: a single Monday-anchored weekdays-repeat event must produce
 * `.task-block`s on Mon–Fri of the seeded week AND of the following week,
 * and must NOT produce any blocks on Saturday or Sunday of either week.
 */

const property: PropertyCreateUpdate = {
  name: generateRandmString(5),
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

let seeded = false;

// Unique title for the single event created by the weekdays-repeat test.
// Declared at module scope so the second visibility test (next week) and
// the weekend-exclusion test can locate the same recurring series.
const weekdaysTitle = `WK-${generateRandmString(8)}`;

test.describe.serial('Calendar weekdays repeat — Mon-Fri visibility', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();

    if (seeded) {
      const folderResp = page.waitForResponse(
        r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
        { timeout: 60000 }
      );
      await calendarPage.selectProperty(property.name);
      await folderResp.catch(() => undefined);
      await page.waitForTimeout(1000);
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
  // Seed test — create property + worker. Runs first via describe.serial.
  // -----------------------------------------------------------------------
  test('seed property and worker', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    seeded = true;
  });

  // =======================================================================
  // W1. Create one event on Monday of next week with the "Alle hverdage"
  //     repeat preset, and assert task-blocks appear on Mon-Fri of that
  //     same week.
  //
  //     openCreateModalAtSlot(0, 9) advances the calendar by one week and
  //     then clicks Monday@9AM — so the event's start date lands on the
  //     Monday of the displayed (next) week. The weekdays preset has
  //     endMode='never' so every subsequent weekday in that week is also
  //     materialised.
  // =======================================================================
  test('weekdays repeat creates events on Mon-Fri of the current week', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);

    await calendarPage.openCreateModalAtSlot(0, 9);

    // Fill title + eForm + planning tag + assignee + save. We need to set
    // the repeat preset BEFORE the save flow inside fillAndSaveEvent fires
    // the create POST, so we do the field-fill steps manually here rather
    // than reusing fillAndSaveEvent verbatim. Mirrors the I1 test pattern
    // in calendar-ui-enhancements.spec.ts.
    await page.locator('#calendarEventTitle').fill(weekdaysTitle);

    const eform = page.locator('#calendarEventEform');
    await eform.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);

    const planningTag = page.locator('#calendarEventPlanningTag');
    await planningTag.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);

    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    // Pick the "Alle hverdage (mandag til fredag)" preset. Locator goes by
    // positional index (5) inside the repeat ng-select to stay locale-
    // independent — same convention as setRepeatToWeekly / setRepeat-
    // ToCustom usage elsewhere in this suite.
    await calendarPage.selectRepeatPreset('weekdays');

    // Save. Pre-register the POST waiter so the listener can't miss the
    // response on a fast machine.
    const createResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
        && !r.url().includes('/tasks/week')
        && !r.url().includes('/tasks/move')
        && !r.url().includes('/tasks/resize')
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await createResp;
    // Allow the post-save tasksReload → /tasks/week round-trip + grid
    // re-render to settle before reading per-day DOM state.
    await page.waitForTimeout(2000);

    // Mon-Fri (data-day 0..4) of the displayed (next) week must each have
    // at least one .task-block carrying our unique title.
    for (let day = 0; day <= 4; day++) {
      const blocks = calendarPage.getDayColumnTaskBlocks(day, weekdaysTitle);
      const count = await blocks.count();
      expect(
        count,
        `Expected weekdays-repeat block on day-of-week index ${day} (Mon=0..Fri=4) ` +
        `for "${weekdaysTitle}", but found ${count}.`
      ).toBeGreaterThanOrEqual(1);
    }
  });

  // =======================================================================
  // W2. After advancing one more week via chevron_right, the same Mon-Fri
  //     presence must hold (the weekdays preset has endMode='never').
  //
  //     Relies on W1 having created the seed event. describe.serial keeps
  //     the test order deterministic. The beforeEach already navigates to
  //     the calendar at the current week — so a single navigateToNextWeek
  //     lands on (today's-week + 7d), which is the SAME week W1 created
  //     events in. A second navigateToNextWeek lands on the week AFTER
  //     that (today's-week + 14d), which is what this test asserts.
  // =======================================================================
  test('weekdays repeat creates events on Mon-Fri of next week (one week after)', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);

    // beforeEach already opened the calendar at today's week with the
    // seeded property selected. Two chevron-right clicks → week containing
    // (Monday+14d). Each click awaits its /tasks/week POST so the new
    // week's data is loaded before we assert.
    await calendarPage.navigateToNextWeek();
    await calendarPage.navigateToNextWeek();

    for (let day = 0; day <= 4; day++) {
      const blocks = calendarPage.getDayColumnTaskBlocks(day, weekdaysTitle);
      const count = await blocks.count();
      expect(
        count,
        `Expected weekdays-repeat block on day-of-week index ${day} (Mon=0..Fri=4) ` +
        `in the week after the seed week for "${weekdaysTitle}", but found ${count}.`
      ).toBeGreaterThanOrEqual(1);
    }
  });

  // =======================================================================
  // W3. Saturday + Sunday in the seed week must have NO .task-block
  //     matching the weekdays-repeat series. This guards against a
  //     symmetric regression where the fix flips too far the other way
  //     (e.g. ships repeatWeekdaysCsv="1,2,3,4,5,6,7" by accident).
  //
  //     This test re-navigates to the seed week from today (one
  //     navigateToNextWeek) so it's independent of W2's state.
  // =======================================================================
  test('weekdays repeat does NOT create events on Saturday or Sunday', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);

    // Land on the same week W1 created events in (today + 7d).
    await calendarPage.navigateToNextWeek();

    for (const day of [5, 6]) {
      const blocks = calendarPage.getDayColumnTaskBlocks(day, weekdaysTitle);
      const count = await blocks.count();
      expect(
        count,
        `Expected NO weekdays-repeat block on day-of-week index ${day} ` +
        `(Sat=5/Sun=6) for "${weekdaysTitle}", but found ${count}. ` +
        `The "weekdays" preset must only materialise Mon-Fri.`
      ).toBe(0);
    }
  });
});
