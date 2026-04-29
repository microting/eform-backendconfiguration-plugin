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
 * Drag-resize regression suite — each test creates its own event so the
 * resize result can be asserted independently. Behaviour is exhaustively
 * covered by the C# integration tests (CalendarResizeTests.cs); this
 * suite proves the UI plumbing (gesture → optimistic update → scope
 * modal → backend → reload) works end to end.
 *
 * Lives in `r/` to share the matrix slot with the existing UI-enhancement
 * suite. Reuses the same property/worker seed pattern.
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

// HOUR_HEIGHT = 52 px; resize snaps to 15-min (13 px) increments.
const HOUR_PX = 52;

test.describe.serial('Calendar event resize', () => {
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

  // Helper — open create modal, save a 1-h non-recurring event with the
  // given title at next week's day-of-week `dayOffset` (0=Mon..6=Sun)
  // at 09:00. Each test in the suite should use a distinct dayOffset
  // so subsequent tests don't accidentally click on a previously-
  // created event (which would open the preview rather than create).
  async function createSimpleEvent(
    page: import('@playwright/test').Page,
    calendarPage: CalendarUiEnhancementsPage,
    title: string,
    dayOffset: number = 0,
    hour: number = 9,
  ) {
    await calendarPage.openCreateModalAtSlot(dayOffset, hour);
    await page.locator('#calendarEventTitle').fill(title);
    // Pick first eForm (required by backend validation in the suite)
    const eform = page.locator('#calendarEventEform');
    await eform.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);
    // Pick first planning tag
    const planningTag = page.locator('#calendarEventPlanningTag');
    await planningTag.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);
    // Pick first assignee
    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    const createResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await createResp;
    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
  }

  // =======================================================================
  // D. Single (non-recurring) event resize — modal does NOT pop, backend
  // commits with scope='this' silently.
  // =======================================================================
  test.describe('Single event resize', () => {
    test('D1: expand from end (drag bottom down) extends duration', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D1-${generateRandmString(5)}`;
      await createSimpleEvent(page, calendarPage, title, 0); // Mon

      // Default: 09:00 – 10:00. Drag bottom down 1 hour → 09:00 – 11:00.
      // dragResizeHandle awaits the post-resize /tasks/week POST internally.
      await calendarPage.dragResizeHandle(title, 'bottom', HOUR_PX);

      const timeText = await calendarPage.getEventTimeText(title);
      expect(timeText).toContain('09:00');
      expect(timeText).toContain('11:00');
    });

    test('D2: shrink from end (drag bottom up) shortens duration', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D2-${generateRandmString(5)}`;
      await createSimpleEvent(page, calendarPage, title, 1); // Tue

      // Default: 09:00 – 10:00. Drag bottom up 30 min → 09:00 – 09:30.
      await calendarPage.dragResizeHandle(title, 'bottom', -Math.round(HOUR_PX / 2));

      // Block height is now 30 min — .task-time only renders when
      // duration ≥ 0.5 h; 0.5 is the boundary so it should render. If
      // empty, fall back to height-based assertion.
      const timeText = await calendarPage.getEventTimeText(title);
      if (timeText.length > 0) {
        expect(timeText).toContain('09:00');
        expect(timeText).toContain('09:30');
      } else {
        const block = calendarPage.findEventBlock(title);
        const box = await block.boundingBox();
        expect(box).not.toBeNull();
        // 30 min = 26 px (half of HOUR_PX); -4 padding = 22 px height.
        expect(box!.height).toBeLessThan(40);
      }
    });

    test('D3: expand from start (drag top up) earlier-shifts start', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D3-${generateRandmString(5)}`;
      await createSimpleEvent(page, calendarPage, title, 2); // Wed

      // Default 09:00 – 10:00. Drag top up 1 hour → 08:00 – 10:00.
      await calendarPage.dragResizeHandle(title, 'top', -HOUR_PX);

      const timeText = await calendarPage.getEventTimeText(title);
      expect(timeText).toContain('08:00');
      expect(timeText).toContain('10:00');
    });

    test('D4: shrink from start (drag top down) later-shifts start', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D4-${generateRandmString(5)}`;
      await createSimpleEvent(page, calendarPage, title, 3); // Thu

      // Default 09:00 – 10:00. Drag top down 30 min → 09:30 – 10:00.
      await calendarPage.dragResizeHandle(title, 'top', Math.round(HOUR_PX / 2));

      const timeText = await calendarPage.getEventTimeText(title);
      if (timeText.length > 0) {
        expect(timeText).toContain('09:30');
        expect(timeText).toContain('10:00');
      } else {
        const block = calendarPage.findEventBlock(title);
        const box = await block.boundingBox();
        expect(box).not.toBeNull();
        expect(box!.height).toBeLessThan(40);
      }
    });
  });

  // =======================================================================
  // E. Recurring event resize — verifies the past-occurrence preservation
  //    fix end to end. Without the backfill in ResizeTask
  //    (BackendConfigurationCalendarService.cs), past occurrences with
  //    no exception row would resolve through the new calConfig and
  //    visually shift; the test below would fail.
  // =======================================================================
  test.describe('Recurring event — thisAndFollowing past preservation', () => {
    test('E1: resize 2 weeks ahead with thisAndFollowing leaves earlier-week occurrence unchanged', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `E1-${generateRandmString(5)}`;

      // Create a weekly recurring event on Friday of next week (week +1)
      // at 09:00 — Friday avoids slot collisions with D1..D4 (Mon..Thu).
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

      // Make it weekly. Index 2 in the repeat dropdown = 'weeklyOne'.
      await calendarPage.setRepeatToWeekly();

      // Match the create endpoint specifically (POST .../tasks), not the
      // loadTasks reload (POST .../tasks/week) which fires repeatedly.
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

      // We are on week +1 with an event at Friday 09:00 – 10:00. Advance
      // two weeks → week +3 (the resize anchor).
      await calendarPage.navigateToNextWeek();
      await calendarPage.navigateToNextWeek();

      // Sanity check: the week +3 occurrence still shows the original time.
      const preResize = await calendarPage.getEventTimeText(title);
      expect(preResize).toContain('09:00');
      expect(preResize).toContain('10:00');

      // Drag the TOP edge up 1 hour → start moves to 08:00 (duration 2h).
      // Skip the internal awaitReload — the scope modal pops between
      // mouse.up and the eventual /tasks/week reload.
      await calendarPage.dragResizeHandle(title, 'top', -HOUR_PX, { awaitReload: false });

      // Pick "this and following" — the bug-fix path.
      const reloadAfterScope = page.waitForResponse(
        r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/week')
          && r.request().method() === 'POST',
        { timeout: 30000 }
      );
      await calendarPage.pickScopeInModal('thisAndFollowing');
      await reloadAfterScope;
      await page.waitForTimeout(800);

      // Confirm the week +3 occurrence now shows the NEW time.
      const postResize = await calendarPage.getEventTimeText(title);
      expect(postResize).toContain('08:00');
      expect(postResize).toContain('10:00');

      // Navigate back ONE week → week +2 (BEFORE the resize anchor).
      // The past-occurrence backfill must have anchored this occurrence
      // with the OLD start/duration so it still shows 09:00 – 10:00.
      await calendarPage.navigateToPreviousWeek();

      const preserved = await calendarPage.getEventTimeText(title);
      expect(preserved).toContain('09:00');
      expect(preserved).toContain('10:00');
      // Critical regression check: the earlier-week occurrence must NOT
      // have inherited the new 08:00 start time from calConfig.
      expect(preserved).not.toContain('08:00');
    });

    test('E2: thisAndFollowing MOVE preserves earlier weeks; future weeks land on new day/time; pre-series weeks empty', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `E2-${generateRandmString(5)}`;

      // Create a weekly event on Tuesday week +1 at 09:00 — Tuesday avoids
      // collisions with D-tests' Mon..Thu and E1's Friday on the same week.
      // Wait — D2 used Tue. Use Saturday (5) instead.
      const startDay = 5; // Saturday week +1
      await calendarPage.openCreateModalAtSlot(startDay, 9);
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

      await calendarPage.setRepeatToWeekly();

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

      // We are at week +1 with the event on Saturday (day 5) at 09:00.
      // Advance two weeks → week +3 (the move anchor).
      await calendarPage.navigateToNextWeek();
      await calendarPage.navigateToNextWeek();

      // Verify the week +3 occurrence is still on Saturday at 09:00 before
      // the move (sanity check for the test setup).
      expect(await calendarPage.getEventDayIndex(title)).toBe(startDay);

      // Drag the event from Saturday (5) at 09:00 to Wednesday (2) at 14:00.
      // Skip awaitReload because the scope modal pops between mouse.up and
      // the eventual reload.
      const targetDay = 2; // Wednesday
      const targetHour = 14;
      await calendarPage.dragEventToSlot(title, targetDay, targetHour, { awaitReload: false });

      const reloadAfterScope = page.waitForResponse(
        r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/week')
          && r.request().method() === 'POST',
        { timeout: 30000 }
      );
      await calendarPage.pickScopeInModal('thisAndFollowing');
      await reloadAfterScope;
      await page.waitForTimeout(800);

      // Week +3 (anchor): event should now be on Wed 14:00.
      expect(await calendarPage.getEventDayIndex(title)).toBe(targetDay);
      const anchorTimeText = await calendarPage.getEventTimeText(title);
      expect(anchorTimeText).toContain('14:00');

      // Navigate back -1 week → week +2: event should still be on the
      // ORIGINAL day (Saturday) at 09:00 (anchored past).
      await calendarPage.navigateToPreviousWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(startDay);
      const w2Time = await calendarPage.getEventTimeText(title);
      expect(w2Time).toContain('09:00');
      expect(w2Time).not.toContain('14:00');

      // Navigate back -1 more → week +1 (the series-start week):
      // event still on original day/time.
      await calendarPage.navigateToPreviousWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(startDay);
      const w1Time = await calendarPage.getEventTimeText(title);
      expect(w1Time).toContain('09:00');

      // Navigate back -1 more → week 0 (BEFORE series start): no event.
      await calendarPage.navigateToPreviousWeek();
      await expect(calendarPage.findEventBlock(title)).toHaveCount(0);

      // Navigate +4 weeks → week +4 (one week PAST the move anchor):
      // event must be on the NEW day (Wed) at 14:00.
      await calendarPage.navigateToNextWeek();
      await calendarPage.navigateToNextWeek();
      await calendarPage.navigateToNextWeek();
      await calendarPage.navigateToNextWeek();
      expect(await calendarPage.getEventDayIndex(title)).toBe(targetDay);
      const w4Time = await calendarPage.getEventTimeText(title);
      expect(w4Time).toContain('14:00');
    });
  });

  // =======================================================================
  // F. Schedule (list) view — clicking a row opens the same preview popover
  //    used by the week-grid view (Edit / Duplicate / Delete actions).
  // =======================================================================
  test.describe('Schedule view — preview popover', () => {
    test('F1: clicking a schedule row opens the preview popover with Edit/Duplicate/Delete', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `F1-${generateRandmString(5)}`;

      // Sunday week +1 at 9:00 — no collision with D1..D4 (Mon-Thu),
      // E1 (Fri), or E2 (Sat).
      await createSimpleEvent(page, calendarPage, title, 6);

      await calendarPage.switchToScheduleView();
      // After the view-switch fix, the schedule view preserves the
      // navigated week (no longer snaps currentDate back to today) and
      // the chevron advances 7 days per click. Advance one week to
      // land in the same week as the event (week +1).
      await calendarPage.navigateScheduleByWeeks(1);

      const row = calendarPage.findScheduleItem(title);
      await expect(row).toBeVisible();
      await row.click();

      const preview = page.locator('app-task-preview-modal');
      await expect(preview).toBeVisible();

      await expect(calendarPage.getPreviewEditButton()).toBeVisible();
      await expect(calendarPage.getPreviewCopyButton()).toBeVisible();
      await expect(calendarPage.getPreviewDeleteButton()).toBeVisible();
    });

    test('F2: clicking Edit in the popover opens the edit modal with the title pre-filled', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `F2-${generateRandmString(5)}`;

      // Sunday at 14:00 — F1 already filled Sunday@9, so use a different
      // hour to avoid clicking on the F1 event by mistake.
      await createSimpleEvent(page, calendarPage, title, 6, 14);

      await calendarPage.switchToScheduleView();
      // Schedule view preserves the navigated week post-fix and the
      // chevron advances 7 days per click. Advance one week to land
      // in week +1 where the event lives.
      await calendarPage.navigateScheduleByWeeks(1);
      await calendarPage.findScheduleItem(title).click();
      await page.locator('app-task-preview-modal').waitFor({ state: 'visible', timeout: 10000 });

      await calendarPage.getPreviewEditButton().click();

      // Edit modal title input should appear and be pre-filled.
      const titleInput = page.locator('#calendarEventTitle');
      await titleInput.waitFor({ state: 'visible', timeout: 15000 });
      const value = await titleInput.inputValue();
      expect(value).toContain(title);

      // Tidy up so subsequent tests don't see lingering modal state.
      await calendarPage.closeEventModal();
    });
  });
});
