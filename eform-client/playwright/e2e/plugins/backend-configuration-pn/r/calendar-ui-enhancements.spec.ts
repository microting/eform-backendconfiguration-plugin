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

// Shared property/worker — created by the first (seed) test, reused by the
// rest of the suite via `test.describe.serial` semantics. Matches the
// `l/calendar.spec.ts` pattern (lines 14–58) intentionally.
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

// ---------- Local ISO-week + week-grid helpers ----------------------------
// Duplicated from CalendarMiniCalendarComponent (mini-calendar.component.ts
// lines 73–118) so B2 stays self-contained and date-deterministic in CI.

function getIsoWeek(d: Date): number {
  const date = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()));
  const dayNum = date.getUTCDay() || 7;
  date.setUTCDate(date.getUTCDate() + 4 - dayNum);
  const yearStart = new Date(Date.UTC(date.getUTCFullYear(), 0, 1));
  return Math.ceil((((date.getTime() - yearStart.getTime()) / 86400000) + 1) / 7);
}

/**
 * Return the 6 ISO week numbers shown on the mini-calendar's grid for the
 * given month, mirroring `buildCalendar()` in mini-calendar.component.ts.
 * Monday-anchored rows; each row's number is computed from its first
 * (Monday) day.
 */
function expectedWeeksForMonth(year: number, month0Indexed: number): number[] {
  const firstDay = new Date(year, month0Indexed, 1);
  const startOffset = (firstDay.getDay() + 6) % 7; // 0 = Monday
  const weeks: number[] = [];
  for (let row = 0; row < 6; row++) {
    const monday = new Date(year, month0Indexed, 1 + row * 7 - startOffset);
    weeks.push(getIsoWeek(monday));
  }
  return weeks;
}

function addMonths(d: Date, n: number): Date {
  return new Date(d.getFullYear(), d.getMonth() + n, 1);
}

// The Monday of NEXT week — matches the slot openCreateModalAt9AM clicks
// (chevron_right advances one week; clickEmptyTimeSlot(0) is Monday of the
// displayed week). Today→Mon means +7; today→other means days-until-Monday + 7
// when chevron_right has been clicked once. Wait — actually: chevron advances
// to next week (Mon-Sun), and slot 0 = that Monday. So it's always
// "Monday of (current week) + 7 days".
function mondayOfNextWeek(today: Date = new Date()): Date {
  const d = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  // getDay(): 0=Sun..6=Sat. Distance back to current Monday:
  const dayOfWeek = d.getDay();
  const backToMonday = dayOfWeek === 0 ? 6 : dayOfWeek - 1;
  d.setDate(d.getDate() - backToMonday + 7); // current Mon, then +7 → next Mon
  return d;
}

