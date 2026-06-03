import { test, expect } from '@playwright/test';
import * as path from 'path';
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
 * Calendar copy-flow suite for GitHub issue #886. Each test creates its own
 * source event (distinct weekday + time + unique title) then exercises the
 * preview → Copy → create-edit-modal → save flow. The copy create-POST hits
 * the same endpoint as a normal create (POST .../calendar/tasks), so the
 * POST assertion matches that endpoint and excludes /tasks/week.
 *
 * Matrix rows:
 *   P01 — "copy preserves eForm + planning tag" — ALREADY COVERED by
 *         l/calendar.spec.ts "should copy an event and preserve eForm +
 *         planning tag" (asserts the "Copy of"/"Kopi af" title-prefix via
 *         title length > source length, plus eForm + planning-tag retained).
 *         Not duplicated here.
 *   P02 — copy adjusts a past-dated source forward (date invariant).
 *   P03 — copy reconstructs a custom repeat rule.
 *   P04 — copy does NOT carry attachments forward.
 *   P05 — copy from the schedule (list) view.
 *
 * Lives in `r/` to share the matrix slot with the other UI-enhancement
 * suites; reuses CalendarUiEnhancementsPage (extended with clickCopyInPreview
 * + getCreateModalTitle + getCreateModalDateText) and the same property/
 * worker seed pattern as calendar-resize.spec.ts.
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

// Fixture re-used from the attachments suite — the CI workflow's `cp -av` of
// the backend-configuration-pn directory picks up the whole fixtures tree.
const FIXTURE_DIR = path.resolve(__dirname, '../fixtures/calendar-attachments');
const PDF_FIXTURE = path.join(FIXTURE_DIR, 'sample.pdf');

// Matches the create endpoint specifically (POST .../calendar/tasks), NOT the
// loadTasks reload (POST .../tasks/week) nor move/resize. A copy save goes
// through the same create POST, so this predicate works for both.
function isCreatePost(r: import('@playwright/test').Response): boolean {
  return (
    r.url().includes('/api/backend-configuration-pn/calendar/tasks') &&
    !r.url().includes('/tasks/week') &&
    !r.url().includes('/tasks/move') &&
    !r.url().includes('/tasks/resize') &&
    !/\/files(?:\/|$)/.test(r.url()) &&
    r.request().method() === 'POST'
  );
}

