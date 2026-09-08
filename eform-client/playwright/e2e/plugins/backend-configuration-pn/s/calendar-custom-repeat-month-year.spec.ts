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
 * GitHub issue #899, focused on the MONTH and YEAR scheduling units (and the
 * quirks they ship on the wire).
 *
 * SCOPE
 * -----
 * Where calendar-custom-repeat-day-week.spec.ts (#898) drives the DAY/WEEK
 * units and asserts the materialised grid columns, this suite drives the
 * MONTH and YEAR units and instead asserts the CREATE POST REQUEST BODY (the
 * wire payload), because:
 *
 *  - SUBSEQUENT MONTH/YEAR occurrences require multi-month / multi-year
 *    navigation that is brittle in a week-grid e2e; the per-occurrence
 *    expansion math is exhaustively covered by calendar-repeat.service.spec
 *    unit tests and by the C# enumerator fixtures. So here we black-box what
 *    the frontend puts on the wire + the collapsed dropdown label, plus (for
 *    CR12b/CR13b/CR32) the FIRST occurrence's render in the anchored week.
 *
 *    NOTE (#1207): the old premise that "MONTH occurrences land on the
 *    1st-of-month" no longer holds. The start date is now occurrence #1 for
 *    every Month rule; the pattern governs #2 onward. A monthly-Nth-weekday
 *    series whose start date does not itself satisfy the rule renders on its
 *    start date and only THEN follows the pattern — which is exactly what
 *    CR32 pins.
 *
 *  - The dialog's MONTH unit has TWO sub-types ("Gentagelsestype"):
 *      * "Månedligt dag" (everyNMonthDom) — a 1..28 day-of-month picker
 *        (custom-repeat-modal.component.ts:38-39). It DEFAULTS to 1 and
 *        CR10/CR11 leave that default alone, so their payloads carry
 *        dayOfMonth=1 even though the event is anchored on a Monday whose
 *        calendar day-of-month is not 1. That is the default, NOT a
 *        hard-coded constant: the earlier claim in this header that the MONTH
 *        option "ALWAYS ships dayOfMonth=1 — it has no day-of-month control"
 *        was already false when it was written.
 *      * "Månedligt på den første" (monthlyFirstWeekday) — an Nth-weekday
 *        rule; ships dayOfMonth=0 and repeatOrdinalWeek=1 instead. CR32.
 *
 *  - The custom dialog's YEAR option anchors the rule on the SELECTED start
 *    date: buildMetaFromCustomConfig's year branch sets dom = date.getDate()
 *    and month = date.getMonth() (#933). It does NOT hard-code 1 January any
 *    more — the "ALWAYS ships dayOfMonth=1 and month=0" claim that stood here
 *    predates #933 and contradicts CR12/CR13's own assertions below. The
 *    January fallback survives only for a missing date argument, which the
 *    modal never passes. Note the month index is NOT a separate wire field —
 *    the create payload carries repeatType/repeatEvery/dayOfMonth but no month
 *    index (verified in task-create-edit-modal.component.ts buildPayload). The
 *    yearly start month is implied by startDate (the anchored Monday's month),
 *    and the local month index only drives the collapsed label.
 *
 * YEAR RENDERING (#922 FIXED)
 * ---------------------------
 * GetOccurrencesInWeek already had a Year branch, but the task wizard only
 * captured DayOfMonth for Month — so a yearly event defaulted to DayOfMonth=1
 * and the Year branch landed on the 1st (wrong week), appearing not to render.
 * The wizard now captures DayOfMonth from the start date for Year too, so
 * yearly events paint on their anchored day. CR12/CR13 assert the wire payload
 * + label; CR12b/CR13b assert the initial render + absence of weekly recurrence
 * (the multi-year cadence is covered server-side, not via week-grid navigation).
 *
 * MATRIX (CR10–CR13, CR32)
 * ------------------------
 *   CR10  month, step=1 (monthlyDom)    → repeatType=3, repeatEvery=1, dayOfMonth=1
 *   CR11  month, step=3 (everyNMonthDom)→ repeatType=3, repeatEvery=3, dayOfMonth=1
 *   CR12  year,  step=1 (yearlyOne)     → repeatType=4, repeatEvery=1, dayOfMonth from start date
 *   CR13  year,  step=2 (everyNYear)    → repeatType=4, repeatEvery=2, dayOfMonth from start date
 *   CR32  month, step=12, sub-type "Månedligt på den første" + a weekday that
 *         is not the anchor's own (everyNMonthFirstWeekday)
 *                                       → repeatType=3, repeatEvery=12,
 *                                         repeatOrdinalWeek=1, dayOfMonth=0,
 *                                         and the start week paints (#1207)
 *
 * The (step, unit) → meta.kind mapping is fixed in
 * calendar-repeat.service.buildMetaFromCustomConfig:
 *   unit=month → monthlyKind 'everyNMonthDom'       : step===1 ? 'monthlyDom' : 'everyNMonthDom'      (dom from the picker, default 1)
 *              → monthlyKind 'monthlyFirstWeekday'  : step===1 ? 'monthlyFirstWeekday' : 'everyNMonthFirstWeekday' (ordinalWeek 1, weekday from the picker)
 *   unit=year  → step===1 ? 'yearlyOne'  : 'everyNYear'      (dom/month from the start date, #933)
 * and the meta.kind → wire mapping in task-create-edit-modal.buildPayload
 * (kindMap + the metaTo* helpers in calendar-repeat.service):
 *   monthlyDom / everyNMonthDom / monthlyFirstWeekday /
 *     everyNMonthFirstWeekday                    → repeatType 3
 *   yearlyOne  / everyNYear                      → repeatType 4
 *   dayOfMonth        = metaToDayOfMonth(meta)
 *     · monthlyDom / everyNMonthDom  → meta.dom, i.e. the 1–28 picker value
 *       (default 1 — CR10/CR11 leave it alone, hence dayOfMonth=1)
 *     · yearlyOne / everyNYear       → meta.dom = the start date's day (#933)
 *     · monthlyFirstWeekday / everyNMonthFirstWeekday → 0, the backend's
 *       "no day-of-month" sentinel (CR32)
 *     · anything outside 1–31 (and every other kind) → null
 *   repeatWeekdaysCsv = metaToWeekdaysCsv(meta)
 *     · monthlyDom / everyNMonthDom / yearlyOne / everyNYear → null
 *       (a by-DOM or yearly rule carries no weekday info)
 *     · monthlyFirstWeekday / everyNMonthFirstWeekday → the single picked
 *       weekday as a JS getDay() index string (CR32: Wednesday → '3')
 *   repeatOrdinalWeek = metaToRepeatOrdinalWeek(meta)
 *     · monthlyDom / everyNMonthDom / yearlyOne / everyNYear → null
 *       (not a by-day rule)
 *     · monthlyFirstWeekday / everyNMonthFirstWeekday → meta.ordinalWeek,
 *       which buildMetaFromCustomConfig always sets to 1 (CR32)
 *   There is no month-index field on the wire for any kind.
 *
 * MODEL
 * -----
 * `openCreateModalAtSlot(0, hour)` advances the calendar one week and clicks
 * Monday@hour, so every event anchors on the Monday of the displayed (next)
 * week — a date whose day-of-month is deliberately NOT necessarily 1, which is
 * the whole point of the CR10/CR11 dayOfMonth=1 assertion: the 1 comes from the
 * day-of-month picker's own default, never from the anchor's calendar day.
 *
 * WIRE CAPTURE
 * ------------
 * The create body is captured with page.waitForRequest (NOT waitForResponse),
 * filtered to the POST /calendar/tasks endpoint excluding the /tasks/week
 * read, then read via request.postDataJSON(). In a waitForRequest predicate
 * the method is r.method() (the arg is already a Request), NOT r.request().
 *
 * DISTINCT HOURS
 * --------------
 * Each test clicks a DIFFERENT Monday hour so the create modal always opens on
 * an empty slot (it only opens on an empty slot, and earlier rows leave a
 * Monday block behind):
 *   CR10=9, CR11=10, CR12=11, CR13=12, CR12b=13, CR13b=14, CR32=15.
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

test.describe.serial('Calendar custom repeat — month & year scheduling (#899)', () => {
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
  // Shared helpers (mirrors calendar-custom-repeat-day-week.spec.ts)
  // =======================================================================

  /** Fill the required create-modal fields (title + first eForm + first
   *  planning tag + first assignee). */
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

  /** Set the custom-repeat unit select. unitOptions order is fixed in
   *  custom-repeat-modal.component ngOnInit: 0=day, 1=week, 2=month, 3=year.
   *  Positional .nth() picking is locale-independent. The select is
   *  [searchable]="false" → click .ng-select-container. */
  async function setCustomUnit(page: Page, unit: 'day' | 'week' | 'month' | 'year'): Promise<void> {
    const indexByUnit: Record<string, number> = { day: 0, week: 1, month: 2, year: 3 };
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

  /**
   * Click Save, capture the create POST REQUEST BODY, and await its 200
   * response. Returns the parsed JSON wire payload.
   *
   * The body is captured with waitForRequest (the request, not the response,
   * carries postDataJSON). The predicate excludes the /tasks/week read and the
   * move/resize mutations and matches only the create POST on /calendar/tasks.
   * Inside a waitForRequest predicate the arg IS a Request, so the verb is
   * r.method() — NOT r.request().method().
   */
  async function saveAndCaptureCreateBody(page: Page): Promise<any> {
    const reqPromise = page.waitForRequest(
      r =>
        /\/calendar\/tasks$/.test(r.url()) &&
        !r.url().includes('/tasks/week') &&
        r.method() === 'POST',
      { timeout: 30000 }
    );
    const respPromise = page.waitForResponse(
      r =>
        r.url().includes('/api/backend-configuration-pn/calendar/tasks') &&
        !r.url().includes('/tasks/week') &&
        !r.url().includes('/tasks/move') &&
        !r.url().includes('/tasks/resize') &&
        r.request().method() === 'POST',
      { timeout: 30000 }
    );

    await page.locator('#calendarEventSaveBtn').click();

    const request = await reqPromise;
    const response = await respPromise;
    expect(
      response.status(),
      `Create POST /calendar/tasks must succeed (HTTP 200), got ${response.status()}`
    ).toBe(200);

    await page.waitForTimeout(1500);
    return request.postDataJSON();
  }

  // =======================================================================
  // CR10 — custom month, step=1 (monthlyDom).
  //   Anchored on a Monday whose calendar day-of-month is (almost certainly)
  //   NOT 1. The MONTH unit's "Månedligt dag" sub-type DOES have a day-of-month
  //   control — a 1–28 picker (custom-repeat-modal.component.ts:38-39) — but it
  //   defaults to 1 and this test never touches it, so meta.dom stays 1 and
  //   dayOfMonth=1 travels on the wire. Assert that default, plus repeatType=3,
  //   repeatEvery=1, and that it is NOT a weekly/ordinal rule
  //   (repeatWeekdaysCsv null/empty, repeatOrdinalWeek null/0).
  //
  //   Subsequent monthly occurrences need multi-month navigation and are covered
  //   by calendar-repeat.service.spec — so we assert the wire payload +
  //   successful creation only, not day-cell rendering.
  // =======================================================================
  test('CR10 — custom month step=1 (monthlyDom) wires repeatType=3, repeatEvery=1, dayOfMonth=1', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR10-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 9);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'month');
    await setCustomStep(page, 1);
    await clickDone(page);

    // Collapsed label: "Monthly on day 1" (da: "Månedligt på dag 1").
    await expect(repeatRowLabel(page)).toHaveText(/dag 1|day 1/i);

    const body = await saveAndCaptureCreateBody(page);

    expect(body.repeatType, 'monthly custom rule → repeatType 3').toBe(3);
    expect(body.repeatEvery, 'step=1 → repeatEvery 1').toBe(1);
    // dom stays at the picker's default: the event was created on a Monday
    // whose calendar day-of-month is not 1, but the "Månedligt dag" 1–28
    // picker was left untouched at 1, so dayOfMonth=1 is what ships.
    expect(
      body.dayOfMonth,
      'MONTH "Månedligt dag" picker left at its default → dayOfMonth 1'
    ).toBe(1);
    // Not a weekly rule and not an ordinal (Nth-weekday) rule.
    expect(body.repeatWeekdaysCsv ?? '', 'monthly-by-DOM rule ships no weekday CSV').toBe('');
    expect(body.repeatOrdinalWeek ?? 0, 'monthly-by-DOM rule is not an ordinal rule').toBe(0);
  });

  // =======================================================================
  // CR11 — custom month, step=3 (everyNMonthDom).
  //   Every 3rd month on day 1 — the day-of-month picker's default, untouched
  //   here exactly as in CR10. repeatType still 3, repeatEvery now 3.
  // =======================================================================
  test('CR11 — custom month step=3 (everyNMonthDom) wires repeatType=3, repeatEvery=3, dayOfMonth=1', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR11-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 10);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'month');
    await setCustomStep(page, 3);
    await clickDone(page);

    // Collapsed label: "Every 3 months on day 1" (da: "Hver 3. måned på dag 1").
    await expect(repeatRowLabel(page)).toHaveText(/dag 1|day 1/i);

    const body = await saveAndCaptureCreateBody(page);

    expect(body.repeatType, 'monthly custom rule → repeatType 3').toBe(3);
    expect(body.repeatEvery, 'step=3 → repeatEvery 3').toBe(3);
    expect(
      body.dayOfMonth,
      'MONTH "Månedligt dag" picker left at its default → dayOfMonth 1'
    ).toBe(1);
    expect(body.repeatWeekdaysCsv ?? '', 'monthly-by-DOM rule ships no weekday CSV').toBe('');
    expect(body.repeatOrdinalWeek ?? 0, 'monthly-by-DOM rule is not an ordinal rule').toBe(0);
  });

  // =======================================================================
  // CR12 — custom year, step=1 (yearlyOne).
  //   The YEAR unit anchors dayOfMonth AND the local month index on the
  //   selected start date (#933) — not on 1 January. There is NO separate month
  //   index wire field — the create payload carries
  //   repeatType/repeatEvery/dayOfMonth only, and the yearly start month is
  //   implied by startDate. Assert the wire payload + collapsed label +
  //   successful CREATION (POST 200) here.
  //
  //   The companion CR12b test asserts the RENDER: yearly events paint on their
  //   anchored day since #922 was fixed (the wizard now captures DayOfMonth from
  //   the start date for Year, so the Year branch no longer lands on the 1st).
  // =======================================================================
  test('CR12 — custom year step=1 (yearlyOne) wires repeatType=4, repeatEvery=1, dayOfMonth from the start date and is created', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR12-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 11);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'year');
    await setCustomStep(page, 1);
    await clickDone(page);

    // Collapsed label reflects a yearly rule on the selected start date (da:
    // "Årligt den <day>. <month>"). Asserted loosely since the run locale is
    // Danish and the month name is locale-dependent — anchor on the yearly
    // keyword + that a day number is shown (no longer hard-coded to 1, #933).
    await expect(repeatRowLabel(page)).toHaveText(/årligt|yearly/i);
    await expect(repeatRowLabel(page)).toHaveText(/\d{1,2}/);

    const body = await saveAndCaptureCreateBody(page);

    expect(body.repeatType, 'yearly custom rule → repeatType 4').toBe(4);
    expect(body.repeatEvery, 'step=1 → repeatEvery 1').toBe(1);
    // YEAR unit now anchors dayOfMonth to the selected start date (#933);
    // assert it matches the day-of-month of the same payload's start date.
    const expectedDom = Number(body.taskDate.slice(-2));
    expect(
      body.dayOfMonth,
      'YEAR custom rule anchors dayOfMonth to the selected start date (#933)'
    ).toBe(expectedDom);
    // No separate month index is sent on the wire (the local month index only
    // drives the label); assert the rule is neither weekly nor ordinal.
    expect(body.repeatWeekdaysCsv ?? '', 'yearly rule ships no weekday CSV').toBe('');
    expect(body.repeatOrdinalWeek ?? 0, 'yearly rule is not an ordinal rule').toBe(0);
  });

  // CR12b — yearly (repeatType=4) RENDERS in the week view (#922 FIXED).
  //   The task wizard now captures DayOfMonth from the start date for Year (it
  //   previously defaulted to 1, so the Year branch landed on the 1st / wrong
  //   week and never painted). The initial yearly occurrence now renders on its
  //   anchored day. The multi-year re-appearance cadence is covered server-side
  //   (GetOccurrencesInWeek's Year math); a 52-week chevron walk is too slow /
  //   flaky to assert in a week-grid e2e, so here we assert the initial render
  //   on the anchored column plus the absence of any weekly recurrence.
  // =======================================================================
  test('CR12b — yearly (repeatType=4) renders an occurrence in the calendar grid', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR12b-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 13);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'year');
    await setCustomStep(page, 1);
    await clickDone(page);

    const body = await saveAndCaptureCreateBody(page);
    expect(body.repeatType, 'yearly custom rule → repeatType 4').toBe(4);
    expect(body.repeatEvery, 'step=1 → repeatEvery 1').toBe(1);

    // The initial yearly occurrence paints on the anchored day (Monday, day 0)
    // of the seed week.
    const mondayCount = await calendarPage.getDayColumnTaskBlocks(0, title).count();
    expect(
      mondayCount,
      `Expected the yearly occurrence to render on Monday (day 0) of the seed week ` +
      `for "${title}", but found ${mondayCount}.`
    ).toBeGreaterThanOrEqual(1);

    // Yearly must NOT recur weekly — the following week is empty on every day.
    await calendarPage.navigateToNextWeek();
    for (let day = 0; day <= 6; day++) {
      const count = await calendarPage.getDayColumnTaskBlocks(day, title).count();
      expect(
        count,
        `Yearly must not recur weekly — day ${day} of the week after the seed week ` +
        `must be empty for "${title}", found ${count}.`
      ).toBe(0);
    }
  });

  // =======================================================================
  // CR13 — custom year, step=2 (everyNYear).
  //   Every 2 years on the start date's day-of-month (#933, exactly as CR12).
  //   repeatType still 4, repeatEvery now 2. Wire payload + creation asserted
  //   here; the render is asserted by CR13b (#922 FIXED).
  // =======================================================================
  test('CR13 — custom year step=2 (everyNYear) wires repeatType=4, repeatEvery=2, dayOfMonth from the start date and is created', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR13-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 12);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'year');
    await setCustomStep(page, 2);
    await clickDone(page);

    // Collapsed label: "Every 2 years on <day>. {month}" (da: "Hvert 2. år på
    // <day>. <month>"). Anchor on the years keyword + that a day number shows
    // (no longer hard-coded to 1, #933).
    await expect(repeatRowLabel(page)).toHaveText(/år|year/i);
    await expect(repeatRowLabel(page)).toHaveText(/\d{1,2}/);

    const body = await saveAndCaptureCreateBody(page);

    expect(body.repeatType, 'yearly custom rule → repeatType 4').toBe(4);
    expect(body.repeatEvery, 'step=2 → repeatEvery 2').toBe(2);
    const expectedDom = Number(body.taskDate.slice(-2));
    expect(
      body.dayOfMonth,
      'YEAR custom rule anchors dayOfMonth to the selected start date (#933)'
    ).toBe(expectedDom);
    expect(body.repeatWeekdaysCsv ?? '', 'yearly rule ships no weekday CSV').toBe('');
    expect(body.repeatOrdinalWeek ?? 0, 'yearly rule is not an ordinal rule').toBe(0);
  });

  // CR13b — every-2-years (repeatType=4) renders an occurrence (#922 FIXED).
  //   Same Year render fix as CR12b, for the every-N-year cadence. The wire
  //   carries repeatEvery=2; the "re-appears in 2 years, not 1" cadence is
  //   exercised by GetOccurrencesInWeek's Year math server-side (a 104-week
  //   chevron walk is impractical in a week-grid e2e). Here we assert the
  //   initial render on the anchored column, repeatEvery=2 on the wire, and no
  //   weekly recurrence.
  // =======================================================================
  test('CR13b — every-2-years (repeatType=4) renders an occurrence in the calendar grid', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR13b-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 14);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'year');
    await setCustomStep(page, 2);
    await clickDone(page);

    const body = await saveAndCaptureCreateBody(page);
    expect(body.repeatType, 'yearly custom rule → repeatType 4').toBe(4);
    expect(body.repeatEvery, 'step=2 → repeatEvery 2').toBe(2);

    // The initial occurrence paints on the anchored day (Monday, day 0) of the
    // seed week.
    const mondayCount = await calendarPage.getDayColumnTaskBlocks(0, title).count();
    expect(
      mondayCount,
      `Expected the every-2-years occurrence to render on Monday (day 0) of the ` +
      `seed week for "${title}", but found ${mondayCount}.`
    ).toBeGreaterThanOrEqual(1);

    // Must NOT recur weekly — the following week is empty on every day.
    await calendarPage.navigateToNextWeek();
    for (let day = 0; day <= 6; day++) {
      const count = await calendarPage.getDayColumnTaskBlocks(day, title).count();
      expect(
        count,
        `every-2-years must not recur weekly — day ${day} of the week after the ` +
        `seed week must be empty for "${title}", found ${count}.`
      ).toBe(0);
    }
  });

  // =======================================================================
  // CR32 — custom month, step=12, sub-type "Månedligt på den første"
  //        + a weekday that is NOT the anchor's own (#1207).
  //
  //   The customer's report: "I create a task on Tuesday 8 September 2026 and
  //   set the repetition to every 12 months, monthly on the first Tuesday. The
  //   task is created the first time on Tuesday 7 September 2027, which is
  //   wrong." 1 September 2026 is itself a Tuesday, so the start date is the
  //   SECOND Tuesday and violates the rule it was given. Both recurrence
  //   enumerators treated the start date purely as a lower bound on a pure
  //   pattern, so the start month's pattern date (which precedes the anchor)
  //   was discarded and the cursor jumped a whole repeat period — the first
  //   occurrence vanished. Since #1207 the anchor is occurrence #1 and the
  //   pattern governs #2 onward.
  //
  //   This is the FIRST e2e to drive the dialog's "Månedligt på den første"
  //   option at all.
  //
  //   The event anchors on the Monday of the displayed (next) week, whose
  //   position within its month varies with the run date. Both positions are
  //   covered by the same assertion:
  //     * anchor is NOT the month's 1st Monday (the common case) — the start
  //       week painted NOTHING before #1207 and paints the anchor now;
  //     * anchor IS the month's 1st Monday — it painted before and still does,
  //       exactly once (no duplicate).
  //
  //   A 12-month cadence cannot be walked in a week grid, so the SUBSEQUENT
  //   occurrences are asserted server-side in
  //   BackendConfiguration.Pn.Integration.Test/CalendarMonthlyAnchorOccurrenceTests.
  //
  //   NOTE on the picked weekday: CreateTask currently overwrites
  //   arp.DayOfWeek with the start date's own weekday whenever
  //   RepeatOrdinalWeek is set, so the rule the backend stores is "1st
  //   <anchor weekday>" regardless of the dialog pick. That is a separate,
  //   deliberately out-of-scope defect; this test therefore asserts the WIRE
  //   payload for the picked weekday and the RENDER for the anchored week,
  //   and does not assert which weekday the backend ends up storing.
  // =======================================================================

  /** Pick the monthly sub-type ("Gentagelsestype") by its visible label. The
   *  ng-dropdown panel is appended to <body>, so locate it from `page`. */
  async function setMonthlyKind(page: Page, label: RegExp): Promise<void> {
    await page
      .locator('.custom-repeat-dialog .monthly-kind-select .ng-select-container')
      .first()
      .click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').filter({ hasText: label }).first().click();
    await page.waitForTimeout(300);
  }

  /** Pick the Nth-weekday rule's weekday by its visible (locale) label. */
  async function setMonthlyWeekday(page: Page, label: RegExp): Promise<void> {
    await page
      .locator('.custom-repeat-dialog .monthly-weekday-select .ng-select-container')
      .first()
      .click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').filter({ hasText: label }).first().click();
    await page.waitForTimeout(300);
  }

  test('CR32 — custom month step=12 "Månedligt på den første <ugedag>" wires an ordinal rule and still paints in the anchored start week (#1207)', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR32-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 15);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'month');
    await setCustomStep(page, 12);
    // "Månedligt på den første" / "Monthly on the first".
    await setMonthlyKind(page, /månedligt på den første|monthly on the first/i);
    // Wednesday — the create slot anchors on a MONDAY, so this is deliberately
    // not the start date's own weekday.
    await setMonthlyWeekday(page, /^\s*(onsdag|wednesday)\s*$/i);

    // The sub-type select must actually hold the ordinal rule before saving.
    // .ng-value-label is text-only; .ng-value would include the clear glyph.
    await expect(
      page.locator('.custom-repeat-dialog .monthly-kind-select .ng-value-label').first()
    ).toHaveText(/månedligt på den første|monthly on the first/i);
    await expect(
      page.locator('.custom-repeat-dialog .monthly-weekday-select .ng-value-label').first()
    ).toHaveText(/^\s*(onsdag|wednesday)\s*$/i);

    await clickDone(page);

    // Collapsed label: "Hver 12. måned på den 1. onsdag".
    await expect(repeatRowLabel(page)).toHaveText(/12/);
    await expect(repeatRowLabel(page)).toHaveText(/onsdag|wednesday/i);

    const body = await saveAndCaptureCreateBody(page);

    expect(body.repeatType, 'monthly custom rule → repeatType 3').toBe(3);
    expect(body.repeatEvery, 'step=12 → repeatEvery 12').toBe(12);
    expect(
      body.repeatOrdinalWeek,
      '"Månedligt på den første" is an Nth-weekday rule → repeatOrdinalWeek 1'
    ).toBe(1);
    expect(
      body.dayOfMonth,
      'an Nth-weekday rule carries the "no day-of-month" sentinel 0'
    ).toBe(0);
    expect(
      body.repeatWeekdaysCsv,
      'the picked weekday travels on the wire (Wednesday = 3)'
    ).toBe('3');

    // #1207: the anchored start week must paint the occurrence. Before the fix
    // this was empty whenever the anchor was not the month's 1st Monday.
    // (>= 1, matching CR12b/CR13b: a deployed occurrence can surface through
    // both the recurrence and the compliance projection.)
    const mondayCount = await calendarPage.getDayColumnTaskBlocks(0, title).count();
    expect(
      mondayCount,
      `Expected "${title}" to render on Monday (day 0) of the seed week — the ` +
      `series' own start date is occurrence #1 (#1207) — but found ${mondayCount}.`
    ).toBeGreaterThanOrEqual(1);

    // The rest of the anchored week must stay empty: a 12-month rule has no
    // second occurrence anywhere near it.
    for (let day = 1; day <= 6; day++) {
      const count = await calendarPage.getDayColumnTaskBlocks(day, title).count();
      expect(
        count,
        `An every-12-months rule must paint only on its anchor — day ${day} of ` +
        `the seed week must be empty for "${title}", found ${count}.`
      ).toBe(0);
    }
  });
});