test.describe.serial('Calendar UI enhancements', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();

    if (seeded) {
      const folderResponsePromise = page.waitForResponse(
        r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
        { timeout: 60000 }
      );
      await calendarPage.selectProperty(property.name);
      await folderResponsePromise.catch(() => undefined);
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
  // Seed test — create property + worker. Runs first thanks to
  // describe.serial. Subsequent beforeEach picks up the seeded property.
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

  // =======================================================================
  // A. Time picker — free-form entry (8 tests, A1..A8)
  // =======================================================================
  test.describe('Time picker — free-form entry', () => {
    test('A1: typing 18:04 in start + Enter sets end to 19:04', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await calendarPage.openCreateModalAt9AM();

      // Default seed is 09:00 → 10:00 (1h duration).
      await calendarPage.typeStartTime('18:04', 'Enter');

      expect(await calendarPage.getStartTimeValue()).toBe('18:04');
      expect(await calendarPage.getEndTimeValue()).toBe('19:04');

      await calendarPage.closeEventModal();
    });

    test('A2: typing 18:04 + Tab commits the value (same as Enter)', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await calendarPage.openCreateModalAt9AM();

      await calendarPage.typeStartTime('18:04', 'Tab');

      // Value commit is what selectOnTab guarantees. Tab does NOT also move
      // focus to the end input — mtx-select renders the next input with
      // tabindex="NaN" which the browser skips, so we don't assert focus.
      expect(await calendarPage.getStartTimeValue()).toBe('18:04');
      expect(await calendarPage.getEndTimeValue()).toBe('19:04');

      await calendarPage.closeEventModal();
    });

    test('A3: custom duration is preserved on free-form start change', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await calendarPage.openCreateModalAt9AM();

      // Default 09:00→10:00. Set end=11:00 via dropdown → duration is 2h.
      await calendarPage.setEndTimeFromDropdown('11:00');
      expect(await calendarPage.getEndTimeValue()).toBe('11:00');

      // Type 13:00 in start → end should jump to 15:00 (2h preserved).
      await calendarPage.typeStartTime('13:00', 'Enter');

      expect(await calendarPage.getStartTimeValue()).toBe('13:00');
      expect(await calendarPage.getEndTimeValue()).toBe('15:00');

      await calendarPage.closeEventModal();
    });

    test('A4: compact forms 1804 / 9:7 / 804 normalize to HH:MM', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);

      // 1804 → 18:04 (no preset matches the substring "1804")
      await calendarPage.openCreateModalAt9AM();
      await calendarPage.typeStartTime('1804', 'Enter');
      expect(await calendarPage.getStartTimeValue()).toBe('18:04');
      await calendarPage.closeEventModal();

      // 9:7 → 09:07. Avoids forms like "8:4" where ng-select's substring
      // filter would match a preset ("08:45" contains "8:4") and Enter
      // would commit the preset rather than route through addTag. ":7"
      // is not a 15-min boundary so no preset can ever match.
      await calendarPage.openCreateModalAt9AM();
      await calendarPage.typeStartTime('9:7', 'Enter');
      expect(await calendarPage.getStartTimeValue()).toBe('09:07');
      await calendarPage.closeEventModal();

      // 804 → 08:04 (no preset substring-matches "804")
      await calendarPage.openCreateModalAt9AM();
      await calendarPage.typeStartTime('804', 'Enter');
      expect(await calendarPage.getStartTimeValue()).toBe('08:04');
      await calendarPage.closeEventModal();
    });

    test('A5: invalid input (25:99 / abc) is rejected, value unchanged', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await calendarPage.openCreateModalAt9AM();

      // 25:99 — addTimeTag throws, ng-select keeps panel open + drops entry
      await calendarPage.typeStartTime('25:99', 'Enter');
      expect(await calendarPage.getStartTimeValue()).toBe('09:00');
      // Panel should still be visible (ng-select didn't close on rejected addTag)
      await expect(page.locator('.ng-dropdown-panel')).toBeVisible();

      // Close and reopen for the abc sub-case so we get a clean panel.
      await calendarPage.closeEventModal();
      await calendarPage.openCreateModalAt9AM();

      await calendarPage.typeStartTime('abc', 'Enter');
      expect(await calendarPage.getStartTimeValue()).toBe('09:00');
      await expect(page.locator('.ng-dropdown-panel')).toBeVisible();

      await calendarPage.closeEventModal();
    });

    test('A6: end-time field accepts typed input independently', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await calendarPage.openCreateModalAt9AM();

      await calendarPage.typeEndTime('15:30', 'Enter');

      expect(await calendarPage.getEndTimeValue()).toBe('15:30');
      // Start untouched — there is no inverse "adjust start" subscription.
      expect(await calendarPage.getStartTimeValue()).toBe('09:00');

      await calendarPage.closeEventModal();
    });

    test('A7: end clamps at 24 (hourToTimeStr(24) → 23:00)', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await calendarPage.openCreateModalAt9AM();

      // Set end to 23:30 from the dropdown → duration becomes 14:30 (start
      // is still 09:00). Typing a new start of 23:00 keeps end well under 24.
      await calendarPage.setEndTimeFromDropdown('23:30');
      expect(await calendarPage.getEndTimeValue()).toBe('23:30');

      // First sub-case: shrink window to 30 min by setting start=23:00.
      // Duration was 14.5h; with the duration-preserve rule end = 23 + 14.5
      // = 37.5 → clamped to 24 → hourToTimeStr(24) = "23:00".
      // We verify the clamp result matches the helper output exactly.
      await calendarPage.typeStartTime('23:00', 'Enter');
      expect(await calendarPage.getStartTimeValue()).toBe('23:00');
      // End is clamped through Math.min(..., 24) → hourToTimeStr(24)
      // produces "23:00" because Math.floor(Math.min(24, 23.75)) = 23 and
      // 24 % 1 === 0. This is the documented helper output (see
      // task-create-edit-modal.component.ts:422–426).
      expect(await calendarPage.getEndTimeValue()).toBe('23:00');

      // Second sub-case: pushing further (23:50) should still clamp, not
      // exceed 24.
      await calendarPage.typeStartTime('23:50', 'Enter');
      expect(await calendarPage.getStartTimeValue()).toBe('23:50');
      expect(await calendarPage.getEndTimeValue()).toBe('23:00');

      await calendarPage.closeEventModal();
    });

    test('A8: typed value matching a preset still preserves duration on Tab', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await calendarPage.openCreateModalAt9AM();

      // Default 09:00 → 10:00 (1h). selectOnTab=true means typing "09:30"
      // and pressing Tab will select the matching preset rather than
      // calling addTimeTag — the duration-preserve subscription must still
      // fire so end → 10:30.
      await calendarPage.typeStartTime('09:30', 'Tab');

      expect(await calendarPage.getStartTimeValue()).toBe('09:30');
      expect(await calendarPage.getEndTimeValue()).toBe('10:30');

      await calendarPage.closeEventModal();
    });
  });

  // =======================================================================
  // B. Mini-calendar — week numbers + minDate (4 tests)
  // =======================================================================
  test.describe('Mini-calendar — week numbers + minDate', () => {
    test('B1: event-modal date popup shows "Uge" header + 6 numeric week cells', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await calendarPage.openCreateModalAt9AM();
      await calendarPage.openEventDatePicker();

      // Overlay is portaled out of the modal — must use page-scope.
      await expect(calendarPage.getMiniPickerOverlay()).toBeVisible();

      // Week-header cell present and not empty (locale-translated, "Uge"
      // in Danish, "Wk" in English — content varies, just assert non-empty).
      const headerLocator = page.locator('.mini-picker-overlay-card .cal-grid .week-header');
      await expect(headerLocator).toHaveCount(1);
      const headerText = ((await headerLocator.textContent()) ?? '').trim();
      expect(headerText.length).toBeGreaterThan(0);

      await expect(page.locator('.mini-picker-overlay-card .week-num-cell')).toHaveCount(6);

      const weekNums = await calendarPage.getMiniCalendarWeekNumbers();
      expect(weekNums).toHaveLength(6);
      for (const n of weekNums) {
        expect(Number.isInteger(n)).toBe(true);
        expect(n).toBeGreaterThanOrEqual(1);
        expect(n).toBeLessThanOrEqual(53);
      }

      await calendarPage.closeEventModal();
    });

    test('B2: week numbers are ISO 8601 correct after navigating +2 months', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await calendarPage.openCreateModalAt9AM();
      await calendarPage.openEventDatePicker();

      // The mini-calendar starts on dateControl.value's month — that's the
      // slot we clicked, which is Monday of next week (openCreateModalAt9AM
      // advances the calendar one week then clicks the leftmost slot).
      // We then press chevron_right twice to land in (slot's month + 2).
      const beforeLabel = await calendarPage.getMiniCalendarMonthLabel();
      expect(beforeLabel.length).toBeGreaterThan(0);

      const slotDate = mondayOfNextWeek();
      const target = addMonths(slotDate, 2);
      const expected = expectedWeeksForMonth(target.getFullYear(), target.getMonth());

      await calendarPage.clickMiniCalendarNext();
      await calendarPage.clickMiniCalendarNext();

      const afterLabel = await calendarPage.getMiniCalendarMonthLabel();
      expect(afterLabel.length).toBeGreaterThan(0);
      expect(afterLabel).not.toBe(beforeLabel); // moved at least one month

      const actual = await calendarPage.getMiniCalendarWeekNumbers();
      if (JSON.stringify(actual) !== JSON.stringify(expected)) {
        // Diagnostic: the picker may have started on a different month
        // than (today+7) due to locale/timezone edges; surface candidates.
        const exp1 = expectedWeeksForMonth(addMonths(target, -1).getFullYear(), addMonths(target, -1).getMonth());
        const exp2 = expectedWeeksForMonth(addMonths(target, +1).getFullYear(), addMonths(target, +1).getMonth());
        console.log(
          `B2 mismatch — beforeLabel="${beforeLabel}", afterLabel="${afterLabel}", ` +
          `actual=${JSON.stringify(actual)}, expected(target)=${JSON.stringify(expected)}, ` +
          `prev=${JSON.stringify(exp1)}, next=${JSON.stringify(exp2)}`
        );
      }
      expect(actual).toEqual(expected);

      await calendarPage.closeEventModal();
    });

    test('B3: custom-repeat dialog shows week numbers and selecting a date closes the popup', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await calendarPage.openCreateModalAt9AM();

      // Open the Repeat mtx-select and choose "Custom…". The repeat select
      // is the 3rd mtx-select in the modal (after start/end time selects),
      // but we identify it via its sync icon row to keep the locator robust.
      // Scope to .gcal-icon to exclude any decorative icons that aren't row markers.
      const repeatRow = page.locator('.gcal-row').filter({ has: page.locator('mat-icon.gcal-icon:has-text("sync")') });
      // The repeat select is [searchable]="false" — the inner combobox input
      // is non-interactive; click the .ng-select-container instead.
      await repeatRow.locator('.ng-select-container').first().click();
      await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
      // Custom is always the LAST repeat option (per calendar-repeat.service
      // buildRepeatOptions). Picking by index avoids depending on the
      // translated label, which varies with the test environment locale.
      await page.locator('.ng-dropdown-panel .ng-option').last().click();

      // Custom-repeat dialog should now be open.
      await page
        .locator('.custom-repeat-dialog')
        .waitFor({ state: 'visible', timeout: 10000 });

      // Click the "On" radio so the date input becomes enabled.
      await page
        .locator('.custom-repeat-dialog .end-option')
        .filter({ has: page.locator('mat-radio-button[value="until"]') })
        .locator('mat-radio-button')
        .click();
      await page.waitForTimeout(300);

      await calendarPage.openCustomRepeatDatePicker();

      await expect(page.locator('.mini-picker-overlay-card .week-num-cell')).toHaveCount(6);

      // Click a current-month day cell (one that's not .other-month and
      // not .disabled) — pick the LAST such cell so we stay inside the
      // current month even near month edges.
      const currentMonthCells = page.locator(
        '.mini-picker-overlay-card .day-cell:not(.other-month):not(.disabled)'
      );
      const currentCount = await currentMonthCells.count();
      expect(currentCount).toBeGreaterThan(0);
      const target = currentMonthCells.nth(Math.min(currentCount - 1, 14));
      const dayText = ((await target.textContent()) ?? '').trim();
      await target.click();

      // Popup should close (cdkConnectedOverlay teardown is async — give it
      // up to 2s rather than asserting immediately).
      await expect(page.locator('.mini-picker-overlay-card')).toHaveCount(0, { timeout: 2000 });

      // Date input value should be a non-empty long-format string and
      // contain the day number we just clicked.
      const dateInputValue = await page
        .locator('.custom-repeat-dialog .end-option .date-input input')
        .inputValue();
      expect(dateInputValue.length).toBeGreaterThan(0);
      expect(dateInputValue).toContain(dayText);

      // Cancel out of the custom-repeat dialog and the event modal.
      await page.locator('.custom-repeat-dialog .btn-cancel-gcal').click();
      await page.waitForTimeout(300);
      await calendarPage.closeEventModal();
    });

    test('B4: minDate disables past-month days and clicks are no-ops', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await calendarPage.openCreateModalAt9AM();

      // Read the current Date input value so we can assert it doesn't change.
      const dateInputBefore = await page
        .locator('.gcal-date-field input[matInput]')
        .first()
        .inputValue();

      await calendarPage.openEventDatePicker();
      // Step back one month so the previous month becomes visible (its
      // days carry .other-month + .disabled when minDate=today).
      await calendarPage.clickMiniCalendarPrev();

      const disabledCell = page
        .locator('.mini-picker-overlay-card .day-cell.disabled')
        .first();
      // There should be at least one disabled cell visible.
      await expect(disabledCell).toBeVisible();

      await disabledCell.click({ force: true });
      // Popup remains visible (selectDate early-returns on isDisabled).
      await expect(calendarPage.getMiniPickerOverlay()).toBeVisible();

      // Date input value unchanged.
      const dateInputAfter = await page
        .locator('.gcal-date-field input[matInput]')
        .first()
        .inputValue();
      expect(dateInputAfter).toBe(dateInputBefore);

      await calendarPage.closeEventModal();
    });
  });

  // =======================================================================
  // C. Property pill clickability (2 tests)
  // =======================================================================
  test.describe('Property pill — sidebar shortcut', () => {
    test('C1: pill opens sidebar when closed', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);

      // Start from open (beforeEach guarantees it). Close via menu toggle.
      await calendarPage.clickMenuToggleButton();
      await expect(page.locator('.calendar-shell.sidebar-closed')).toHaveCount(1);

      // Click the pill — it's now a real <button>.
      await calendarPage.clickPropertyPill();
      // Immediate assertion (no waitForTimeout) — the click toggles
      // sidebarOpen synchronously in onPropertyPillClicked().
      await expect(page.locator('.calendar-shell.sidebar-closed')).toHaveCount(0);
    });

    test('C2: pill is no-op when sidebar already open', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);

      // beforeEach already calls ensureSidebarOpen, but be explicit.
      await calendarPage.ensureSidebarOpen();
      await expect(page.locator('.calendar-shell.sidebar-closed')).toHaveCount(0);

      await calendarPage.clickPropertyPill();
      // Still open — the pill click handler is a no-op when already open.
      await expect(page.locator('.calendar-shell.sidebar-closed')).toHaveCount(0);
    });
  });

  // =======================================================================
  // G. Sticky day-of-week header (regression for cd637cbd) — 2 tests
  // The mtx-grid host had `overflow: hidden` from @ng-matero/extensions,
  // which captured `position: sticky` on the header row and made it scroll
  // with content instead of staying pinned to the wrapper top. The SCSS fix
  // adds `&,` to the overflow override so the host becomes overflow:visible
  // and sticky resolves to the actual scrolling .week-grid-wrapper.
  // =======================================================================
  test.describe('Calendar — sticky day-of-week header', () => {
    test('G1: week view — header stays pinned at partial and max scroll', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);

      // Week is the default viewMode in calendar-container.component.ts; just
      // wait for the grid to render before measuring.
      await page.locator('app-calendar-week-grid').waitFor({ state: 'visible', timeout: 10000 });
      await page.waitForTimeout(500);

      await calendarPage.assertHeaderStaysSticky('partial');
      await calendarPage.assertHeaderStaysSticky('max');
    });

    test('G2: day view — header stays pinned at partial and max scroll', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);

      await calendarPage.switchToDayView();
      // Re-wait after the view switch — Angular re-instantiates the
      // <app-calendar-week-grid> when *ngSwitchCase flips from 'week' to 'day'.
      await page.locator('app-calendar-week-grid').waitFor({ state: 'visible', timeout: 10000 });
      await page.waitForTimeout(500);

      // Reset scrollTop in case the wrapper inherited a non-zero offset
      // from prior interactions in the same beforeEach session.
      await calendarPage
        .getWeekGridWrapper()
        .evaluate(el => { (el as HTMLElement).scrollTop = 0; });

      await calendarPage.assertHeaderStaysSticky('partial');
      await calendarPage.assertHeaderStaysSticky('max');
    });
  });

  // =======================================================================
  // H. View-switch preserves the navigated week (regression for the
  //    currentDate-reset removal). Switching from week → day or week →
  //    schedule used to snap currentDate back to today; it now snaps to
  //    Monday of the currently-viewed week. Schedule-view chevron also
  //    advances 7 days per click (was 1).
  // =======================================================================
  test.describe('Calendar — view-switch preserves navigated week', () => {
    // Test-side helpers (kept inline — test-only date math)
    // Local-tz Monday of the current week. Mirrors getMondayOfWeek in
    // calendar-container.component.ts so the tests reason in the same
    // timezone as the running app.
    function mondayOfThisWeekLocal(): Date {
      const d = new Date();
      d.setHours(0, 0, 0, 0);
      const dow = d.getDay(); // 0=Sun … 6=Sat
      const diff = dow === 0 ? -6 : 1 - dow;
      d.setDate(d.getDate() + diff);
      return d;
    }
    function addDays(d: Date, n: number): Date {
      const out = new Date(d);
      out.setDate(out.getDate() + n);
      return out;
    }
    // Mirror calendar-header.component.ts displayDate format for day/schedule views.
    // The Playwright runtime is da-DK (matches the dev environment).
    function formatLongDate(d: Date, locale = 'da-DK'): string {
      const formatted = d.toLocaleDateString(locale, {
        weekday: 'long', day: 'numeric', month: 'long', year: 'numeric',
      });
      return formatted.charAt(0).toUpperCase() + formatted.slice(1);
    }

    test('H1: schedule view preserves the navigated week (not today)', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await page.locator('app-calendar-week-grid').waitFor({ state: 'visible', timeout: 10000 });

      // Navigate two weeks forward in week view
      await calendarPage.navigateToNextWeek();
      await calendarPage.navigateToNextWeek();

      // Switch to schedule (Tidsplan)
      await calendarPage.switchToScheduleView();

      // Header should now show the Monday of (today's-week + 14d), in long format
      const expected = formatLongDate(addDays(mondayOfThisWeekLocal(), 14));
      const actual = await calendarPage.getCalendarHeaderDateText();
      expect(actual).toBe(expected);
    });

    test('H2: day view lands on Monday of the navigated week', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await page.locator('app-calendar-week-grid').waitFor({ state: 'visible', timeout: 10000 });

      await calendarPage.navigateToNextWeek();
      await calendarPage.navigateToNextWeek();

      await calendarPage.switchToDayView();

      const expectedMonday = addDays(mondayOfThisWeekLocal(), 14);
      const expectedHeader = formatLongDate(expectedMonday);
      const actualHeader = await calendarPage.getCalendarHeaderDateText();
      expect(actualHeader).toBe(expectedHeader);

      // The single day-grid column header should also show that Monday's date.
      // Selector skips the time-axis column. The grid label format is
      // "weekdayShort. dd/mm" (e.g. "man. 11/05"), so the dd/mm pair is a
      // tighter anchor than the bare day number — a neighbour-week off-by-one
      // would not collide.
      const dayCol = page
        .locator('app-calendar-week-grid .mat-mdc-header-cell:not(.mat-column-time-axis)')
        .first();
      const colText = (await dayCol.innerText()).trim();
      const dd = String(expectedMonday.getDate()).padStart(2, '0');
      const mm = String(expectedMonday.getMonth() + 1).padStart(2, '0');
      expect(colText).toContain(`${dd}/${mm}`);
    });

    test('H3: schedule chevron advances one week per click', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      await page.locator('app-calendar-week-grid').waitFor({ state: 'visible', timeout: 10000 });

      // Get into schedule view at this week's Monday (no navigation yet)
      await calendarPage.switchToScheduleView();
      const before = await calendarPage.getCalendarHeaderDateText();
      expect(before).toBe(formatLongDate(mondayOfThisWeekLocal()));

      // One chevron click should advance 7 days (NOT 1 day, post-fix)
      await calendarPage.navigateToNextWeek();

      const after = await calendarPage.getCalendarHeaderDateText();
      expect(after).toBe(formatLongDate(addDays(mondayOfThisWeekLocal(), 7)));
    });
  });

  // =======================================================================
  // I. Edit-mode reconstructs custom multi-day weekly rules. Regression
  //    coverage for Layer 3 of the calendar custom-repeat reconstruction
  //    feature: Layer 1 added RepeatWeekdaysCsv to AreaRulePlanning;
  //    Layer 2 surfaced it on the response DTO + accepted it on
  //    create/update; Layer 3 (this test) verifies the modal reconstructs
  //    a CalendarRepeatMeta from the persisted fields and lands on the
  //    synthesized 'customCurrent' option with the readable summary.
  // =======================================================================
  test.describe('Edit-mode meta reconstruction (custom multi-day weekly)', () => {
    test('I1: edit-mode reconstructs custom multi-day weekly', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const eventTitle = `I1-${generateRandmString(5)}`;

      // ----- Step 1: open create-modal at Monday slot -----------------
      // openCreateModalAt9AM advances one week then clicks Monday@9. The
      // create-modal's Repeat dropdown defaults to 'none' which is what
      // we want to override.
      await calendarPage.openCreateModalAt9AM();
      await page.locator('#calendarEventTitle').fill(eventTitle);
      // Defaults from the modal: first eForm and a board are auto-picked;
      // we don't set planningTag/assignee here because the suite's seeded
      // worker isn't strictly required to validate reconstruction.

      // ----- Step 2: open repeat dropdown → Tilpasset… ----------------
      // The repeat select is [searchable]="false", so click .ng-select-container.
      const repeatRow = page
        .locator('.gcal-row')
        .filter({ has: page.locator('mat-icon.gcal-icon:has-text("sync")') });
      await repeatRow.locator('.ng-select-container').first().click();
      await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
      // Custom is always the LAST repeat option per
      // calendar-repeat.service.buildRepeatSelectOptions ordering.
      await page.locator('.ng-dropdown-panel .ng-option').last().click();

      // ----- Step 3: configure custom rule -----------------------------
      // Custom-repeat dialog opens. Set step=2, unit defaults to 'week'.
      await page
        .locator('.custom-repeat-dialog')
        .waitFor({ state: 'visible', timeout: 10000 });
      const stepInput = page.locator('.custom-repeat-dialog .step-input input');
      await stepInput.fill('2');

      // Pick Mon+Wed+Fri. Monday is auto-active when opening fresh against
      // a Monday slot date — toggle Wednesday and Friday on, leave Monday on,
      // turn the rest off (Tuesday/Thursday should already be off, but be
      // defensive in case the modal reseeds them in the future).
      const dayCircles = page.locator('.custom-repeat-dialog .day-circle');
      // Order in the modal is Mon, Tue, Wed, Thu, Fri, Sat, Sun.
      // Ensure each circle's active state matches the desired set.
      const expectedActive = [true, false, true, false, true, false, false];
      for (let i = 0; i < 7; i++) {
        const circle = dayCircles.nth(i);
        const cls = (await circle.getAttribute('class')) ?? '';
        const isActive = cls.split(/\s+/).includes('active');
        if (isActive !== expectedActive[i]) {
          await circle.click();
        }
      }

      // End mode "Efter" + afterCount = 6.
      await page
        .locator('.custom-repeat-dialog .end-option')
        .filter({ has: page.locator('mat-radio-button[value="after"]') })
        .locator('mat-radio-button')
        .click();
      await page.waitForTimeout(200);
      await page.locator('.custom-repeat-dialog .count-input input').fill('6');

      // Færdig (Done) — closes the modal, syncs meta back to parent.
      await page.locator('.custom-repeat-dialog .btn-done-gcal').click();
      await page
        .locator('.custom-repeat-dialog')
        .waitFor({ state: 'detached', timeout: 5000 });

      // ----- Step 4: verify dropdown collapsed value -------------------
      // The `customCurrent` option should now render the formatted Danish
      // label "Hver 2. uge: mandag, onsdag og fredag".
      const dropdownValue = page
        .locator('.gcal-row')
        .filter({ has: page.locator('mat-icon.gcal-icon:has-text("sync")') })
        .locator('.ng-value-label')
        .first();
      await expect(dropdownValue).toHaveText('Hver 2. uge: mandag, onsdag og fredag');

      // ----- Step 5: save -----------------------------------------------
      const createWait = page.waitForResponse(
        r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
          && !r.url().includes('/tasks/week')
          && !r.url().includes('/tasks/move')
          && !r.url().includes('/tasks/resize')
          && r.request().method() === 'POST',
        { timeout: 30000 }
      );
      await page.locator('#calendarEventSaveBtn').click();
      await createWait;
      await page.waitForTimeout(1500);

      // ----- Step 6: full page reload ----------------------------------
      // Reload the calendar route directly so we exercise the GET-back path
      // (week tasks → mapper → DTO → frontend reconstruction).
      await calendarPage.goToCalendar();
      await calendarPage.ensureSidebarOpen();
      const folderResponsePromise = page.waitForResponse(
        r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
        { timeout: 60000 }
      );
      await calendarPage.selectProperty(property.name);
      await folderResponsePromise.catch(() => undefined);
      await page.waitForTimeout(1500);
      // The event was created on next-week's Monday; advance the view so
      // the seeded event is visible.
      await calendarPage.navigateToNextWeek();

      // ----- Step 7: open the seeded event in edit mode ----------------
      const block = page.locator('.task-block').filter({ hasText: eventTitle }).first();
      await block.waitFor({ state: 'visible', timeout: 10000 });
      await block.click();
      await calendarPage.getPreviewEditButton().waitFor({ state: 'visible', timeout: 10000 });
      await calendarPage.getPreviewEditButton().click();
      await page.locator('#calendarEventTitle').waitFor({ state: 'visible', timeout: 10000 });

      // ----- Step 8: assert reconstructed dropdown summary --------------
      const reopenedDropdownValue = page
        .locator('.gcal-row')
        .filter({ has: page.locator('mat-icon.gcal-icon:has-text("sync")') })
        .locator('.ng-value-label')
        .first();
      await expect(reopenedDropdownValue)
        .toHaveText('Hver 2. uge: mandag, onsdag og fredag');

      // ----- Step 9: open Tilpasset… and verify modal pre-population ---
      const repeatRow2 = page
        .locator('.gcal-row')
        .filter({ has: page.locator('mat-icon.gcal-icon:has-text("sync")') });
      await repeatRow2.locator('.ng-select-container').first().click();
      await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
      // Pick the actual 'custom' option (Tilpasset…) — last in the list.
      // Its meta is undefined in the option list; the click triggers
      // valueChanges → onRepeatChange → opens the dialog hydrated from
      // customRepeatMeta (which was set by reconstructMetaFromTask in ngOnInit).
      await page.locator('.ng-dropdown-panel .ng-option').last().click();

      await page
        .locator('.custom-repeat-dialog')
        .waitFor({ state: 'visible', timeout: 10000 });

      // Frequency should be 2.
      const stepInput2 = page.locator('.custom-repeat-dialog .step-input input');
      expect(await stepInput2.inputValue()).toBe('2');

      // Unit shows "uge" (Danish for week).
      const unitLabel = page
        .locator('.custom-repeat-dialog .unit-select .ng-value-label')
        .first();
      await expect(unitLabel).toHaveText('uge');

      // Mon (idx 0), Wed (idx 2), Fri (idx 4) active; others inactive.
      const dayCircles2 = page.locator('.custom-repeat-dialog .day-circle');
      const expectedActive2 = [true, false, true, false, true, false, false];
      for (let i = 0; i < 7; i++) {
        const cls = (await dayCircles2.nth(i).getAttribute('class')) ?? '';
        const isActive = cls.split(/\s+/).includes('active');
        expect(isActive, `weekday circle index=${i} expected active=${expectedActive2[i]}, got=${isActive}`)
          .toBe(expectedActive2[i]);
      }

      // End mode "Efter" radio is checked (mat-radio uses .mat-mdc-radio-checked
      // on the matching <mat-radio-button>).
      const afterOption = page
        .locator('.custom-repeat-dialog .end-option')
        .filter({ has: page.locator('mat-radio-button[value="after"]') });
      await expect(afterOption.locator('mat-radio-button.mat-mdc-radio-checked'))
        .toHaveCount(1);

      // afterCount field shows 6.
      const countInput = page.locator('.custom-repeat-dialog .count-input input');
      expect(await countInput.inputValue()).toBe('6');

      // Cancel out so we don't disturb the row on close.
      await page.locator('.custom-repeat-dialog .btn-cancel-gcal').click();
      await page
        .locator('.custom-repeat-dialog')
        .waitFor({ state: 'detached', timeout: 5000 });
      await calendarPage.closeEventModal();
    });
  });
});
