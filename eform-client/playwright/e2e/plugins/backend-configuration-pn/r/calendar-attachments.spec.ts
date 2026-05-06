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
 * J1 covers the full upload/list/download/delete round-trip: attach a PDF +
 * PNG + JPG, reload the calendar, delete one, verify the remaining two are
 * still visible after a second reload.
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

const FIXTURE_DIR = path.resolve(__dirname, '../../../../fixtures/calendar-attachments');
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
  // J1: full attachment round-trip — create event, attach PDF + PNG + JPG,
  //     reload, delete the PDF, reload again, assert the two remaining
  //     attachments are still present.
  // =======================================================================
  test('J1: attach PDF + PNG + JPG to event, reload, delete one, verify', async ({ page }) => {
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

    // Sanity: in create-mode the Attach link is disabled (visible but
    // pointer-events handled at CSS level — assert the disabled class).
    const attachLink = page.locator('#calendarEventAttachInput');
    // The link itself isn't rendered in create-mode (separate disabled <a>);
    // the hidden <input> IS in the DOM via *ngIf="!isReadonly" logic, so
    // checking the disabled-class anchor is the right gate here.
    await expect(page.locator('a.gcal-action-link--disabled', { hasText: 'Vedhæft fil' }).or(
                  page.locator('a.gcal-action-link--disabled', { hasText: 'Attach file' }))).toBeVisible();

    // 2. Save the event. This POSTs /tasks and on success closes the modal.
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

    // 3. Re-open the event in edit-mode (where the Attach link is active).
    await calendarPage.findEventBlock(title).click();
    // Preview popover → click Edit. Reuse the existing helper.
    await page.locator('app-task-preview-modal').waitFor({ state: 'visible', timeout: 10000 });
    await calendarPage.getPreviewEditButton().click();
    await page.locator('#calendarEventTitle').waitFor({ state: 'visible', timeout: 15000 });

    // 4. Wire up response promises BEFORE setting files so we don't miss
    //    the upload POSTs (sequential — three of them).
    const upload1 = page.waitForResponse(
      r => /\/calendar\/tasks\/\d+\/files$/.test(r.url())
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    // Set all three files in one shot — uploadFiles() iterates sequentially.
    await page.locator('#calendarEventAttachInput').setInputFiles([
      PDF_FIXTURE, PNG_FIXTURE, JPG_FIXTURE,
    ]);
    await upload1;
    // Wait for the remaining two POSTs by polling DOM count instead of racing
    // multiple waitForResponse — sequentially-fired uploads can finish faster
    // than playwright can register a second listener, so DOM is the truth.
    await expect(page.locator('.gcal-attachment-row').filter({ hasNot: page.locator('mat-spinner') }))
      .toHaveCount(3, { timeout: 30000 });

    // 5. Close the modal — saves are unrelated to attachments (uploads are
    //    independent endpoint calls), so just close.
    await calendarPage.closeEventModal();
    await page.waitForTimeout(500);

    // 6. Reload the calendar (full page reload exercises the
    //    CalendarTaskResponseModel.Attachments DTO mapping).
    await page.reload();
    // After page.reload() the session token survives in localStorage, but the
    // app re-renders the login screen until the auth-store rehydrates.
    // LoginPage.login() short-circuits when the user-menu (already-authed
    // marker) is visible — call it without swallowing errors so a real auth
    // regression actually fails the test.
    await new LoginPage(page).login();
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
    // After page.reload() the session token survives in localStorage, but the
    // app re-renders the login screen until the auth-store rehydrates.
    // LoginPage.login() short-circuits when the user-menu (already-authed
    // marker) is visible — call it without swallowing errors so a real auth
    // regression actually fails the test.
    await new LoginPage(page).login();
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
});
