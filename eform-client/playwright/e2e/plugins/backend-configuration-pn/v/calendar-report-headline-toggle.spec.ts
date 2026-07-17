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
 * Calendar report-headline checkbox suite.
 *
 * The create/edit-task modal has a `mat-checkbox`
 * (`#calendarEventReportHeadlineToggle`) in front of the "Report headline"
 * mtx-select (`#calendarEventPlanningTag`):
 *   - Create mode: checkbox defaults CHECKED, dropdown enabled.
 *   - Checked + no tag selected => `#calendarEventSaveBtn` is DISABLED (the
 *     new save-guard leg: `reportHeadlineEnabledControl.value &&
 *     !planningTagControl.value`).
 *   - Unchecked => label dims, `#calendarEventPlanningTag` control disabled
 *     (its value is retained visually), Save is allowed, and the POST body
 *     carries `itemPlanningTagId: null` (the old server-side
 *     `ReportTableHeaderTagIsRequired` rejection has been removed).
 *   - Edit mode: checkbox is seeded checked iff the task has a headline;
 *     unchecking + saving removes the headline series-wide.
 *
 * Single property + single worker seed suffices here — one worker is enough
 * to make Save reachable, and none of these tests need a multi-assignee
 * list. Lives in `v/` to share the matrix slot with
 * calendar-create-validation.spec.ts and reuse CalendarUiEnhancementsPage.
 *
 * Slot strategy: each test gets a fresh page (beforeEach re-navigates to the
 * calendar's real current week), so every `openCreateModalAtSlot`/
 * `openCreateModalAt9AM()` call independently advances one week (chevron
 * click) and lands on next week's target day — it never compounds across
 * tests. What DOES compound is a same-day collision: two persisting tests
 * both targeting next-week Monday would race for the same cell. Per the
 * sibling calendar-create-validation.spec.ts convention ("Each test uses a
 * DISTINCT weekday ... so the serial suite never collides on a shared
 * week"), T01 (non-persisting, cancels) and T02 (persists) share Monday
 * (day 0) safely since T01 never saves; T03 (persists) uses Tuesday (day 1)
 * to avoid colliding with T02's saved block.
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

test.describe.serial('Calendar report-headline checkbox', () => {
  let calendarPage: CalendarUiEnhancementsPage;

  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);

    calendarPage = new CalendarUiEnhancementsPage(page);
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

    // Sanity: open a create modal and confirm the assignee dropdown lists the
    // seeded worker, so the assignee-dependent legs below are exercisable.
    const seedCalendarPage = new CalendarUiEnhancementsPage(page);
    await seedCalendarPage.goToCalendar();
    await seedCalendarPage.ensureSidebarOpen();
    const folderResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
      { timeout: 60000 }
    );
    await seedCalendarPage.selectProperty(property.name);
    await folderResp.catch(() => undefined);
    await page.waitForTimeout(1000);

    await seedCalendarPage.openCreateModalAtSlot(0, 8);
    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    const optionCount = await page.locator('.ng-dropdown-panel .ng-option').count();
    expect(
      optionCount,
      `assignee dropdown should list the seeded worker; got ${optionCount}`
    ).toBeGreaterThanOrEqual(1);
    await seedCalendarPage.closeEventModal();
  });

  // =======================================================================
  // T01 — unchecking dims and disables the report-headline dropdown;
  // re-checking restores the previously picked value.
  // =======================================================================
  test('unchecking dims and disables the report headline dropdown; re-checking restores value', async ({ page }) => {
    await calendarPage.openCreateModalAt9AM();
    const select = page.locator('#calendarEventPlanningTag');
    const toggle = page.locator('#calendarEventReportHeadlineToggle');

    // default: checked + enabled, placeholder visible
    await expect(toggle.locator('input')).toBeChecked();
    await expect(select.locator('input')).toBeEnabled();

    // pick a tag, then uncheck: select disabled, value retained visually
    await select.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);
    const chosen = (await select.locator('.ng-value-label').innerText()).trim();
    await toggle.click();
    await page.waitForTimeout(300);
    await expect(toggle.locator('input')).not.toBeChecked();
    await expect(select.locator('input')).toBeDisabled();
    await expect(select).toHaveClass(/mtx-select-disabled/);

    // re-check: enabled again with the same value
    await toggle.click();
    await page.waitForTimeout(300);
    await expect(select.locator('input')).toBeEnabled();
    await expect(select.locator('.ng-value-label')).toHaveText(chosen);

    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // T02 — a task created with the checkbox unchecked stores no headline.
  // =======================================================================
  test('task created with checkbox unchecked stores no headline', async ({ page }) => {
    const title = `no-headline-${generateRandmString(5)}`;
    await calendarPage.openCreateModalAt9AM();

    // Fill title / eForm / assignee exactly as fillAndSaveEvent does, but
    // skip the planning tag — mirrors the locator steps in
    // calendar-ui-enhancements.page.ts fillAndSaveEvent().
    await page.locator('#calendarEventTitle').fill(title);

    const eform = page.locator('#calendarEventEform');
    await eform.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);

    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    // Deliberately DO NOT pick a planning tag — uncheck the report-headline
    // toggle instead so Save is reachable without one.
    await page.locator('#calendarEventReportHeadlineToggle').click();
    await page.waitForTimeout(300);

    const [response] = await Promise.all([
      page.waitForResponse(
        r => r.url().includes('/calendar/tasks') && r.request().method() === 'POST',
        { timeout: 30000 }
      ),
      page.locator('#calendarEventSaveBtn').click(),
    ]);
    expect((await response.json().catch(() => null))?.success).toBeTruthy();

    await page.waitForTimeout(1000);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });

    // Reopen: checkbox unchecked, select disabled and empty.
    await calendarPage.openEditModal(title);
    await expect(page.locator('#calendarEventReportHeadlineToggle input')).not.toBeChecked();
    await expect(page.locator('#calendarEventPlanningTag input')).toBeDisabled();
    await expect(page.locator('#calendarEventPlanningTag .ng-value')).toHaveCount(0);

    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // T03 — unchecking on edit removes the headline from the task.
  // =======================================================================
  // Uses Tuesday (day offset 1) rather than openCreateModalAt9AM()'s Monday
  // (day 0) — T02 already persists a saved block on next-week Monday, and
  // each test gets a fresh page (openCreateModalAtSlot always lands on
  // "next week" from a freshly-loaded current week), so reusing Monday here
  // would race T02's block for the same cell. Distinct weekday per
  // persisting test matches the convention in
  // calendar-create-validation.spec.ts (V01-V06 each use their own day).
  test('unchecking on edit removes the headline from the task', async ({ page }) => {
    const title = `remove-headline-${generateRandmString(5)}`;
    await calendarPage.openCreateModalAtSlot(1, 9);
    await calendarPage.fillAndSaveEvent(title); // creates WITH a headline

    await calendarPage.openEditModal(title);
    await expect(page.locator('#calendarEventReportHeadlineToggle input')).toBeChecked();
    await page.locator('#calendarEventReportHeadlineToggle').click(); // uncheck
    await page.waitForTimeout(300);
    await calendarPage.clickSaveInEditModal();
    await page.waitForTimeout(1000);

    await calendarPage.openEditModal(title);
    await expect(page.locator('#calendarEventReportHeadlineToggle input')).not.toBeChecked();
    await expect(page.locator('#calendarEventPlanningTag .ng-value')).toHaveCount(0);

    await calendarPage.closeEventModal();
  });
});
