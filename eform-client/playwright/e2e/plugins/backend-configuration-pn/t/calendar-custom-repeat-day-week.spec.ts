import { test, expect, Page } from '@playwright/test';
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
 * Regression suite for the calendar's "Tilpasset…" (Custom) repeat dialog —
 * GitHub issue #898, focused on the DAY and WEEK scheduling units.
 *
 * SCOPE
 * -----
 * Where calendar-repeat-presets.spec.ts (#888) covers the BUILT-IN dropdown
 * presets, this suite drives the custom-repeat dialog itself: open it via the
 * repeat row's last ng-option ("Tilpasset…"), configure step / unit / weekday
 * circles / end mode, click Færdig (Done), and then assert the MATERIALISED
 * occurrences land on the correct day-of-week columns of the grid — across
 * the seed week AND the next week. This goes further than the existing I1
 * test (calendar-ui-enhancements.spec.ts), which only asserts the collapsed
 * dropdown label + dialog re-population; here we verify the recurrence engine
 * actually paints blocks on the expected columns.
 *
 * MATRIX (CR01–CR09)
 * ------------------
 *   CR01  day,  step=1                 → daily        (every day)
 *   CR02  day,  step=2                 → everyNd      (Mon, +2=Wed, +4=Fri, +6=Sun)
 *   CR03  week, 0 weekdays, step=1     → everyNWeekAll (ALL 7 days — the quirk)
 *   CR04  week, 0 weekdays, step=2     → everyNWeekAll every 2nd week
 *   CR05  week, Mon, step=1            → weeklyOne    (Monday only)
 *   CR06  week, Mon, step=2            → everyNWeekOne (Monday, every 2nd week)
 *   CR07  week, Mon/Wed/Fri, step=1    → weeklyMulti  (days 0,2,4)
 *   CR08  ── covered by I1 (week, Mon/Wed/Fri, step=2, end=after 6) — NOT
 *            duplicated here. See calendar-ui-enhancements.spec.ts.
 *   CR09  week, ALL 7 weekdays, step=1 → weeklyMulti  (all 7 — "Weekly on all days")
 *
 * The (step, unit, weekdays) → meta.kind mapping is fixed in
 * calendar-repeat.service.buildMetaFromCustomConfig:
 *   unit=day  → step===1 ? 'daily' : 'everyNd'
 *   unit=week, 0 days → 'everyNWeekAll' (regardless of step — the CR03/CR04 quirk)
 *   unit=week, 1 day  → step===1 ? 'weeklyOne'  : 'everyNWeekOne'
 *   unit=week, ≥2 days→ step===1 ? 'weeklyMulti' : 'everyNWeekMulti'
 * The per-week cadence math is exhaustively covered by
 * calendar-repeat.service.spec unit tests; from this black-box angle we only
 * assert day-of-week column presence/absence.
 *
 * MODEL
 * -----
 * `openCreateModalAtSlot(0, hour)` advances the calendar one week and clicks
 * Monday@hour, so every event anchors on the Monday of the displayed (next)
 * week. `getDayColumnTaskBlocks(dayIndex, title)` reads per-day `.task-block`s
 * (Mon=0..Sun=6), filtered by the unique title so accumulation across tests
 * never corrupts counts.
 *
 * DISTINCT HOURS
 * --------------
 * The daily (CR01) and all-days (CR03/CR04/CR09) tests paint a block on
 * Monday too. So every test clicks a DIFFERENT Monday hour to land on an
 * empty slot (the create modal only opens on an empty slot):
 *   CR01=9, CR02=10, CR03=11, CR04=12, CR05=13, CR06=14, CR07=15, CR09=16.
 *
 * All these rules use endMode='never', so a weekly/every-N-week cadence
 * recurs into the following week(s) and the next-week assertions are stable.
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

test.describe.serial('Calendar custom repeat — day & week scheduling (#898)', () => {
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
  // Shared helpers
  // =======================================================================

  /** Fill the required create-modal fields (title + first eForm + first
   *  planning tag + first assignee). Mirrors the field-fill pattern in
   *  calendar-repeat-presets.spec.ts / I1. */
  async function fillRequiredFields(page: Page, title: string): Promise<void> {
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
  }

  /** Open the repeat dropdown and pick the LAST option = "Tilpasset…" (custom),
   *  which opens the custom-repeat dialog. The repeat select is
   *  [searchable]="false", so click .ng-select-container directly. */
  async function openCustomRepeatDialog(page: Page): Promise<void> {
    const repeatRow = page
      .locator('.gcal-row')
      .filter({ has: page.locator('mat-icon.gcal-icon:has-text("sync")') });
    await repeatRow.locator('.ng-select-container').first().click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    // Custom is always the LAST repeat option per buildRepeatSelectOptions.
    await page.locator('.ng-dropdown-panel .ng-option').last().click();
    await page
      .locator('.custom-repeat-dialog')
      .waitFor({ state: 'visible', timeout: 10000 });
  }

  /** Set the custom-repeat unit select ('day' | 'week'). unitOptions order is
   *  fixed in custom-repeat-modal.component ngOnInit: 0=day, 1=week, 2=month,
   *  3=year. Positional .nth() picking is locale-independent. The select is
   *  [searchable]="false" → click .ng-select-container. */
  async function setCustomUnit(page: Page, unit: 'day' | 'week'): Promise<void> {
    const indexByUnit: Record<string, number> = { day: 0, week: 1 };
    await page
      .locator('.custom-repeat-dialog .unit-select .ng-select-container')
      .first()
      .click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').nth(indexByUnit[unit]).click();
    await page.waitForTimeout(300);
  }

  /** Set the step ("Repeat every N") number input. */
  async function setCustomStep(page: Page, step: number): Promise<void> {
    await page.locator('.custom-repeat-dialog .step-input input').fill(String(step));
    await page.waitForTimeout(150);
  }

  /** Toggle the weekday circles to EXACTLY match the desired active set.
   *  Circle order is Mon..Sun = idx 0..6 (matching getDayColumnTaskBlocks).
   *  "active" in the class list = selected. Only meaningful in week mode (the
   *  weekday-row renders only when unit==='week'). */
  async function setActiveWeekdays(page: Page, active: boolean[]): Promise<void> {
    const circles = page.locator('.custom-repeat-dialog .day-circle');
    await circles.first().waitFor({ state: 'visible', timeout: 5000 });
    for (let i = 0; i < 7; i++) {
      const circle = circles.nth(i);
      const cls = (await circle.getAttribute('class')) ?? '';
      const isActive = cls.split(/\s+/).includes('active');
      if (isActive !== active[i]) {
        await circle.click();
        await page.waitForTimeout(100);
      }
    }
  }

  /** Click Færdig (Done) and wait for the dialog to detach. */
  async function clickDone(page: Page): Promise<void> {
    await page.locator('.custom-repeat-dialog .btn-done-gcal').click();
    await page
      .locator('.custom-repeat-dialog')
      .waitFor({ state: 'detached', timeout: 5000 });
  }

  /** Read the collapsed repeat-row label (the customCurrent ng-value-label). */
  function repeatRowLabel(page: Page) {
    return page
      .locator('.gcal-row')
      .filter({ has: page.locator('mat-icon.gcal-icon:has-text("sync")') })
      .locator('.ng-value-label')
      .first();
  }

  /** Save the create modal and await the create POST (excluding
   *  week/move/resize traffic), then settle for the post-save reload +
   *  re-render. Mirrors calendar-repeat-presets.spec.ts. */
  async function saveAndAwaitCreate(page: Page): Promise<void> {
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
    await page.waitForTimeout(2000);
  }

  /** Assert a block IS present on the given day-of-week column for `title`. */
  async function expectPresent(
    calendarPage: CalendarUiEnhancementsPage,
    day: number,
    title: string,
    weekLabel: string,
  ): Promise<void> {
    const count = await calendarPage.getDayColumnTaskBlocks(day, title).count();
    expect(
      count,
      `Expected block on day-of-week index ${day} (Mon=0..Sun=6) of ${weekLabel} ` +
      `for "${title}", but found ${count}.`
    ).toBeGreaterThanOrEqual(1);
  }

  /** Assert NO block on the given day-of-week column for `title`. */
  async function expectAbsent(
    calendarPage: CalendarUiEnhancementsPage,
    day: number,
    title: string,
    weekLabel: string,
  ): Promise<void> {
    const count = await calendarPage.getDayColumnTaskBlocks(day, title).count();
    expect(
      count,
      `Expected NO block on day-of-week index ${day} (Mon=0..Sun=6) of ${weekLabel} ` +
      `for "${title}", but found ${count}.`
    ).toBe(0);
  }

  // =======================================================================
  // CR01 — day, step=1 → daily: an occurrence on EVERY day.
  // =======================================================================
  test('CR01 — custom day step=1 (daily) materialises on every day of the seed & next week', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR01-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 9);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'day');
    await setCustomStep(page, 1);
    await clickDone(page);

    await saveAndAwaitCreate(page);

    // Seed week: every day Mon..Sun carries the series.
    for (let day = 0; day <= 6; day++) {
      await expectPresent(calendarPage, day, title, 'the seed week');
    }
    // Next week: daily recurrence continues.
    await calendarPage.navigateToNextWeek();
    for (let day = 0; day <= 6; day++) {
      await expectPresent(calendarPage, day, title, 'the week after the seed week');
    }
  });

  // =======================================================================
  // CR02 — day, step=2 → everyNd: Mon + every 2 days = Wed, Fri, Sun.
  //   Monday is the start; +2=Wed (2), +4=Fri (4), +6=Sun (6). Tue (1) and
  //   Thu (3) must be empty in the seed week.
  // =======================================================================
  test('CR02 — custom day step=2 (everyNd) lands on Mon/Wed/Fri/Sun, skips Tue/Thu', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR02-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 10);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'day');
    await setCustomStep(page, 2);
    await clickDone(page);

    await saveAndAwaitCreate(page);

    // Seed week: Mon (0), Wed (2), Fri (4), Sun (6) present; Tue (1), Thu (3) absent.
    await expectPresent(calendarPage, 0, title, 'the seed week');
    await expectAbsent(calendarPage, 1, title, 'the seed week');
    await expectPresent(calendarPage, 2, title, 'the seed week');
    await expectAbsent(calendarPage, 3, title, 'the seed week');
    await expectPresent(calendarPage, 4, title, 'the seed week');
    await expectPresent(calendarPage, 6, title, 'the seed week');
  });

  // =======================================================================
  // CR03 — week, 0 weekdays, step=1 → everyNWeekAll QUIRK.
  //   buildMetaFromCustomConfig maps an empty-weekday week config to
  //   'everyNWeekAll' regardless of step — i.e. it expands to ALL 7 days, NOT
  //   a Monday-only series. Deselect every circle (incl. the auto-Monday) and
  //   assert all 7 days are present in BOTH weeks. Explicitly assert it is not
  //   collapsed to a single weekday.
  // =======================================================================
  // #922 FIXED: the frontend builds kind=everyNWeekAll but previously shipped
  // repeatWeekdaysCsv=null, which the backend rendered as start-weekday-only.
  // metaToWeekdaysCsv now emits the full "0,1,2,3,4,5,6" CSV for the all-days
  // weekly kinds, so the backend expands all 7 days.
  test('CR03 — custom week step=1 with zero weekdays expands to ALL 7 days (everyNWeekAll quirk)', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR03-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 11);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    // Unit defaults to 'week'; be explicit for clarity.
    await setCustomUnit(page, 'week');
    await setCustomStep(page, 1);
    // Deselect ALL day-circles (fresh open pre-selects Monday).
    await setActiveWeekdays(page, [false, false, false, false, false, false, false]);
    await clickDone(page);

    // Collapsed label should read "Weekly on all days" (da: "Ugentlig på alle
    // dage") — asserted loosely since the run locale is Danish.
    await expect(repeatRowLabel(page)).toHaveText(/alle dage|all days/i);

    await saveAndAwaitCreate(page);

    // Seed week: ALL 7 days present — NOT Monday-only.
    for (let day = 0; day <= 6; day++) {
      await expectPresent(calendarPage, day, title, 'the seed week');
    }
    // Explicitly assert this is not a single-weekday series: at least one
    // non-Monday day carries a block.
    const tueCount = await calendarPage.getDayColumnTaskBlocks(1, title).count();
    expect(
      tueCount,
      `everyNWeekAll quirk: a zero-weekday week config must expand to all days, ` +
      `so Tuesday (day 1) must carry a block — found ${tueCount}.`
    ).toBeGreaterThanOrEqual(1);

    // Next week (step=1 → every week): all 7 days present again.
    await calendarPage.navigateToNextWeek();
    for (let day = 0; day <= 6; day++) {
      await expectPresent(calendarPage, day, title, 'the week after the seed week');
    }
  });

  // =======================================================================
  // CR04 — week, 0 weekdays, step=2 → everyNWeekAll every 2nd week.
  //   All 7 days present in the seed week, ABSENT in the +1 week, present
  //   again in the +2 week.
  // =======================================================================
  // #922 FIXED: same all-days CSV fix as CR03 — the every-2nd-week cadence now
  // expands all 7 days on the active weeks.
  test('CR04 — custom week step=2 with zero weekdays repeats all 7 days every 2nd week', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR04-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 12);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'week');
    await setCustomStep(page, 2);
    await setActiveWeekdays(page, [false, false, false, false, false, false, false]);
    await clickDone(page);

    await saveAndAwaitCreate(page);

    // Seed week: all 7 days present.
    for (let day = 0; day <= 6; day++) {
      await expectPresent(calendarPage, day, title, 'the seed week');
    }
    // +1 week: every-2nd-week cadence skips this week — all days absent.
    await calendarPage.navigateToNextWeek();
    for (let day = 0; day <= 6; day++) {
      await expectAbsent(calendarPage, day, title, 'the +1 week (skipped by every-2nd-week)');
    }
    // +2 week: cadence resumes — all 7 days present again.
    await calendarPage.navigateToNextWeek();
    for (let day = 0; day <= 6; day++) {
      await expectPresent(calendarPage, day, title, 'the +2 week');
    }
  });

  // =======================================================================
  // CR05 — week, Mon, step=1 → weeklyOne: Monday only; recurs next week.
  // =======================================================================
  test('CR05 — custom week step=1 with Monday only (weeklyOne) lands on Monday each week', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR05-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 13);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'week');
    await setCustomStep(page, 1);
    // Monday only (fresh open already pre-selects Monday — be explicit).
    await setActiveWeekdays(page, [true, false, false, false, false, false, false]);
    await clickDone(page);

    await saveAndAwaitCreate(page);

    // Seed week: Monday present, Tue..Sun absent.
    await expectPresent(calendarPage, 0, title, 'the seed week');
    for (let day = 1; day <= 6; day++) {
      await expectAbsent(calendarPage, day, title, 'the seed week');
    }
    // Next week: Monday present again, rest absent.
    await calendarPage.navigateToNextWeek();
    await expectPresent(calendarPage, 0, title, 'the week after the seed week');
    for (let day = 1; day <= 6; day++) {
      await expectAbsent(calendarPage, day, title, 'the week after the seed week');
    }
  });

  // =======================================================================
  // CR06 — week, Mon, step=2 → everyNWeekOne: Monday, every 2nd week.
  //   Monday present seed week, ABSENT +1 week, present again +2 week.
  // =======================================================================
  test('CR06 — custom week step=2 with Monday only (everyNWeekOne) lands on Monday every 2nd week', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR06-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 14);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'week');
    await setCustomStep(page, 2);
    await setActiveWeekdays(page, [true, false, false, false, false, false, false]);
    await clickDone(page);

    await saveAndAwaitCreate(page);

    // Seed week: Monday present, rest absent.
    await expectPresent(calendarPage, 0, title, 'the seed week');
    for (let day = 1; day <= 6; day++) {
      await expectAbsent(calendarPage, day, title, 'the seed week');
    }
    // +1 week: every-2nd-week cadence skips Monday too — nothing present.
    await calendarPage.navigateToNextWeek();
    for (let day = 0; day <= 6; day++) {
      await expectAbsent(calendarPage, day, title, 'the +1 week (skipped by every-2nd-week)');
    }
    // +2 week: Monday present again, rest absent.
    await calendarPage.navigateToNextWeek();
    await expectPresent(calendarPage, 0, title, 'the +2 week');
    for (let day = 1; day <= 6; day++) {
      await expectAbsent(calendarPage, day, title, 'the +2 week');
    }
  });

  // =======================================================================
  // CR07 — week, Mon/Wed/Fri, step=1 → weeklyMulti: days 0,2,4 present;
  //   1,3,5,6 absent; same pattern next week.
  // =======================================================================
  test('CR07 — custom week step=1 with Mon/Wed/Fri (weeklyMulti) lands on days 0,2,4 each week', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR07-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 15);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'week');
    await setCustomStep(page, 1);
    // Mon (0), Wed (2), Fri (4) active; rest off.
    await setActiveWeekdays(page, [true, false, true, false, true, false, false]);
    await clickDone(page);

    await saveAndAwaitCreate(page);

    const presentDays = [0, 2, 4];
    const absentDays = [1, 3, 5, 6];

    // Seed week.
    for (const day of presentDays) {
      await expectPresent(calendarPage, day, title, 'the seed week');
    }
    for (const day of absentDays) {
      await expectAbsent(calendarPage, day, title, 'the seed week');
    }
    // Next week: same Mon/Wed/Fri pattern.
    await calendarPage.navigateToNextWeek();
    for (const day of presentDays) {
      await expectPresent(calendarPage, day, title, 'the week after the seed week');
    }
    for (const day of absentDays) {
      await expectAbsent(calendarPage, day, title, 'the week after the seed week');
    }
  });

  // -----------------------------------------------------------------------
  // CR08 — week, Mon/Wed/Fri, step=2, end=after 6 (everyNWeekMulti).
  //   ALREADY COVERED by I1 ("edit-mode reconstructs custom multi-day weekly")
  //   in calendar-ui-enhancements.spec.ts: it creates exactly this rule and
  //   verifies the collapsed label + edit-mode dialog re-population. Not
  //   duplicated here.
  // -----------------------------------------------------------------------

  // =======================================================================
  // CR09 — week, ALL 7 weekdays, step=1 → weeklyMulti (length 7): all 7 days
  //   present; collapsed label collapses to "Weekly on all days"
  //   (da: "Ugentlig på alle dage") via the sortedDays.length===7 branch.
  // =======================================================================
  test('CR09 — custom week step=1 with all 7 weekdays (weeklyMulti) lands on every day; label "Weekly on all days"', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR09-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 16);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'week');
    await setCustomStep(page, 1);
    // All 7 weekdays active.
    await setActiveWeekdays(page, [true, true, true, true, true, true, true]);
    await clickDone(page);

    // Collapsed label collapses to the all-days form (locale-dependent —
    // asserted loosely).
    await expect(repeatRowLabel(page)).toHaveText(/alle dage|all days/i);

    await saveAndAwaitCreate(page);

    // Seed week: all 7 days present.
    for (let day = 0; day <= 6; day++) {
      await expectPresent(calendarPage, day, title, 'the seed week');
    }
    // Next week: weekly (step=1) → all 7 days again.
    await calendarPage.navigateToNextWeek();
    for (let day = 0; day <= 6; day++) {
      await expectPresent(calendarPage, day, title, 'the week after the seed week');
    }
  });
});
