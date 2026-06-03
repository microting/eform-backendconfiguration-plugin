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
 * GitHub issue #901, focused on the remaining DIALOG MECHANICS that the
 * #898 (day/week) and #899 (month/year) suites don't cover.
 *
 * SCOPE
 * -----
 * Where calendar-custom-repeat-day-week.spec.ts (#898) asserts materialised
 * grid columns and calendar-custom-repeat-month-year.spec.ts (#899) asserts
 * the create POST wire payload, this suite drives the INTERNAL MECHANICS of
 * the Tilpasset… dialog itself, none of which depend on occurrence rendering:
 *
 *   - step-input bounds (max=99, min=1 clamp)
 *   - weekday-picker visibility per unit (only unit=week renders circles)
 *   - fresh-open weekday pre-selection from the task date
 *   - edit-mode reconstruction for day (everyNd) and week (everyNWeekOne) kinds
 *   - the synthesized `customCurrent` collapsed-label option
 *   - cancel-restores-previous-selection
 *   - the degenerate null-meta path (documented, fixme)
 *
 * MATRIX (CR19–CR31)
 * ------------------
 *   CR19  step max=99 (unit=day) → wire repeatType=1, repeatEvery=99
 *   CR20  step min clamp (0/empty → 1)
 *   CR21  fresh-open pre-selects task-date weekday (Monday slot → Monday circle active)
 *   CR22  weekday picker only renders for unit=week (0 circles for day/month/year)
 *   CR23  edit-reconstruct daily: day, step=2 (everyNd) rehydrates unit=day, step=2
 *   CR24  ── COVERED by B3 (custom-repeat until date-picker + btn-cancel-gcal close)
 *            in calendar-ui-enhancements.spec.ts. Not duplicated here.
 *   CR25  edit-reconstruct monthly (monthlyDom) — fixme (occurrence renders on
 *            day-1-of-month, not reliably in the seed/next week to click→edit)
 *   CR26  edit-reconstruct yearly (everyNYear) — fixme (repeatType=4 does NOT
 *            render in the week view; the Year gap, see RP05/CR12b)
 *   CR27  edit-reconstruct everyNWeekOne: week/Mon/step=2 rehydrates unit=week,
 *            step=2, Monday circle active
 *   CR28  customCurrent option: configuring a custom rule synthesizes a
 *            customCurrent option showing the rule summary; reopening the
 *            dropdown shows it + selecting it keeps the rule
 *   CR29  cancel restores previous: a known preset, then open Tilpasset…,
 *            mutate, btn-cancel-gcal → collapsed label unchanged
 *   CR30  ── COVERED by I1 (edit-mode reconstructs custom multi-day weekly,
 *            week/Mon-Wed-Fri/step=2/after-6, full reload→edit→reopen-dialog)
 *            in calendar-ui-enhancements.spec.ts. Not duplicated here.
 *   CR31  degenerate null-meta (repeatType=6 → zero occurrences) — fixme
 *            (defensive server path; not reachable through normal UI)
 *
 * The (step, unit, weekdays) → meta.kind → wire mapping is fixed in
 * calendar-repeat.service.buildMetaFromCustomConfig + task-create-edit-modal
 * buildPayload:
 *   unit=day  → step===1 ? 'daily' : 'everyNd'        → repeatType 1
 *   unit=week, 1 day → step===1 ? 'weeklyOne' : 'everyNWeekOne' → repeatType 2
 *
 * MODEL
 * -----
 * `openCreateModalAtSlot(0, hour)` advances the calendar one week and clicks
 * Monday@hour, so every event anchors on the Monday of the displayed (next)
 * week. Reconstruction tests mirror I1's reload → selectProperty →
 * navigateToNextWeek → click .task-block → preview Edit → reopen Tilpasset…
 * flow exactly.
 *
 * DISTINCT HOURS / TITLES
 * -----------------------
 * Every test uses a unique title (generateRandmString) and clicks a DIFFERENT
 * Monday hour so the create modal always opens on an empty slot:
 *   CR19=9, CR20=10, CR21=11, CR22=12, CR23=13, CR27=14, CR28=15, CR29=16.
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

test.describe.serial('Calendar custom repeat — dialog mechanics (#901)', () => {
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
  // Shared helpers (mirror calendar-custom-repeat-day-week.spec.ts /
  // calendar-custom-repeat-month-year.spec.ts).
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

  /** Set the step ("Repeat every N") number input via fill (replaces value). */
  async function setCustomStep(page: Page, step: number | string): Promise<void> {
    await page.locator('.custom-repeat-dialog .step-input input').fill(String(step));
    await page.waitForTimeout(150);
  }

  /** Toggle the weekday circles to EXACTLY match the desired active set.
   *  Circle order is Mon..Sun = idx 0..6. "active" in the class list =
   *  selected. Only meaningful in week mode (the weekday row renders only when
   *  unit==='week'). */
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

  /** True iff weekday-circle idx i (Mon=0..Sun=6) carries the `active` class. */
  async function isWeekdayActive(page: Page, idx: number): Promise<boolean> {
    const cls = (await page.locator('.custom-repeat-dialog .day-circle').nth(idx).getAttribute('class')) ?? '';
    return cls.split(/\s+/).includes('active');
  }

  /** Click Færdig (Done) and wait for the dialog to detach. */
  async function clickDone(page: Page): Promise<void> {
    await page.locator('.custom-repeat-dialog .btn-done-gcal').click();
    await page
      .locator('.custom-repeat-dialog')
      .waitFor({ state: 'detached', timeout: 5000 });
  }

  /** Click Annuller (Cancel) and wait for the dialog to detach. */
  async function clickCancel(page: Page): Promise<void> {
    await page.locator('.custom-repeat-dialog .btn-cancel-gcal').click();
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

  /** Open the repeat dropdown WITHOUT picking anything (panel stays open). */
  async function openRepeatDropdown(page: Page): Promise<void> {
    const repeatRow = page
      .locator('.gcal-row')
      .filter({ has: page.locator('mat-icon.gcal-icon:has-text("sync")') });
    await repeatRow.locator('.ng-select-container').first().click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
  }

  /** Save the create modal, capturing the create POST REQUEST BODY and
   *  awaiting its 200 response. Returns the parsed JSON wire payload.
   *  In a waitForRequest predicate the arg IS a Request → verb is r.method().
   *  Mirrors saveAndCaptureCreateBody in calendar-custom-repeat-month-year. */
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

  /** Plain save (no body capture) — awaits the create POST 200. */
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

  /** Reload the calendar route, reselect the property, advance to next week
   *  (where the Monday-anchored series lives), click the titled .task-block,
   *  open the preview Edit button, and wait for the edit modal title input.
   *  Mirrors I1 Steps 6–7 exactly. */
  async function reloadAndOpenForEdit(
    page: Page,
    calendarPage: CalendarUiEnhancementsPage,
    title: string,
  ): Promise<void> {
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();
    const folderResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
      { timeout: 60000 }
    );
    await calendarPage.selectProperty(property.name);
    await folderResp.catch(() => undefined);
    await page.waitForTimeout(1500);
    // The series anchors on next-week's Monday; advance the view so it shows.
    await calendarPage.navigateToNextWeek();

    const block = page.locator('.task-block').filter({ hasText: title }).first();
    await block.waitFor({ state: 'visible', timeout: 10000 });
    await block.click();
    await calendarPage.getPreviewEditButton().waitFor({ state: 'visible', timeout: 10000 });
    await calendarPage.getPreviewEditButton().click();
    await page.locator('#calendarEventTitle').waitFor({ state: 'visible', timeout: 10000 });
    await page.waitForTimeout(800);
  }

  // =======================================================================
  // CR19 — step max: unit=day, step=99. Input max=99, so 99 reads back as
  //   "99" and wires repeatType=1 (day), repeatEvery=99. We assert the wire
  //   payload + 200 only; we do NOT assert the 99-day cadence rendering (the
  //   next occurrence is 99 days out — far beyond any navigable week), the
  //   start block on Monday is sufficient proof the create succeeded.
  // =======================================================================
  test('CR19 — custom day step=99 (input max) wires repeatType=1, repeatEvery=99', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR19-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 9);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'day');
    await setCustomStep(page, 99);

    // Reads back as the max value "99".
    const stepInput = page.locator('.custom-repeat-dialog .step-input input');
    expect(await stepInput.inputValue()).toBe('99');

    await clickDone(page);

    const body = await saveAndCaptureCreateBody(page);
    expect(body.repeatType, 'unit=day maps to repeatType=1').toBe(1);
    expect(body.repeatEvery, 'step=99 wires repeatEvery=99').toBe(99);

    // The start occurrence renders on next-week's Monday (the anchor slot).
    await expect(calendarPage.getDayColumnTaskBlocks(0, title)).toHaveCount(1);
  });

  // =======================================================================
  // CR20 — step min clamp: set the step to "0" (below the input min=1) and
  //   commit/blur; the value clamps to "1" and the wire repeatEvery=1.
  //   Documents the EXACT clamp behaviour observed (see assertion comments).
  // =======================================================================
  test('CR20 — custom step below min clamps to 1 (input min) and wires repeatEvery=1', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR20-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 10);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'day');

    const stepInput = page.locator('.custom-repeat-dialog .step-input input');
    // Type "0" (below min) then blur to commit the clamp. Material/the
    // component's (change)/ngModel clamp coerces sub-min values up to min=1.
    await stepInput.fill('0');
    // Blur to fire the component's commit handler (clamp runs on change/blur).
    await page.locator('.custom-repeat-dialog .unit-select .ng-select-container').first().click();
    await page.keyboard.press('Escape'); // close the unit panel without changing it
    await page.waitForTimeout(200);

    // EXACT clamp behaviour: a sub-min "0" entry is coerced to the input's
    // min="1" — the model never holds 0. We assert the wire repeatEvery=1
    // (the canonical proof) and tolerate either "1" or an empty display in the
    // input read-back, because the clamp may run on the model rather than the
    // DOM value. The wire assertion below is the authoritative check.
    const displayed = await stepInput.inputValue();
    expect(
      displayed === '1' || displayed === '' || displayed === '0',
      `CR20 step display after sub-min entry was "${displayed}" — documented; ` +
      `wire repeatEvery is the authoritative clamp check below.`
    ).toBe(true);

    await clickDone(page);

    const body = await saveAndCaptureCreateBody(page);
    expect(body.repeatType, 'unit=day maps to repeatType=1').toBe(1);
    expect(
      body.repeatEvery,
      'a sub-min step must clamp to 1 on the wire (repeatEvery=1)'
    ).toBe(1);
  });

  // =======================================================================
  // CR21 — fresh-open pre-selects the task-date weekday. The slot is Monday
  //   (openCreateModalAtSlot(0, ...)); unit defaults to 'week'; the Monday
  //   circle (idx 0) must be `active` on fresh open.
  // =======================================================================
  test('CR21 — fresh Tilpasset… open pre-selects the task-date weekday (Monday)', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR21-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 11);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    // Unit defaults to 'week' → weekday circles render. The anchor is Monday,
    // so the Monday circle (idx 0) is pre-selected.
    await page.locator('.custom-repeat-dialog .day-circle').first().waitFor({ state: 'visible', timeout: 5000 });
    expect(
      await isWeekdayActive(page, 0),
      'fresh open against a Monday slot must pre-select the Monday circle (idx 0)'
    ).toBe(true);

    await clickCancel(page);
    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // CR22 — weekday picker renders ONLY for unit=week. Switch unit to day,
  //   month, year and assert 0 day-circles each; week shows >0.
  // =======================================================================
  test('CR22 — weekday circles render only for unit=week (0 for day/month/year)', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR22-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 12);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);

    const circles = page.locator('.custom-repeat-dialog .day-circle');

    // unit=week (default) → 7 circles.
    await setCustomUnit(page, 'week');
    await expect(circles).toHaveCount(7);

    // unit=day → no circles.
    await setCustomUnit(page, 'day');
    await expect(circles).toHaveCount(0);

    // unit=month → no circles.
    await setCustomUnit(page, 'month');
    await expect(circles).toHaveCount(0);

    // unit=year → no circles.
    await setCustomUnit(page, 'year');
    await expect(circles).toHaveCount(0);

    await clickCancel(page);
    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // CR23 — edit-reconstruct daily (everyNd). Create a unit=day, step=2 rule
  //   (everyNd), save, full reload, open via preview→Edit, reopen Tilpasset…,
  //   assert unit rehydrates to 'day' and step rehydrates to 2.
  //   Mirrors I1's reload→edit→reopen-dialog flow.
  // =======================================================================
  test('CR23 — edit-mode reconstructs a daily custom rule (unit=day, step=2)', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    test.setTimeout(180000);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR23-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 13);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'day');
    await setCustomStep(page, 2);
    await clickDone(page);

    await saveAndAwaitCreate(page);

    await reloadAndOpenForEdit(page, calendarPage, title);

    // Reopen Tilpasset… — the dialog hydrates from the reconstructed meta.
    await openCustomRepeatDialog(page);

    // Step rehydrates to 2.
    const stepInput = page.locator('.custom-repeat-dialog .step-input input');
    expect(await stepInput.inputValue()).toBe('2');

    // Unit rehydrates to 'day' — proven structurally by the absence of the
    // week-only weekday circles (CR22 establishes circles render only for
    // unit=week). This is locale-independent (the unit label is translated).
    await expect(page.locator('.custom-repeat-dialog .day-circle')).toHaveCount(0);

    await clickCancel(page);
    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // CR25 — edit-reconstruct monthly (monthlyDom). FIXME.
  //
  // The monthly custom rule (unit=month) hard-codes dayOfMonth=1
  // (buildMetaFromCustomConfig → dom=1), so its occurrences render on the 1st
  // of each month — NOT necessarily within the seed week or the +1 week we can
  // reliably navigate to. Anchoring the create on next-week's Monday means the
  // 1st-of-month occurrence is in an arbitrary other week, so there is no
  // robust .task-block to click → preview → Edit for the reconstruction step.
  //
  // INTENDED BODY (left fixme until a robust navigate-to-day-1 path exists):
  //   1. openCreateModalAtSlot(0, H); fillRequiredFields
  //   2. openCustomRepeatDialog; setCustomUnit('month'); setCustomStep(1); clickDone
  //   3. saveAndAwaitCreate
  //   4. navigate the week view to the month's day-1 occurrence (the brittle
  //      part — would need to compute the next 1st-of-month from the anchor
  //      Monday and navigateToNextWeek the right number of times, which varies
  //      0..5 weeks and may cross a /tasks/week empty-week with no block)
  //   5. click the block → preview Edit → reopen Tilpasset…
  //   6. assert unit rehydrates to 'month' (0 day-circles) and step=1
  // The wire payload for monthly is already covered actively by CR10/CR11 in
  // calendar-custom-repeat-month-year.spec.ts (#899); only the edit-reconstruct
  // round-trip is gated here.
  // =======================================================================
  test.fixme('CR25 — edit-mode reconstructs a monthly custom rule (monthlyDom)', async () => {
    // See header comment — monthlyDom occurrences land on day-1-of-month, which
    // is not reliably reachable in the navigable week window to click→edit.
    expect(seeded).toBe(true);
  });

  // =======================================================================
  // CR26 — edit-reconstruct yearly (everyNYear). FIXME.
  //
  // The server recurrence engine (CalendarService GetOccurrencesInWeek /
  // EnumerateOccurrences) does NOT expand repeatType=4 (yearly) — confirmed by
  // RP05 (calendar-repeat-presets) and CR12b/CR13b
  // (calendar-custom-repeat-month-year), both test.fixme. A yearly event is
  // CREATED (POST 200) but never paints a .task-block in ANY week view, so
  // there is nothing to click → preview → Edit to drive the reconstruction.
  //
  // INTENDED BODY (left fixme until the backend expands yearly rules):
  //   1. openCreateModalAtSlot(0, H); fillRequiredFields
  //   2. openCustomRepeatDialog; setCustomUnit('year'); setCustomStep(2); clickDone
  //   3. saveAndAwaitCreate
  //   4. reloadAndOpenForEdit(title)  ← FAILS HERE: no .task-block ever renders
  //   5. reopen Tilpasset…; assert unit='year' (0 day-circles) and step=2
  // The yearly wire payload is already covered actively by CR12/CR13 in
  // calendar-custom-repeat-month-year.spec.ts (#899).
  // =======================================================================
  test.fixme('CR26 — edit-mode reconstructs a yearly custom rule (everyNYear)', async () => {
    // See header comment — repeatType=4 (yearly) is not expanded server-side,
    // so no occurrence block renders to click→edit (the Year gap, RP05/CR12b).
    expect(seeded).toBe(true);
  });

  // =======================================================================
  // CR27 — edit-reconstruct everyNWeekOne. Create unit=week, Monday-only,
  //   step=2 (everyNWeekOne), save, full reload, open via preview→Edit, reopen
  //   Tilpasset…, assert unit=week, step=2, Monday circle active.
  //   Mirrors I1's reload→edit→reopen-dialog flow.
  // =======================================================================
  test('CR27 — edit-mode reconstructs an everyNWeekOne rule (week, Monday, step=2)', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    test.setTimeout(180000);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR27-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 14);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'week');
    await setCustomStep(page, 2);
    // Monday only (fresh open already pre-selects Monday — be explicit).
    await setActiveWeekdays(page, [true, false, false, false, false, false, false]);
    await clickDone(page);

    await saveAndAwaitCreate(page);

    await reloadAndOpenForEdit(page, calendarPage, title);

    await openCustomRepeatDialog(page);

    // Unit rehydrates to 'week' — weekday circles render (CR22 establishes
    // circles render only for unit=week).
    await expect(page.locator('.custom-repeat-dialog .day-circle')).toHaveCount(7);

    // Step rehydrates to 2.
    const stepInput = page.locator('.custom-repeat-dialog .step-input input');
    expect(await stepInput.inputValue()).toBe('2');

    // Monday (idx 0) active; the rest inactive.
    const expectedActive = [true, false, false, false, false, false, false];
    for (let i = 0; i < 7; i++) {
      expect(
        await isWeekdayActive(page, i),
        `weekday circle idx=${i} expected active=${expectedActive[i]}`
      ).toBe(expectedActive[i]);
    }

    await clickCancel(page);
    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // CR28 — customCurrent option. After configuring a custom rule and clicking
  //   Done, the repeat ng-select synthesizes a `customCurrent` option holding
  //   the rule summary, and the collapsed .ng-value-label shows that summary.
  //   Reopening the dropdown must expose the customCurrent option (the
  //   summary), and selecting it must keep the rule (label unchanged).
  // =======================================================================
  test('CR28 — configuring a custom rule synthesizes a customCurrent option that keeps the rule', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR28-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 15);
    await fillRequiredFields(page, title);

    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'week');
    await setCustomStep(page, 2);
    // Mon + Wed + Fri → a multi-day summary, distinct from any built-in preset.
    await setActiveWeekdays(page, [true, false, true, false, true, false, false]);
    await clickDone(page);

    // The collapsed label now shows the synthesized summary. The Danish run
    // locale renders "Hver 2. uge: mandag, onsdag og fredag" — assert a stable
    // non-empty summary that mentions the cadence (2) and is NOT the bare
    // "Tilpasset…" custom placeholder.
    const collapsed = repeatRowLabel(page);
    const summary = ((await collapsed.textContent()) ?? '').trim();
    expect(summary.length, 'customCurrent summary must be non-empty').toBeGreaterThan(0);
    expect(summary, 'customCurrent summary must reflect the configured cadence').toContain('2');

    // Reopen the repeat dropdown. A customCurrent option (carrying the summary)
    // is synthesized at the TOP of the option list (before the built-in
    // presets), so it is selectable by its label text.
    await openRepeatDropdown(page);
    const customCurrentOption = page
      .locator('.ng-dropdown-panel .ng-option')
      .filter({ hasText: summary })
      .first();
    await expect(
      customCurrentOption,
      'reopening the dropdown must expose the synthesized customCurrent option (the rule summary)'
    ).toBeVisible();

    // Selecting customCurrent keeps the rule — the collapsed label is unchanged.
    await customCurrentOption.click();
    await page.waitForTimeout(300);
    await expect(repeatRowLabel(page)).toHaveText(summary);

    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // CR29 — cancel restores the previous selection. Set the repeat to a known
  //   built-in preset (weeklyOne), capture the collapsed label, open
  //   Tilpasset…, mutate step + weekdays, then btn-cancel-gcal. The collapsed
  //   label must be unchanged from before opening the dialog (cancel reverts).
  // =======================================================================
  test('CR29 — cancelling Tilpasset… restores the previous repeat selection', async ({ page }) => {
    expect(seeded, 'seed property + worker must have completed').toBe(true);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `CR29-${generateRandmString(8)}`;

    await calendarPage.openCreateModalAtSlot(0, 16);
    await fillRequiredFields(page, title);

    // Known previous selection: weeklyOne (index 2 in selectRepeatPreset).
    await calendarPage.selectRepeatPreset('weeklyOne');
    await page.waitForTimeout(300);
    const labelBefore = ((await repeatRowLabel(page).textContent()) ?? '').trim();
    expect(labelBefore.length, 'a known preset must produce a non-empty label').toBeGreaterThan(0);

    // Open Tilpasset… and mutate the config (step + weekdays).
    await openCustomRepeatDialog(page);
    await setCustomUnit(page, 'week');
    await setCustomStep(page, 3);
    await setActiveWeekdays(page, [true, true, true, false, false, false, false]);

    // Cancel — the dialog discards the mutation and the row reverts to the
    // previous selection.
    await clickCancel(page);
    await page.waitForTimeout(300);

    const labelAfter = ((await repeatRowLabel(page).textContent()) ?? '').trim();
    expect(
      labelAfter,
      `cancelling Tilpasset… must restore the previous selection ` +
      `(before="${labelBefore}", after="${labelAfter}")`
    ).toBe(labelBefore);

    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // CR31 — degenerate null-meta (repeatType=6 → zero occurrences). FIXME.
  //
  // Selecting Tilpasset… opens the dialog, but producing a COMMITTED custom
  // selection whose customRepeatMeta is null is not achievable through the
  // normal UI: clicking Done always builds a meta from the current config, and
  // clicking Cancel (btn-cancel-gcal) reverts the selection to the previous
  // value (covered actively by CR29) rather than committing a null-meta custom
  // option. There is no UI affordance that lands on repeatType=6 with a null
  // meta.
  //
  // This is a DEFENSIVE server-side code path: when the wire payload arrives
  // with a custom repeatType but no usable repeat metadata, the recurrence
  // engine emits ZERO occurrences (a safe no-op) rather than throwing. It is
  // exercised by backend unit tests on the recurrence service, not by an e2e
  // through the dialog.
  //
  // INTENDED BODY (would require a non-UI hook to forge the payload):
  //   - intercept/forge a create POST with repeatType=6 + null repeat meta
  //   - assert HTTP 200 and that GET /tasks/week returns no occurrences for
  //     the series across the visible weeks
  // Left fixme — not reachable through the dialog, documented as a defensive
  // zero-occurrence path.
  // =======================================================================
  test.fixme('CR31 — degenerate null-meta custom selection emits zero occurrences (defensive)', async () => {
    // See header comment — a committed null-meta custom selection isn't
    // reachable through the normal UI (Done always builds a meta; Cancel
    // reverts). Defensive server path emitting zero occurrences.
    expect(seeded).toBe(true);
  });
});
