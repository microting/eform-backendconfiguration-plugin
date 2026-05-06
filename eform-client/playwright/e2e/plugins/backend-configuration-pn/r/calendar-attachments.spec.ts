import { test, expect } from '@playwright/test';
import * as path from 'path';
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
 * Calendar event-attachments suite (J-series). Mirrors the bootstrap of
 * `calendar-resize.spec.ts`: serial describe with a property + worker seed.
 *
 * J1 covers the create-mode staging flow: stage PDF + PNG + JPG before
 * save, save the event, wait for the create POST + 3 sequential file
 * POSTs, reload, delete the PDF, reload again, verify PNG + JPG remain.
 *
 * J2 covers the edit-mode immediate-upload flow: open J1's existing event
 * for edit, upload a new PDF, assert it appears immediately, reload, reopen
 * for edit, verify all 3 attachments are visible.
 *
 * Lives in `r/` to share the matrix slot with the existing UI-enhancement
 * suite.
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

// Fixtures live next to the spec inside e2e/plugins/backend-configuration-pn/
// so the CI workflow's existing `cp -av` of that directory picks them up.
const FIXTURE_DIR = path.resolve(__dirname, 'fixtures/calendar-attachments');
const PDF_FIXTURE = path.join(FIXTURE_DIR, 'sample.pdf');
const PNG_FIXTURE = path.join(FIXTURE_DIR, 'sample.png');
const JPG_FIXTURE = path.join(FIXTURE_DIR, 'sample.jpg');

