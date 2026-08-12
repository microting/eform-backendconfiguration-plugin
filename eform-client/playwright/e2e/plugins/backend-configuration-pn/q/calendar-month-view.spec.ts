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
 * Calendar MONTH VIEW suite (calendar month-view feature).
 *
 * The view-mode dropdown (`#calendarViewModeSelect`) gains a third
 * option — "Måned" — between "Uge" and "Tidsplan", available to every user.
 * For an ADMIN the full order is: Dag, Uge, Måned, Tidsplan, Compliance
 * (see `q/calendar-admin-gating.spec.ts` for the non-admin variant, which
 * keeps Måned but lacks the admin-only Compliance).
 *
 * Month view renders 6 `.month-week-row` rows, 6 `.wk-cell` week-number
 * buttons, and `.month-day-cell` cells (each carrying a `.day-number`
 * button); tasks render as passive `.month-chip` elements — clicking a chip
 * opens neither the preview popover (`app-task-preview-modal`) nor the
 * edit modal (`#calendarEventTitle`). `#monthScheduleLink` (top-right)
 * jumps to the schedule (Tidsplan) view scoped to the WHOLE displayed
 * month, rather than the usual single-week scope.
 *
 * Verified click-throughs:
 *   - MV1: dropdown order + read-only grid render + chip passivity.
 *   - MV2: `.wk-cell` click opens WEEK view for that ISO week
 *          (`.week-badge` shows "Uge N").
 *   - MV3: `.day-number` click opens DAY view for that date (long-form
 *          title containing "15.").
 *   - MV4: `#monthScheduleLink` opens the SCHEDULE view scoped to the whole
 *          month (title stays the month title, e.g. "juli 2026") — unlike
 *          the normal week-scoped Tidsplan.
 *
 * DEVIATION FROM THE ORIGINAL DESIGN for MV4's final assertion: re-selecting
 * "Tidsplan" from the dropdown while ALREADY in month-scoped Tidsplan is a
 * NO-OP — ng-select does not emit a change event on a same-value selection,
 * so a direct "select Tidsplan again" step would never fire and the title
 * would never change (a literal "click Tidsplan, expect title to change"
 * assertion FAILS). Instead, MV4 first switches to a DIFFERENT view ("Uge")
 * to break the same-value no-op, THEN selects "Tidsplan" again and asserts
 * the resulting title is a single-day long-form date (matches `/\d{1,2}\./`)
 * that differs from the month title — proving the scope reset back to the
 * normal week-scoped schedule.
 *
 * Single property + single worker seed suffices (mirrors
 * `v/calendar-create-validation.spec.ts`). Only MV1 persists a task (on
 * next week's Monday, via `openCreateModalAt9AM` + `fillAndSaveEvent`);
 * MV2-MV4 only switch view modes and click existing grid affordances, so
 * there is no shared-slot collision risk across the serial suite.
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

test.describe.serial('Calendar month view', () => {
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
  // Seed test — property + ONE worker. Runs first via describe.serial.
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

  // ----- shared helpers ---------------------------------------------------

  // Switch the calendar view mode via the header dropdown. `label` matches
  // the visible Danish option text ('Dag', 'Uge', 'Måned', 'Tidsplan',
  // 'Compliance').
  async function selectViewMode(page: import('@playwright/test').Page, label: string): Promise<void> {
    await page.locator('#calendarViewModeSelect').click();
    await page.locator('.ng-dropdown-panel .ng-option', {hasText: label}).first().click();
    await page.waitForTimeout(800);
  }

  // =======================================================================
  // MV1 — dropdown order, read-only month grid, chip passivity.
  // =======================================================================
  test('MV1: dropdown order and month grid renders read-only', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);

    // seed one task next week Monday 9:00 via fillAndSaveEvent (has a headline)
    await calendarPage.openCreateModalAt9AM();
    const title = `mv-task-${generateRandmString()}`;
    await calendarPage.fillAndSaveEvent(title);

    await page.locator('#calendarViewModeSelect').click();
    const options = page.locator('.ng-dropdown-panel .ng-option');
    await expect(options).toHaveText(['Dag', 'Uge', 'Måned', 'Tidsplan', 'Compliance']);
    await options.filter({hasText: 'Måned'}).first().click();
    await page.waitForTimeout(1200);

    await expect(page.locator('.month-week-row')).toHaveCount(6);
    await expect(page.locator('.wk-cell')).toHaveCount(6);
    const chip = page.locator('.month-chip', {hasText: title}).first();
    await expect(chip).toBeVisible();
    // Passive: clicking a chip opens neither preview nor edit modal.
    await chip.click();
    await page.waitForTimeout(500);
    await expect(page.locator('app-task-preview-modal')).toHaveCount(0);
    await expect(page.locator('#calendarEventTitle')).toHaveCount(0);
  });

  // =======================================================================
  // MV2 — week-number click opens that week in week view.
  // =======================================================================
  test('MV2: week-number click opens that week in week view', async ({ page }) => {
    await selectViewMode(page, 'Måned');
    const wk = page.locator('.wk-cell').nth(2);
    const wkNum = (await wk.innerText()).trim();
    await wk.click();
    await page.waitForTimeout(1200);
    await expect(page.locator('.week-badge')).toHaveText(`Uge ${wkNum}`);
  });

  // =======================================================================
  // MV3 — day click opens that date in day view.
  // =======================================================================
  test('MV3: day click opens that date in day view', async ({ page }) => {
    await selectViewMode(page, 'Måned');
    // click the 15th of the current month
    const dayBtn = page.locator('.month-day-cell:not(.out-month) .day-number', {hasText: /^15$/}).first();
    await dayBtn.click();
    await page.waitForTimeout(1200);
    // day view shows a single-day title containing "15."
    await expect(page.locator('.date-range')).toContainText('15.');
  });

  // =======================================================================
  // MV4 — Tidsplan link shows the whole month; dropdown returns to week scope.
  // =======================================================================
  // See the file-header DEVIATION note: re-selecting 'Tidsplan' while already
  // in month-scoped Tidsplan is a same-value no-op in ng-select, so the scope
  // reset is proven by switching to 'Uge' first, then back to 'Tidsplan'.
  test('MV4: Tidsplan link shows the whole month; dropdown returns to week scope', async ({ page }) => {
    await selectViewMode(page, 'Måned');
    const monthTitle = (await page.locator('.date-range').innerText()).trim();
    await page.locator('#monthScheduleLink').click();
    await page.waitForTimeout(1200);
    // month-scoped schedule keeps the month title (not a single-day title)
    await expect(page.locator('.date-range')).toHaveText(monthTitle);

    // Break the ng-select same-value no-op by switching to a different view
    // first, then re-selecting Tidsplan — this proves the scope resets back
    // to the normal week-scoped schedule (single-day long-form title).
    await selectViewMode(page, 'Uge');
    await selectViewMode(page, 'Tidsplan');
    const scheduleTitle = (await page.locator('.date-range').innerText()).trim();
    expect(scheduleTitle).not.toBe(monthTitle);
    expect(scheduleTitle).toMatch(/\d{1,2}\./);
  });
});
