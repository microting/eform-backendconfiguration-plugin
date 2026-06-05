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

/**
 * Edit-scope regression suite (GitHub #893) — proves the calendar EDIT modal
 * honours the recurring-edit scope (this / thisAndFollowing / all), the
 * acceptance criteria for the #885 fix (now MERGED).
 *
 * Before #885 an edit always relocated the whole series StartDate onto the
 * clicked occurrence; the per-occurrence/`thisAndFollowing` scopes did
 * nothing visible cross-week. These tests drive the same week-math pattern
 * as calendar-resize.spec.ts's E1/E2 (create at week +1, anchor at week +3,
 * mutate, then read earlier/later weeks) but exercise an EDIT-modal field
 * change (start time / title) rather than a drag gesture.
 *
 * Lives in `r/` alongside the resize + UI-enhancement suites; reuses the
 * same property/worker seed pattern and the shared page object.
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

test.describe.serial('Calendar edit scope', () => {
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
  // Seed test — property + worker. Runs first via describe.serial.
  // -----------------------------------------------------------------------
  test('seed: create property + worker', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    seeded = true;
  });

  // Helper — create a WEEKLY recurring event with the given title on the
  // given day-of-week (0=Mon..6=Sun) at `hour`:00 of NEXT week (week +1),
  // then return with the calendar still on week +1 and the block visible.
  //
  // Each test uses a DISTINCT day so it never accidentally clicks an event
  // created by a previous test in the serial suite (which would open the
  // preview instead of the create modal). Mirrors the recurring-create
  // sequence from calendar-resize.spec.ts E1/E2.
  async function createWeeklyEvent(
    page: import('@playwright/test').Page,
    calendarPage: CalendarUiEnhancementsPage,
    title: string,
    dayOffset: number,
    hour: number = 9,
  ): Promise<void> {
    await calendarPage.openCreateModalAtSlot(dayOffset, hour);
    await page.locator('#calendarEventTitle').fill(title);

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

    // Weekly recurrence (index 2 in the repeat dropdown = 'weeklyOne').
    await calendarPage.setRepeatToWeekly();

    // Match the create endpoint (POST .../tasks), not the loadTasks reload
    // (POST .../tasks/week) which fires repeatedly.
    const createResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
        && !r.url().includes('/tasks/week')
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await createResp;
    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
  }

  // =======================================================================
  // Start-time edits — the clearest cross-week assertion. Each test creates
  // a weekly event at week +1 09:00–10:00, advances two weeks to the anchor
  // (week +3), opens the edit modal, changes the start time, saves and picks
  // a scope, then reads earlier/later weeks to confirm the scope semantics.
  // =======================================================================

  test('edit start time, scope=thisAndFollowing — anchor + future weeks change, earlier weeks preserved', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `S1-${generateRandmString(5)}`;

    // Monday week +1 at 09:00.
    await createWeeklyEvent(page, calendarPage, title, 0, 9);

    // Advance two weeks → week +3 (the edit anchor).
    await calendarPage.navigateToNextWeek();
    await calendarPage.navigateToNextWeek();

    // Sanity: the anchor occurrence still shows the original time.
    const pre = await calendarPage.getEventTimeText(title);
    expect(pre).toContain('09:00');
    expect(pre).toContain('10:00');

    // Open edit modal, change start to 11:00, save → scope modal pops.
    await calendarPage.openEditModal(title);
    await calendarPage.typeStartTime('11:00', 'Enter');
    await calendarPage.clickSaveInEditModal();
    await calendarPage.confirmEditScope('thisAndFollowing');

    // Week +3 (anchor) now shows the NEW start time.
    const anchor = await calendarPage.getEventTimeText(title);
    expect(anchor).toContain('11:00');

    // Earlier weeks (+2, +1) must be PRESERVED at the original 09:00.
    await calendarPage.navigateToPreviousWeek();
    const w2 = await calendarPage.getEventTimeText(title);
    expect(w2).toContain('09:00');
    expect(w2).not.toContain('11:00');

    await calendarPage.navigateToPreviousWeek();
    const w1 = await calendarPage.getEventTimeText(title);
    expect(w1).toContain('09:00');
    expect(w1).not.toContain('11:00');

    // Forward to week +4 (one past the anchor) → NEW time applies.
    await calendarPage.navigateToNextWeek();
    await calendarPage.navigateToNextWeek();
    await calendarPage.navigateToNextWeek();
    const w4 = await calendarPage.getEventTimeText(title);
    expect(w4).toContain('11:00');
  });

  test('edit start time, scope=this — only the anchor occurrence changes', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `S2-${generateRandmString(5)}`;

    // Tuesday week +1 at 09:00.
    await createWeeklyEvent(page, calendarPage, title, 1, 9);

    await calendarPage.navigateToNextWeek();
    await calendarPage.navigateToNextWeek();

    const pre = await calendarPage.getEventTimeText(title);
    expect(pre).toContain('09:00');
    expect(pre).toContain('10:00');

    // Change start to 13:00 with scope=this.
    await calendarPage.openEditModal(title);
    await calendarPage.typeStartTime('13:00', 'Enter');
    await calendarPage.clickSaveInEditModal();
    await calendarPage.confirmEditScope('this');

    // Week +3 (anchor) shows the NEW time.
    const anchor = await calendarPage.getEventTimeText(title);
    expect(anchor).toContain('13:00');

    // Every OTHER week (+2, +1, +4) is untouched at 09:00.
    await calendarPage.navigateToPreviousWeek();
    const w2 = await calendarPage.getEventTimeText(title);
    expect(w2).toContain('09:00');
    expect(w2).not.toContain('13:00');

    await calendarPage.navigateToPreviousWeek();
    const w1 = await calendarPage.getEventTimeText(title);
    expect(w1).toContain('09:00');
    expect(w1).not.toContain('13:00');

    await calendarPage.navigateToNextWeek();
    await calendarPage.navigateToNextWeek();
    await calendarPage.navigateToNextWeek();
    const w4 = await calendarPage.getEventTimeText(title);
    expect(w4).toContain('09:00');
    expect(w4).not.toContain('13:00');
  });

  test('edit start time, scope=all — every week reflects the new time', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `S3-${generateRandmString(5)}`;

    // Wednesday week +1 at 09:00.
    await createWeeklyEvent(page, calendarPage, title, 2, 9);

    await calendarPage.navigateToNextWeek();
    await calendarPage.navigateToNextWeek();

    const pre = await calendarPage.getEventTimeText(title);
    expect(pre).toContain('09:00');
    expect(pre).toContain('10:00');

    // Change start to 08:00 with scope=all.
    await calendarPage.openEditModal(title);
    await calendarPage.typeStartTime('08:00', 'Enter');
    await calendarPage.clickSaveInEditModal();
    await calendarPage.confirmEditScope('all');

    // Week +3 (anchor) shows the NEW time.
    const anchor = await calendarPage.getEventTimeText(title);
    expect(anchor).toContain('08:00');

    // Earlier weeks (+2, +1) ALSO reflect the new time.
    await calendarPage.navigateToPreviousWeek();
    const w2 = await calendarPage.getEventTimeText(title);
    expect(w2).toContain('08:00');

    await calendarPage.navigateToPreviousWeek();
    const w1 = await calendarPage.getEventTimeText(title);
    expect(w1).toContain('08:00');

    // Later week (+4) too.
    await calendarPage.navigateToNextWeek();
    await calendarPage.navigateToNextWeek();
    await calendarPage.navigateToNextWeek();
    const w4 = await calendarPage.getEventTimeText(title);
    expect(w4).toContain('08:00');
  });

  // =======================================================================
  // Title edit — scope=this overrides only the anchor occurrence's title;
  // earlier weeks keep the series title.
  // =======================================================================

  test('edit title, scope=this — only the anchor occurrence shows the new title', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const baseTitle = `S4-${generateRandmString(5)}`;
    const overrideTitle = `S4ovr-${generateRandmString(5)}`;

    // Thursday week +1 at 09:00.
    await createWeeklyEvent(page, calendarPage, baseTitle, 3, 9);

    await calendarPage.navigateToNextWeek();
    await calendarPage.navigateToNextWeek();

    // Sanity: anchor occurrence carries the original series title.
    expect(await calendarPage.getEventTitleText(baseTitle)).toContain(baseTitle);

    // Override the title with scope=this.
    await calendarPage.openEditModal(baseTitle);
    const titleInput = page.locator('#calendarEventTitle');
    await titleInput.fill(overrideTitle);
    await page.waitForTimeout(200);
    await calendarPage.clickSaveInEditModal();
    await calendarPage.confirmEditScope('this');

    // Week +3 (anchor) now shows the OVERRIDDEN title; the original series
    // title is no longer present on this week.
    await calendarPage.findEventBlock(overrideTitle).waitFor({ state: 'visible', timeout: 10000 });
    expect(await calendarPage.getEventTitleText(overrideTitle)).toContain(overrideTitle);

    // Earlier week (+2) still shows the ORIGINAL series title, and NOT the
    // per-occurrence override.
    await calendarPage.navigateToPreviousWeek();
    await calendarPage.findEventBlock(baseTitle).waitFor({ state: 'visible', timeout: 10000 });
    expect(await calendarPage.getEventTitleText(baseTitle)).toContain(baseTitle);
    await expect(calendarPage.findEventBlock(overrideTitle)).toHaveCount(0);
  });

  // #960 — editing the DATE of a monthly Nth-weekday series must re-derive the
  // "Gentag" (Repeat) label to follow the new weekday. Pre-fix the label stayed
  // pinned to the loaded rule meta (e.g. "1st Friday") even after moving the
  // date to a different weekday, silently persisting the stale pattern on save.
  test('edit date re-anchors monthly "Gentag" label to the new weekday (#960)', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `S5-${generateRandmString(5)}`;

    // Create a MONTHLY (Nth-weekday) event on Friday of week +1 at 09:00.
    await calendarPage.openCreateModalAtSlot(4, 9);
    await page.locator('#calendarEventTitle').fill(title);

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

    // Monthly on the Nth weekday (index 3 in the repeat dropdown = 'monthlyByDay').
    await calendarPage.selectRepeatPreset('monthlyByDay');

    const createResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
        && !r.url().includes('/tasks/week')
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await createResp;
    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });

    // Reopen the series in edit mode → the repeat select reconstructs the
    // synthesized "customCurrent" monthly-Nth-weekday option, whose label is
    // derived from the loaded rule meta (the Friday weekday).
    await calendarPage.openEditModal(title);
    const before = await calendarPage.getRepeatSelectLabel();
    expect(before.length).toBeGreaterThan(0);

    // Move the date by one day → the weekday changes. The re-anchor fix must
    // update the "Gentag" label to the new weekday; pre-fix it stayed pinned.
    await calendarPage.shiftEventDateByOneDay();
    const after = await calendarPage.getRepeatSelectLabel();

    expect(after).not.toEqual(before);
  });
});
