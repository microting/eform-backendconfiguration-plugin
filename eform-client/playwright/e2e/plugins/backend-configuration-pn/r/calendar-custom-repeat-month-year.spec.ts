import { test, expect, Page } from '@playwright/test';
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
 *  - MONTH/YEAR occurrences land on the 1st-of-month / 1-January and require
 *    multi-month / multi-year navigation that is brittle in a week-grid e2e;
 *    the per-occurrence expansion math is exhaustively covered by
 *    calendar-repeat.service.spec unit tests. So here we black-box only what
 *    the frontend actually puts on the wire + the collapsed dropdown label.
 *
 *  - The custom dialog's MONTH option ALWAYS ships dayOfMonth=1 — it has no
 *    day-of-month control, so buildMetaFromCustomConfig hard-codes dom=1
 *    (calendar-repeat.service.ts:800). The create event is anchored on a
 *    Monday whose calendar day-of-month is NOT 1, yet the payload's
 *    dayOfMonth is still 1. CR10/CR11 assert exactly that quirk.
 *
 *  - The custom dialog's YEAR option ALWAYS ships dayOfMonth=1 and month=0
 *    (1 January) in the LOCAL meta (calendar-repeat.service.ts:802). Note the
 *    month index is NOT a separate wire field — the create payload carries
 *    repeatType/repeatEvery/dayOfMonth but no month index (verified in
 *    task-create-edit-modal.component.ts buildPayload). The yearly start month
 *    is implied by startDate (the anchored Monday's month), and the local
 *    January default only drives the collapsed label.
 *
 * KNOWN BACKEND GAP (Year does not render)
 * ----------------------------------------
 * The server recurrence engine (CalendarService GetOccurrencesInWeek /
 * EnumerateOccurrences) only expands Day/Week/Month rules — repeatType=4
 * (yearly) is NOT expanded, so a yearly event is CREATED (POST 200) but never
 * paints a block in the week view. This is the same gap the #888 RP05 test is
 * test.fixme for. CR12/CR13 therefore assert ONLY the wire payload + collapsed
 * label in an ACTIVE test, and mark the calendar-rendering check test.fixme
 * with a pointer to this gap.
 *
 * MATRIX (CR10–CR13)
 * ------------------
 *   CR10  month, step=1 (monthlyDom)    → repeatType=3, repeatEvery=1, dayOfMonth=1
 *   CR11  month, step=3 (everyNMonthDom)→ repeatType=3, repeatEvery=3, dayOfMonth=1
 *   CR12  year,  step=1 (yearlyOne)     → repeatType=4, repeatEvery=1, dayOfMonth=1 (render fixme)
 *   CR13  year,  step=2 (everyNYear)    → repeatType=4, repeatEvery=2, dayOfMonth=1 (render fixme)
 *
 * The (step, unit) → meta.kind mapping is fixed in
 * calendar-repeat.service.buildMetaFromCustomConfig:
 *   unit=month → step===1 ? 'monthlyDom' : 'everyNMonthDom'  (dom hard-coded 1)
 *   unit=year  → step===1 ? 'yearlyOne'  : 'everyNYear'      (dom=1, month=0)
 * and the meta.kind → wire mapping in task-create-edit-modal.buildPayload:
 *   monthlyDom / everyNMonthDom → repeatType 3
 *   yearlyOne  / everyNYear     → repeatType 4
 *   dayOfMonth        = metaToDayOfMonth(meta)        → 1 for all four kinds
 *   repeatWeekdaysCsv = metaToWeekdaysCsv(meta)       → null (no weekday info)
 *   repeatOrdinalWeek = metaToRepeatOrdinalWeek(meta) → null (not a by-day rule)
 *
 * MODEL
 * -----
 * `openCreateModalAtSlot(0, hour)` advances the calendar one week and clicks
 * Monday@hour, so every event anchors on the Monday of the displayed (next)
 * week — a date whose day-of-month is deliberately NOT necessarily 1, which is
 * the whole point of the dayOfMonth=1 quirk assertion.
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
 *   CR10=9, CR11=10, CR12=11, CR13=12.
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
  //   NOT 1, but the MONTH unit has no day-of-month control, so the dialog
  //   hard-codes dom=1 → dayOfMonth=1 on the wire. Assert that quirk, plus
  //   repeatType=3, repeatEvery=1, and that it is NOT a weekly/ordinal rule
  //   (repeatWeekdaysCsv null/empty, repeatOrdinalWeek null/0).
  //
  //   Monthly occurrence rendering (on the 1st of each month) needs multi-month
  //   navigation and is covered by calendar-repeat.service.spec — so we assert
  //   the wire payload + successful creation only, not day-cell rendering.
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
    // The dom=1 quirk: even though the event was created on a Monday whose
    // calendar day-of-month is not 1, the MONTH unit always ships dayOfMonth=1.
    expect(
      body.dayOfMonth,
      'MONTH unit has no day-of-month control → dayOfMonth hard-coded to 1'
    ).toBe(1);
    // Not a weekly rule and not an ordinal (Nth-weekday) rule.
    expect(body.repeatWeekdaysCsv ?? '', 'monthly-by-DOM rule ships no weekday CSV').toBe('');
    expect(body.repeatOrdinalWeek ?? 0, 'monthly-by-DOM rule is not an ordinal rule').toBe(0);
  });

  // =======================================================================
  // CR11 — custom month, step=3 (everyNMonthDom).
  //   Every 3rd month on day 1. Same dayOfMonth=1 quirk, repeatType still 3,
  //   repeatEvery now 3.
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
      'MONTH unit has no day-of-month control → dayOfMonth hard-coded to 1'
    ).toBe(1);
    expect(body.repeatWeekdaysCsv ?? '', 'monthly-by-DOM rule ships no weekday CSV').toBe('');
    expect(body.repeatOrdinalWeek ?? 0, 'monthly-by-DOM rule is not an ordinal rule').toBe(0);
  });

  // =======================================================================
  // CR12 — custom year, step=1 (yearlyOne).
  //   The YEAR unit always ships dayOfMonth=1 and a local month=0 (January)
  //   that drives the label. There is NO separate month index wire field —
  //   the create payload carries repeatType/repeatEvery/dayOfMonth only, and
  //   the yearly start month is implied by startDate. Assert the wire payload
  //   + collapsed label + successful CREATION (POST 200) here.
  //
  //   The companion CR12b test (test.fixme) documents the known Year RENDERING
  //   gap: repeatType=4 is not expanded by GetOccurrencesInWeek, so the event
  //   is created but never paints a block — same as #888 RP05.
  // =======================================================================
  test('CR12 — custom year step=1 (yearlyOne) wires repeatType=4, repeatEvery=1, dayOfMonth=1 and is created', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR12-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 11);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'year');
    await setCustomStep(page, 1);
    await clickDone(page);

    // Collapsed label reflects a yearly rule on 1 January (da: "Årligt den 1.
    // januar"). Asserted loosely since the run locale is Danish and the month
    // name is locale-dependent — anchor on the yearly keyword + day 1.
    await expect(repeatRowLabel(page)).toHaveText(/årligt|yearly/i);
    await expect(repeatRowLabel(page)).toHaveText(/(^|\D)1(\.|\s)/);

    const body = await saveAndCaptureCreateBody(page);

    expect(body.repeatType, 'yearly custom rule → repeatType 4').toBe(4);
    expect(body.repeatEvery, 'step=1 → repeatEvery 1').toBe(1);
    // YEAR unit always ships dom=1 (1 January in the local meta).
    expect(
      body.dayOfMonth,
      'YEAR unit has no day-of-month control → dayOfMonth hard-coded to 1'
    ).toBe(1);
    // No separate month index is sent on the wire (the local month=0 only
    // drives the label); assert the rule is neither weekly nor ordinal.
    expect(body.repeatWeekdaysCsv ?? '', 'yearly rule ships no weekday CSV').toBe('');
    expect(body.repeatOrdinalWeek ?? 0, 'yearly rule is not an ordinal rule').toBe(0);
  });

  // KNOWN BACKEND GAP — Year does not render in the week view.
  // repeatType=4 (yearly) is NOT expanded by CalendarService
  // GetOccurrencesInWeek / EnumerateOccurrences (Day/Week/Month only), so the
  // CR12 event is CREATED but never paints a block — identical to the #888
  // RP05 fixme. Kept as a documented placeholder so the gap is visible in the
  // suite and flips to an active assertion once the engine learns Year.
  test.fixme('CR12b — yearly (repeatType=4) renders an occurrence in the calendar grid', async () => {
    // Blocked by the server recurrence engine not expanding yearly rules
    // (same gap as #888 RP05). When fixed: create a yearlyOne rule anchored on
    // Monday, then assert a block lands on the anchored day-of-week column, and
    // re-appears the following year via multi-year navigation.
  });

  // =======================================================================
  // CR13 — custom year, step=2 (everyNYear).
  //   Every 2 years on day 1. repeatType still 4, repeatEvery now 2, same
  //   dayOfMonth=1 quirk. Wire payload + creation asserted here; rendering is
  //   the same Year gap, documented via CR13b test.fixme.
  // =======================================================================
  test('CR13 — custom year step=2 (everyNYear) wires repeatType=4, repeatEvery=2, dayOfMonth=1 and is created', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR13-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 12);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'year');
    await setCustomStep(page, 2);
    await clickDone(page);

    // Collapsed label: "Every 2 years on 1. {month}" (da: "Hvert 2. år på 1.
    // januar"). Anchor on the years keyword + day 1.
    await expect(repeatRowLabel(page)).toHaveText(/år|year/i);
    await expect(repeatRowLabel(page)).toHaveText(/(^|\D)1(\.|\s)/);

    const body = await saveAndCaptureCreateBody(page);

    expect(body.repeatType, 'yearly custom rule → repeatType 4').toBe(4);
    expect(body.repeatEvery, 'step=2 → repeatEvery 2').toBe(2);
    expect(
      body.dayOfMonth,
      'YEAR unit has no day-of-month control → dayOfMonth hard-coded to 1'
    ).toBe(1);
    expect(body.repeatWeekdaysCsv ?? '', 'yearly rule ships no weekday CSV').toBe('');
    expect(body.repeatOrdinalWeek ?? 0, 'yearly rule is not an ordinal rule').toBe(0);
  });

  // KNOWN BACKEND GAP — same as CR12b but for the every-N-year cadence.
  test.fixme('CR13b — every-2-years (repeatType=4) renders an occurrence in the calendar grid', async () => {
    // Blocked by the server recurrence engine not expanding yearly rules
    // (same gap as #888 RP05 / CR12b). When fixed: assert the block lands on
    // the anchored column and re-appears two years later, not one.
  });
});
