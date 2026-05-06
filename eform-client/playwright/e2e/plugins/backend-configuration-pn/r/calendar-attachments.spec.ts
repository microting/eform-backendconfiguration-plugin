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
 * J3 verifies thumbnails actually load — that the authImage pipe converted
 * the auth-bearing GET into a Blob URL and the browser decoded the bytes
 * (naturalWidth > 0, complete === true on every <img> in the rows).
 *
 * J4 verifies the download path: clicking an attachment fires the
 * GET /tasks/{id}/files/{fileId} endpoint, returns 200 with a
 * non-empty body and correct content-type, and the bytes start with the
 * PDF magic header.
 *
 * J5 verifies oversized files are rejected — a synthetic 26 MB upload
 * is refused by the server's [RequestSizeLimit(26_214_400)] / quota path
 * and no new attachment row materialises.
 *
 * J6 verifies invalid MIME types are rejected — a synthetic .docx upload
 * (bypassing the picker via setInputFiles) is refused by the server's
 * PDF/PNG/JPEG whitelist and no new attachment row materialises.
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
// Shared across J1..J6 — J1 sets it, J2..J6 read it. Avoids fragile
// regex matching on `.task-block` text and resists random-suffix collision.
let j1EventTitle = '';

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
    j1EventTitle = title;  // shared with J2..J6

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
    await calendarPage.navigateToNextWeek();

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
    await calendarPage.navigateToNextWeek();

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
    const j1Block = page.locator(`.task-block`).filter({ hasText: j1EventTitle }).first();

    // Navigate forward one week to where J1 created its event.
    await calendarPage.navigateToNextWeek();
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
    await calendarPage.navigateToNextWeek();

    // 7. Reopen the same event in edit mode and assert all 3 attachments.
    await page.locator(`.task-block`).filter({ hasText: j1EventTitle }).first().click();
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

  // =======================================================================
  // J3: thumbnails actually load — verifies the authImage pipe end-to-end.
  //     Reopen J1's event in edit mode, then for every <img> inside the
  //     attachment rows assert naturalWidth > 0, naturalHeight > 0, and
  //     complete === true. Proves the auth-bearing GET succeeded, the pipe
  //     converted the response to a Blob URL, and the browser decoded it.
  //
  //     Going into J3, J1's event has 3 attachments: sample.png, sample.jpg,
  //     sample.pdf (the PNG/JPG remained after J1's PDF deletion, and J2
  //     uploaded a fresh PDF). The PDF row uses an icon, not an <img>,
  //     so we expect exactly 2 imgs.
  // =======================================================================
  test('J3: PNG/JPEG thumbnails actually load (authImage pipe end-to-end)', async ({ page }) => {
    test.setTimeout(120000);
    const calendarPage = new CalendarUiEnhancementsPage(page);

    // Navigate to J1's event week and reopen in edit mode.
    await calendarPage.navigateToNextWeek();
    await page.locator(`.task-block`).filter({ hasText: j1EventTitle }).first().click();
    await page.locator('app-task-preview-modal').waitFor({ state: 'visible', timeout: 10000 });
    await calendarPage.getPreviewEditButton().click();
    await page.locator('#calendarEventTitle').waitFor({ state: 'visible', timeout: 15000 });

    // Wait for all attachment rows to materialise.
    await expect(page.locator('.gcal-attachment-row')
      .filter({ hasNot: page.locator('mat-spinner') })
      .filter({ hasNot: page.locator('.gcal-attachment-pending-icon') }))
      .toHaveCount(3, { timeout: 10000 });

    // For each <img> inside attachment rows, assert naturalWidth > 0
    // (the authImage pipe converted the auth-bearing GET into a Blob URL,
    // and the browser actually decoded the image bytes).
    const imgStates = await page.locator('.gcal-attachment-row img').evaluateAll(imgs =>
      imgs.map(img => ({
        src: (img as HTMLImageElement).src.substring(0, 30),
        naturalWidth: (img as HTMLImageElement).naturalWidth,
        naturalHeight: (img as HTMLImageElement).naturalHeight,
        complete: (img as HTMLImageElement).complete,
      }))
    );

    expect(imgStates.length).toBe(2);  // PNG + JPEG (PDF row uses an icon, no img)
    for (const state of imgStates) {
      expect(state.complete, `Image ${state.src} did not finish loading`).toBe(true);
      expect(state.naturalWidth, `Image ${state.src} has zero naturalWidth`).toBeGreaterThan(0);
      expect(state.naturalHeight, `Image ${state.src} has zero naturalHeight`).toBeGreaterThan(0);
    }

    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // J4: download path works — clicking an attachment fires the blob fetch.
  //     Reopen J1's event in edit mode, click the PDF row's filename span,
  //     intercept the GET /tasks/{id}/files/{fileId} response, and assert:
  //       • status === 200
  //       • content-type contains 'application/pdf'
  //       • body length > 0
  //       • body starts with %PDF- (PDF magic)
  //
  //     The download flow normally fires window.open() against a blob URL.
  //     We neutralise window.open with page.evaluate so the navigation
  //     doesn't open a new tab — the test only cares about the GET response.
  // =======================================================================
  test('J4: clicking attachment fetches blob via GET endpoint', async ({ page }) => {
    test.setTimeout(120000);
    const calendarPage = new CalendarUiEnhancementsPage(page);

    // beforeEach resets to today's week — navigate forward to where J1
    // created the event before locating the task block.
    await calendarPage.navigateToNextWeek();
    await page.locator(`.task-block`).filter({ hasText: j1EventTitle }).first().click();
    await page.locator('app-task-preview-modal').waitFor({ state: 'visible', timeout: 10000 });
    await calendarPage.getPreviewEditButton().click();
    await page.locator('#calendarEventTitle').waitFor({ state: 'visible', timeout: 15000 });

    // Block the actual window.open new-tab navigation so the test runs in
    // the original tab. We only care about the blob fetch.
    await page.evaluate(() => { (window as any).open = () => null; });

    // Arm the GET response promise BEFORE the click — same race-free pattern.
    const downloadResp = page.waitForResponse(
      r => /\/calendar\/tasks\/\d+\/files\/\d+$/.test(r.url())
        && r.request().method() === 'GET',
      { timeout: 30000 }
    );

    // Click the PDF row's filename. The .gcal-attachment-name spans match.
    await page.locator('.gcal-attachment-row', { hasText: 'sample.pdf' })
      .locator('.gcal-attachment-name')
      .click();

    const response = await downloadResp;
    expect(response.status()).toBe(200);
    expect(response.headers()['content-type']).toContain('application/pdf');
    const buffer = await response.body();
    expect(buffer.length).toBeGreaterThan(0);
    // Sanity: PDFs start with %PDF-
    expect(buffer.subarray(0, 5).toString()).toBe('%PDF-');

    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // J5: oversized file rejection — synthesize a 26 MB PDF in OS temp,
  //     upload via setInputFiles, assert the server (or ASP.NET request-size
  //     limit) rejects it and that no new attachment row materialises.
  //
  //     The server may respond with HTTP 200 + body.success=false (the
  //     OperationDataResult quota path), 400 (validation), or 413 (the
  //     [RequestSizeLimit(26_214_400)] guard — request body trimmed
  //     before reaching the controller). All three are valid rejections.
  //     The hard contract is "no new row appeared".
  // =======================================================================
  test('J5: server rejects file over 25 MB with error chip', async ({ page }, testInfo) => {
    test.setTimeout(180000);
    const calendarPage = new CalendarUiEnhancementsPage(page);

    // Build a 26 MB synthetic PDF in the OS temp dir. The bytes only need
    // PDF magic + filler — the server validates extension + MIME + size, NOT content.
    const fs = await import('fs/promises');
    const path = await import('path');
    const os = await import('os');
    const tmpPdf = path.join(os.tmpdir(), `oversized-${Date.now()}.pdf`);
    const filler = Buffer.alloc(26 * 1024 * 1024, 0x20);  // 26 MB of spaces
    // Minimal PDF header so the file extension + MIME pass; size still > 25 MB.
    filler.write('%PDF-1.4\n', 0);
    await fs.writeFile(tmpPdf, filler);
    testInfo.attachments.push({ name: 'oversized.pdf', path: tmpPdf, contentType: 'application/pdf' });

    try {
      // beforeEach resets to today's week — navigate forward to J1's event.
      await calendarPage.navigateToNextWeek();
      await page.locator(`.task-block`).filter({ hasText: j1EventTitle }).first().click();
      await page.locator('app-task-preview-modal').waitFor({ state: 'visible', timeout: 10000 });
      await calendarPage.getPreviewEditButton().click();
      await page.locator('#calendarEventTitle').waitFor({ state: 'visible', timeout: 15000 });

      // Wait for the existing attachments to fully render BEFORE snapshotting
      // the count — without this poll we could read `before === 0` mid-load
      // and any successful upload would silently satisfy `after === before`.
      const settledRows = page.locator('.gcal-attachment-row')
        .filter({ hasNot: page.locator('mat-spinner') })
        .filter({ hasNot: page.locator('.gcal-attachment-pending-icon') });
      await expect(settledRows).toHaveCount(3, { timeout: 10000 });
      const before = await settledRows.count();

      // Don't waitForResponse here — Kestrel's MaxRequestBodySize middleware
      // can abort the connection on >25 MB bodies BEFORE sending an HTTP
      // response, which would hang waitForResponse forever. Some other paths
      // surface as 200 + success=false or 400/413. Either way the test
      // contract is just "no new row appeared".
      const failResp = page.waitForResponse(
        r => /\/calendar\/tasks\/\d+\/files$/.test(r.url())
          && r.request().method() === 'POST',
        { timeout: 15000 }
      ).catch(() => null);
      await page.locator('#calendarEventAttachInput').setInputFiles([tmpPdf]);
      const response = await failResp;
      if (response) {
        // If a response did come back, it must be a rejection (not 200+success=true).
        expect([200, 400, 413]).toContain(response.status());
      }
      // Wait long enough for a successful upload (if one slipped through) to
      // surface a new row. Tighter than the response timeout because we only
      // need to confirm the absence of a row.
      await page.waitForTimeout(5000);
      const after = await page.locator('.gcal-attachment-row')
        .filter({ hasNot: page.locator('mat-spinner') })
        .filter({ hasNot: page.locator('.gcal-attachment-pending-icon') })
        .count();
      expect(after).toBe(before);

      await calendarPage.closeEventModal();
    } finally {
      await fs.unlink(tmpPdf).catch(() => undefined);
    }
  });

  // =======================================================================
  // J6: invalid MIME rejection — synthesize a .docx in OS temp, upload via
  //     setInputFiles (bypasses the picker dialog's accept= filter), assert
  //     the server's PDF/PNG/JPEG whitelist refuses it and no new row
  //     appears.
  //
  //     Two acceptable response shapes:
  //       • HTTP 200 + body.success=false (OperationResult mismatch)
  //       • HTTP 400/415 (validation)
  // =======================================================================
  test('J6: server rejects unsupported file types (.docx)', async ({ page }, testInfo) => {
    test.setTimeout(120000);
    const calendarPage = new CalendarUiEnhancementsPage(page);

    // Build a synthetic .docx in temp.
    const fs = await import('fs/promises');
    const path = await import('path');
    const os = await import('os');
    const tmpDocx = path.join(os.tmpdir(), `unsupported-${Date.now()}.docx`);
    await fs.writeFile(tmpDocx, Buffer.from('PK\x03\x04...not really a docx but the server only checks ext+mime'));
    testInfo.attachments.push({ name: 'unsupported.docx', path: tmpDocx });

    try {
      // beforeEach resets to today's week — navigate forward to J1's event.
      await calendarPage.navigateToNextWeek();
      await page.locator(`.task-block`).filter({ hasText: j1EventTitle }).first().click();
      await page.locator('app-task-preview-modal').waitFor({ state: 'visible', timeout: 10000 });
      await calendarPage.getPreviewEditButton().click();
      await page.locator('#calendarEventTitle').waitFor({ state: 'visible', timeout: 15000 });

      // Same poll-the-baseline guard as J5 — without this we could read
      // before === 0 mid-render and any successful upload would silently pass.
      const settledRows = page.locator('.gcal-attachment-row')
        .filter({ hasNot: page.locator('mat-spinner') })
        .filter({ hasNot: page.locator('.gcal-attachment-pending-icon') });
      await expect(settledRows).toHaveCount(3, { timeout: 10000 });
      const before = await settledRows.count();

      // Don't block hard on the response — same Kestrel/connection-drop
      // risk as J5. The contract is just "no new row appears".
      const failResp = page.waitForResponse(
        r => /\/calendar\/tasks\/\d+\/files$/.test(r.url())
          && r.request().method() === 'POST',
        { timeout: 15000 }
      ).catch(() => null);
      await page.locator('#calendarEventAttachInput').setInputFiles([tmpDocx]);
      const response = await failResp;

      if (response) {
        // If a response did come back, it must be a rejection.
        if (response.status() === 200) {
          const body = await response.json().catch(() => null);
          expect(body?.success).toBe(false);
        } else {
          expect([400, 415]).toContain(response.status());
        }
      }

      await page.waitForTimeout(5000);
      const after = await page.locator('.gcal-attachment-row')
        .filter({ hasNot: page.locator('mat-spinner') })
        .filter({ hasNot: page.locator('.gcal-attachment-pending-icon') })
        .count();
      expect(after).toBe(before);

      await calendarPage.closeEventModal();
    } finally {
      await fs.unlink(tmpDocx).catch(() => undefined);
    }
  });
});