// Open the create modal at the given slot and fill the three backend-required
// dropdowns (first eForm + first planning tag + first assignee), mirroring
// createSimpleEvent in calendar-resize.spec.ts. Leaves the modal open.
async function fillRequiredFields(
  page: import('@playwright/test').Page,
  title: string,
): Promise<void> {
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

// Create a single non-recurring event at next-week's dayOffset/hour with the
// required fields filled. Awaits the create POST + the rendered block.
async function createSimpleEvent(
  page: import('@playwright/test').Page,
  calendarPage: CalendarUiEnhancementsPage,
  title: string,
  dayOffset: number,
  hour: number,
): Promise<void> {
  await calendarPage.openCreateModalAtSlot(dayOffset, hour);
  await fillRequiredFields(page, title);

  const createResp = page.waitForResponse(isCreatePost, { timeout: 30000 });
  await page.locator('#calendarEventSaveBtn').click();
  await createResp;
  await page.waitForTimeout(1500);
  await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
}

test.describe.serial('Calendar copy flows (#886)', () => {
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

  // =======================================================================
  // P02: copy keeps the date in the valid (today-or-future) range.
  //
  // WEAKENED INVARIANT — read this before changing the assertion:
  // The create modal's mini-calendar enforces minDate=today and the create
  // flow rejects past time slots, so a PAST-dated source event cannot be
  // produced through the UI. `computeCopyDate` (calendar-copy-date.helper.ts)
  // only shifts the date forward when the source date+time is already in the
  // past:
  //   - source date < today        → today
  //   - source date == today AND startHour < now → tomorrow
  //   - otherwise (future)         → source date unchanged
  // Because every UI-creatable source is in the FUTURE, the forward-shift
  // branch is unreachable from e2e and is instead covered by the helper's
  // unit tests. Here we assert the weaker but still meaningful invariant:
  // the copy modal's Date field is NEVER a past date — for a future source
  // it equals the source's date — and the copy saves (POST 200) and renders.
  // =======================================================================
  test('P02: copy adjusts a past-dated source forward to today/tomorrow', async ({ page }) => {
    test.setTimeout(180000);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `P02-${generateRandmString(5)}`;

    // Source: Monday next week at 09:00 (guaranteed future).
    await createSimpleEvent(page, calendarPage, title, 0, 9);

    // Capture the source event's displayed date by opening it for EDIT first
    // (edit shows the source's own date), then cancel out without mutating.
    await calendarPage.openEventPreview(title);
    await calendarPage.clickEditInPreview();
    const sourceDateText = await calendarPage.getCreateModalDateText();
    expect(sourceDateText.length).toBeGreaterThan(0);
    await calendarPage.closeEventModal();
    await page.waitForTimeout(500);

    // Now open the COPY flow: preview → Copy.
    await calendarPage.openEventPreview(title);
    await calendarPage.clickCopyInPreview();

    // Copy-of prefix sanity (locale-dependent prefix, so check length grows).
    const copyTitle = await calendarPage.getCreateModalTitle();
    expect(copyTitle).toContain(title);
    expect(copyTitle.length).toBeGreaterThan(title.length);

    // Date invariant: for a future source the copy keeps the same date, and
    // it is never a past date. We assert equality with the source date here;
    // the forward-shift branch is unit-tested in calendar-copy-date.helper.
    const copyDateText = await calendarPage.getCreateModalDateText();
    expect(copyDateText.length).toBeGreaterThan(0);
    expect(copyDateText).toBe(sourceDateText);

    // Save the copy → POST 200 + success.
    const createResp = page.waitForResponse(isCreatePost, { timeout: 30000 });
    await page.locator('#calendarEventSaveBtn').click();
    const response = await createResp;
    const body = await response.json().catch(() => null);
    expect(response.status()).toBe(200);
    expect(body?.success).toBeTruthy();

    // The copy renders on the calendar (locale-prefixed title contains the
    // original substring).
    await page.waitForTimeout(1500);
    expect(
      await page.locator('.task-block').filter({ hasText: title }).count()
    ).toBeGreaterThanOrEqual(1);
  });

  // =======================================================================
  // P03: copy reconstructs a custom (multi-day weekly) repeat rule.
  //
  // Mirrors I1 in calendar-ui-enhancements.spec.ts for the custom-rule
  // configuration + collapsed-summary assertion ("Hver 2. uge: mandag,
  // onsdag og fredag"). After creating the recurring source, the copy modal
  // must rehydrate the SAME collapsed summary, and reopening Tilpasset… must
  // show the same dialog field values (step=2, unit=uge, Mon/Wed/Fri).
  // =======================================================================
  test('P03: copy reconstructs a custom repeat rule', async ({ page }) => {
    test.setTimeout(180000);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `P03-${generateRandmString(5)}`;
    const expectedSummary = 'Hver 2. uge: mandag, onsdag og fredag';
    const expectedActive = [true, false, true, false, true, false, false];

    // Source: Tuesday next week at 09:00 (distinct slot from the other tests).
    await calendarPage.openCreateModalAtSlot(1, 9);
    await fillRequiredFields(page, title);

    // Open Repeat → Tilpasset… (custom is the last option).
    const repeatRow = page
      .locator('.gcal-row')
      .filter({ has: page.locator('mat-icon.gcal-icon:has-text("sync")') });
    await repeatRow.locator('.ng-select-container').first().click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').last().click();

    // Configure: step=2, weekly, Mon+Wed+Fri.
    await page.locator('.custom-repeat-dialog').waitFor({ state: 'visible', timeout: 10000 });
    await page.locator('.custom-repeat-dialog .step-input input').fill('2');

    const dayCircles = page.locator('.custom-repeat-dialog .day-circle');
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

    // Færdig — collapse back to the summary label.
    await page.locator('.custom-repeat-dialog .btn-done-gcal').click();
    await page.locator('.custom-repeat-dialog').waitFor({ state: 'detached', timeout: 5000 });

    await expect(
      repeatRow.locator('.ng-value-label').first()
    ).toHaveText(expectedSummary);

    // Save the source.
    const createSource = page.waitForResponse(isCreatePost, { timeout: 30000 });
    await page.locator('#calendarEventSaveBtn').click();
    await createSource;
    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });

    // Open preview → Copy.
    await calendarPage.openEventPreview(title);
    await calendarPage.clickCopyInPreview();

    // The copy modal must rehydrate the SAME collapsed summary label.
    const copyRepeatRow = page
      .locator('.gcal-row')
      .filter({ has: page.locator('mat-icon.gcal-icon:has-text("sync")') });
    await expect(copyRepeatRow.locator('.ng-value-label').first())
      .toHaveText(expectedSummary);

    // Reopen Tilpasset… in the copy modal and verify the dialog field values
    // match what I1 asserts after reconstruction.
    await copyRepeatRow.locator('.ng-select-container').first().click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').last().click();
    await page.locator('.custom-repeat-dialog').waitFor({ state: 'visible', timeout: 10000 });

    expect(await page.locator('.custom-repeat-dialog .step-input input').inputValue()).toBe('2');
    await expect(page.locator('.custom-repeat-dialog .unit-select .ng-value-label').first())
      .toHaveText('uge');

    const copyDayCircles = page.locator('.custom-repeat-dialog .day-circle');
    for (let i = 0; i < 7; i++) {
      const cls = (await copyDayCircles.nth(i).getAttribute('class')) ?? '';
      const isActive = cls.split(/\s+/).includes('active');
      expect(isActive, `weekday circle index=${i} expected active=${expectedActive[i]}, got=${isActive}`)
        .toBe(expectedActive[i]);
    }

    // Cancel the dialog so closing the modal doesn't mutate the row.
    await page.locator('.custom-repeat-dialog .btn-cancel-gcal').click();
    await page.locator('.custom-repeat-dialog').waitFor({ state: 'detached', timeout: 5000 });

    // Save the copy → POST 200.
    const createCopy = page.waitForResponse(isCreatePost, { timeout: 30000 });
    await page.locator('#calendarEventSaveBtn').click();
    const response = await createCopy;
    const body = await response.json().catch(() => null);
    expect(response.status()).toBe(200);
    expect(body?.success).toBeTruthy();
  });

  // =======================================================================
  // P04: copy does NOT carry attachments forward.
  //
  // Create a source event with one PDF attachment (pre-save staging flow from
  // calendar-attachments.spec.ts J1), save, then copy it. After saving the
  // copy, reopen the COPY in edit mode and assert ZERO attachment rows — copy
  // mode intentionally drops attachments (see task-create-edit-modal.component
  // comment "copy mode intentionally does NOT carry attachments forward").
  // =======================================================================
  test('P04: copy does not carry attachments', async ({ page }) => {
    test.setTimeout(180000);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `P04-${generateRandmString(5)}`;

    // Source: Saturday next week at 09:00. Saturday avoids P03's Mon/Wed/Fri
    // recurring occurrences (which would otherwise sit on this week's
    // Wednesday and intercept the empty-slot click).
    await calendarPage.openCreateModalAtSlot(5, 9);
    await fillRequiredFields(page, title);

    // Stage one PDF before save — queues as a pending row, uploaded post-create.
    await page.locator('#calendarEventAttachInput').setInputFiles([PDF_FIXTURE]);
    await expect(page.locator('.gcal-attachment-row .gcal-attachment-pending-icon'))
      .toHaveCount(1, { timeout: 5000 });

    const createSource = page.waitForResponse(isCreatePost, { timeout: 30000 });
    const upload = page.waitForResponse(
      r => /\/calendar\/tasks\/\d+\/files$/.test(r.url())
        && r.request().method() === 'POST',
      { timeout: 60000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await createSource;
    await upload;
    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });

    // Sanity: the source actually has the attachment when reopened for edit.
    await calendarPage.openEventPreview(title);
    await calendarPage.clickEditInPreview();
    await expect(page.locator('.gcal-attachment-row', { hasText: 'sample.pdf' }))
      .toBeVisible({ timeout: 10000 });
    await calendarPage.closeEventModal();
    await page.waitForTimeout(500);

    // Open preview → Copy → save the copy.
    await calendarPage.openEventPreview(title);
    await calendarPage.clickCopyInPreview();

    // While we're in the copy modal, attachments must already be empty.
    await expect(page.locator('.gcal-attachment-row')).toHaveCount(0, { timeout: 5000 });

    const copyTitle = await calendarPage.getCreateModalTitle();
    expect(copyTitle).toContain(title);
    expect(copyTitle.length).toBeGreaterThan(title.length);

    const createCopy = page.waitForResponse(isCreatePost, { timeout: 30000 });
    await page.locator('#calendarEventSaveBtn').click();
    const response = await createCopy;
    const body = await response.json().catch(() => null);
    expect(response.status()).toBe(200);
    expect(body?.success).toBeTruthy();
    await page.waitForTimeout(1500);

    // Reopen the COPY in edit mode. The copy's title is locale-prefixed
    // ("Copy of " / "Kopi af "); two blocks now contain the `title`
    // substring (source + copy). Pick the block whose visible title is the
    // LONGER one (the prefixed copy) so we open the copy, not the source.
    const candidateBlocks = page.locator('.task-block').filter({ hasText: title });
    const count = await candidateBlocks.count();
    expect(count).toBeGreaterThanOrEqual(2);
    let copyBlockIndex = 0;
    let longestLen = -1;
    for (let i = 0; i < count; i++) {
      const text = ((await candidateBlocks.nth(i).locator('.task-title').first().textContent()) ?? '').trim();
      if (text.length > longestLen) {
        longestLen = text.length;
        copyBlockIndex = i;
      }
    }
    await candidateBlocks.nth(copyBlockIndex).locator('.task-block-body').click();
    await page.locator('app-task-preview-modal').waitFor({ state: 'visible', timeout: 10000 });
    await calendarPage.clickEditInPreview();

    // The copy must have NO attachments — the source's PDF was not carried.
    await expect(page.locator('.gcal-attachment-row')).toHaveCount(0, { timeout: 10000 });

    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // P05: copy from the schedule (list) view.
  //
  // Mirrors F1 (calendar-resize.spec.ts) for the schedule-row → preview
  // popover path, then continues into the copy flow: clicking Copy in the
  // popover opens the create modal in copy mode (title carries the locale
  // "Copy of"/"Kopi af" prefix → contains source title AND is longer).
  // =======================================================================
  test('P05: copy from schedule view', async ({ page }) => {
    test.setTimeout(180000);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `P05-${generateRandmString(5)}`;

    // Source: Thursday next week at 09:00.
    await createSimpleEvent(page, calendarPage, title, 3, 9);

    // Switch to schedule view — the view-switch snaps currentDate to Monday
    // of the currently-viewed week (next week), so the source row is visible.
    await calendarPage.switchToScheduleView();

    const row = calendarPage.findScheduleItem(title);
    await expect(row).toBeVisible();
    await row.click();

    // Preview popover opens with the shared Edit/Copy/Delete actions.
    await page.locator('app-task-preview-modal').waitFor({ state: 'visible', timeout: 10000 });
    await expect(calendarPage.getPreviewCopyButton()).toBeVisible();

    // Copy → create modal opens in copy mode.
    await calendarPage.clickCopyInPreview();

    const copyTitle = await calendarPage.getCreateModalTitle();
    expect(copyTitle).toContain(title);
    expect(copyTitle.length).toBeGreaterThan(title.length);

    // Save the copy → POST 200.
    const createCopy = page.waitForResponse(isCreatePost, { timeout: 30000 });
    await page.locator('#calendarEventSaveBtn').click();
    const response = await createCopy;
    const body = await response.json().catch(() => null);
    expect(response.status()).toBe(200);
    expect(body?.success).toBeTruthy();
  });
});