test.describe.serial('Calendar event attachments', () => {
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
  // J1: full attachment round-trip — create event with 3 files staged BEFORE
  //     save (pre-save staging flow), wait for the create POST + 3 sequential
  //     file POSTs, reopen, delete the PDF, reload, assert the two remaining
  //     attachments are still present.
  // =======================================================================
  test('J1: stage PDF + PNG + JPG in create modal, save, delete one, verify', async ({ page }) => {
    test.setTimeout(180000);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `J1-${generateRandmString(5)}`;

    // 1. Open create modal at next-week Monday@9, fill required fields.
    await calendarPage.openCreateModalAtSlot(0, 9);
    await page.locator('#calendarEventTitle').fill(title);

    // eForm — required by backend validation in the suite.
    await page.locator('#calendarEventEform').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);

    // Planning tag.
    await page.locator('#calendarEventPlanningTag').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);

    // Assignee.
    await page.locator('#calendarEventAssignee').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    // Sanity: in create-mode the Attach link is now ALWAYS active (the
    // pre-save staging flow no longer requires saving first). Verify the
    // link is visible and clickable, with no disabled-class variant rendered.
    await expect(page.locator('#calendarEventAttachLink')).toBeVisible();
    await expect(page.locator('a.gcal-action-link--disabled', { hasText: 'Vedhæft fil' }).or(
                  page.locator('a.gcal-action-link--disabled', { hasText: 'Attach file' }))).toHaveCount(0);

    // 2. Stage all three files in the create modal — they queue locally as
    //    'pending' chips (icon = `schedule`, no spinner), no network call yet.
    await page.locator('#calendarEventAttachInput').setInputFiles([
      PDF_FIXTURE, PNG_FIXTURE, JPG_FIXTURE,
    ]);
    // Three pending rows visible — match by the schedule icon class so we
    // don't false-positive on completed rows.
    await expect(page.locator('.gcal-attachment-row .gcal-attachment-pending-icon'))
      .toHaveCount(3, { timeout: 5000 });

    // 3. Click Gem (Save). Expect 1 create POST + 3 sequential file POSTs.
    const createResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
        && !r.url().includes('/tasks/week')
        && !/\/files(?:\/|$)/.test(r.url())
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    const upload1 = page.waitForResponse(
      r => /\/calendar\/tasks\/\d+\/files$/.test(r.url())
        && r.request().method() === 'POST',
      { timeout: 60000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await createResp;
    // One file upload registered is enough proof the staged-flow fired —
    // the modal closes once `uploadStagedFilesSequential` finishes the loop.
    await upload1;
    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });

    // 4. Re-open the event in edit-mode. The 3 attachments should be visible
    //    (no longer pending — they were uploaded post-save).
    await calendarPage.findEventBlock(title).click();
    await page.locator('app-task-preview-modal').waitFor({ state: 'visible', timeout: 10000 });
    await calendarPage.getPreviewEditButton().click();
    await page.locator('#calendarEventTitle').waitFor({ state: 'visible', timeout: 15000 });
    await expect(page.locator('.gcal-attachment-row').filter({ hasNot: page.locator('mat-spinner') }))
      .toHaveCount(3, { timeout: 30000 });

    // 5. Close the modal — uploads are already complete.
    await calendarPage.closeEventModal();
    await page.waitForTimeout(500);

    // 6. Reload the calendar (full page reload exercises the
    //    CalendarTaskResponseModel.Attachments DTO mapping).
    await page.reload();
    // The auth token typically survives reload via localStorage, so the
    // app re-renders the calendar without showing the login form. But on
    // slower CI runners the auth-store occasionally rehydrates AFTER the
    // login screen briefly flashes. Re-login only when the form actually
    // appears — otherwise the wait for #loginBtn would time out.
    if (await page.locator('#loginBtn').isVisible({ timeout: 3000 }).catch(() => false)) {
      await new LoginPage(page).login();
    }
    await page.waitForTimeout(2000);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();
    await calendarPage.selectProperty(property.name);
    await page.waitForTimeout(1500);
    // We were on next-week when we created the event; advance one week to
    // the same view as before. openCreateModalAtSlot already advanced once.
    await page.locator('mat-icon:has-text("chevron_right")').first().click();
    await page.waitForTimeout(1500);

    // 7. Re-open the event → expect 3 attachment rows.
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
    await calendarPage.findEventBlock(title).click();
    await page.locator('app-task-preview-modal').waitFor({ state: 'visible', timeout: 10000 });
    await calendarPage.getPreviewEditButton().click();
    await page.locator('#calendarEventTitle').waitFor({ state: 'visible', timeout: 15000 });

    await expect(page.locator('.gcal-attachment-row').filter({ hasNot: page.locator('mat-spinner') }))
      .toHaveCount(3, { timeout: 10000 });

    // 8. Delete the PDF row. Match by visible filename; accept the
    //    confirm() dialog programmatically.
    page.once('dialog', dialog => dialog.accept());
    const deleteResp = page.waitForResponse(
      r => /\/calendar\/tasks\/\d+\/files\/\d+$/.test(r.url())
        && r.request().method() === 'DELETE',
      { timeout: 30000 }
    );
    const pdfRow = page.locator('.gcal-attachment-row', { hasText: 'sample.pdf' });
    await pdfRow.locator('button.gcal-attachment-delete').click();
    await deleteResp;

    await expect(page.locator('.gcal-attachment-row').filter({ hasNot: page.locator('mat-spinner') }))
      .toHaveCount(2, { timeout: 10000 });

    // 9. Close + full reload → still 2 rows (PNG + JPG).
    await calendarPage.closeEventModal();
    await page.reload();
    // Re-login only if the form is actually shown — see step 6 above.
    if (await page.locator('#loginBtn').isVisible({ timeout: 3000 }).catch(() => false)) {
      await new LoginPage(page).login();
    }
    await page.waitForTimeout(2000);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();
    await calendarPage.selectProperty(property.name);
    await page.waitForTimeout(1500);
    await page.locator('mat-icon:has-text("chevron_right")').first().click();
    await page.waitForTimeout(1500);

    await calendarPage.findEventBlock(title).click();
    await page.locator('app-task-preview-modal').waitFor({ state: 'visible', timeout: 10000 });
    await calendarPage.getPreviewEditButton().click();
    await page.locator('#calendarEventTitle').waitFor({ state: 'visible', timeout: 15000 });

    const remainingRows = page.locator('.gcal-attachment-row').filter({ hasNot: page.locator('mat-spinner') });
    await expect(remainingRows).toHaveCount(2, { timeout: 10000 });
    await expect(page.locator('.gcal-attachment-row', { hasText: 'sample.png' })).toBeVisible();
    await expect(page.locator('.gcal-attachment-row', { hasText: 'sample.jpg' })).toBeVisible();
    await expect(page.locator('.gcal-attachment-row', { hasText: 'sample.pdf' })).toHaveCount(0);
  });

  // =======================================================================
  // J2: edit-mode immediate-upload — open an EXISTING event, attach a new
  //     file from the edit modal (not via pre-save staging), assert it
  //     appears immediately, close + reload, reopen the same event for
  //     edit, assert the newly-attached file is still listed alongside
  //     the J1 leftovers (sample.png, sample.jpg).
  //
  // Complements J1 by exercising the OTHER upload path: in J1 files were
  // staged before the event existed and uploaded post-create-POST; here
  // the event already exists, so the upload fires immediately on file
  // selection. Covers the Vedhæft fil link's edit-mode behavior end-to-end.
  // =======================================================================
  test('J2: edit-mode upload — open existing event, attach file, verify after reload', async ({ page }) => {
    test.setTimeout(180000);
    const calendarPage = new CalendarUiEnhancementsPage(page);
    // The J1 event title was randomized inside that test. Find any seeded
    // event with the J1- prefix; J2 runs serially after J1 so this is safe.
    const j1Block = page.locator('.task-block').filter({ hasText: /^J1-/ }).first();

    // Navigate forward one week to where J1 created its event.
    await page.locator('mat-icon:has-text("chevron_right")').first().click();
    await page.waitForTimeout(1500);
    await j1Block.waitFor({ state: 'visible', timeout: 10000 });

    // 1. Open the existing event in edit mode (preview popover → Edit button).
    await j1Block.click();
    await page.locator('app-task-preview-modal').waitFor({ state: 'visible', timeout: 10000 });
    await calendarPage.getPreviewEditButton().click();
    await page.locator('#calendarEventTitle').waitFor({ state: 'visible', timeout: 15000 });

    // 2. Confirm we're in edit mode. The Vedhæft fil link is always active
    //    in the modal (the staging flow makes it work in both create and
    //    edit modes); the visible-link assertion is just a sanity check.
    await expect(page.locator('#calendarEventAttachLink')).toBeVisible();

    // Snapshot the existing attachment count. J1 leaves at least 1 file
    // (PNG + JPG after deleting PDF). Don't hard-pin to 2 — keeps J2 robust
    // if J1's final delete count ever changes.
    const existingCount = await page.locator('.gcal-attachment-row')
      .filter({ hasNot: page.locator('mat-spinner') })
      .filter({ hasNot: page.locator('.gcal-attachment-pending-icon') })
      .count();
    expect(existingCount).toBeGreaterThanOrEqual(1);

    // 3. Trigger an immediate upload via the file picker. In edit-mode the
    //    upload fires right away (no pending-staging), so we wait for the
    //    POST to /tasks/{id}/files BEFORE selecting the file to avoid a race.
    const uploadResp = page.waitForResponse(
      r => /\/calendar\/tasks\/\d+\/files$/.test(r.url())
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await page.locator('#calendarEventAttachInput').setInputFiles([PDF_FIXTURE]);
    await uploadResp;

    // 4. The new row should appear within the modal without needing reload.
    //    Match by filename to be specific (sample.pdf came back after the
    //    J1 deletion removed it).
    await expect(page.locator('.gcal-attachment-row', { hasText: 'sample.pdf' }))
      .toBeVisible({ timeout: 10000 });
    await expect(page.locator('.gcal-attachment-row')
      .filter({ hasNot: page.locator('mat-spinner') })
      .filter({ hasNot: page.locator('.gcal-attachment-pending-icon') }))
      .toHaveCount(3, { timeout: 10000 });

    // 5. Close modal (no save needed — edit-mode uploads are independent
    //    of the modal's save lifecycle).
    await calendarPage.closeEventModal();
    await page.waitForTimeout(500);

    // 6. Full reload to verify the new attachment is persisted via the
    //    DTO mapper and survives a page round-trip.
    await page.reload();
    if (await page.locator('#loginBtn').isVisible({ timeout: 3000 }).catch(() => false)) {
      await new LoginPage(page).login();
    }
    await page.waitForTimeout(2000);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();
    await calendarPage.selectProperty(property.name);
    await page.waitForTimeout(1500);
    await page.locator('mat-icon:has-text("chevron_right")').first().click();
    await page.waitForTimeout(1500);

    // 7. Reopen the same event in edit mode and assert all 3 attachments.
    await page.locator('.task-block').filter({ hasText: /^J1-/ }).first().click();
    await page.locator('app-task-preview-modal').waitFor({ state: 'visible', timeout: 10000 });
    await calendarPage.getPreviewEditButton().click();
    await page.locator('#calendarEventTitle').waitFor({ state: 'visible', timeout: 15000 });

    const finalRows = page.locator('.gcal-attachment-row')
      .filter({ hasNot: page.locator('mat-spinner') })
      .filter({ hasNot: page.locator('.gcal-attachment-pending-icon') });
    await expect(finalRows).toHaveCount(3, { timeout: 10000 });
    await expect(page.locator('.gcal-attachment-row', { hasText: 'sample.pdf' })).toBeVisible();
    await expect(page.locator('.gcal-attachment-row', { hasText: 'sample.png' })).toBeVisible();
    await expect(page.locator('.gcal-attachment-row', { hasText: 'sample.jpg' })).toBeVisible();
  });
});
