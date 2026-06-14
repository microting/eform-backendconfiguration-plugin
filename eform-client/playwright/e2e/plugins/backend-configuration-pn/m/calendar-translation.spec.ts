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
 * Calendar create/edit task-modal TRANSLATION suite.
 *
 * Feature under test (task-create-edit-modal.component.{html,ts}):
 *   - A translate icon (#calendarEventTitleTranslate / #calendarEventDescriptionTranslate,
 *     gated by `*ngIf="showTranslate"`) appears next to Title/Description ONLY when
 *     the assigned property-workers resolve to a non-Danish target language.
 *     `showTranslate` is `targetLanguages.length > 0`, and `targetLanguages` is
 *     derived from the selected assignees' site languageIds, dropping Danish (id 1).
 *   - Pressing the icon reveals the per-language fields (`.gcal-translate-fields`)
 *     and auto-fills each from the backend translation endpoint.
 *   - The e2e backend runs with a SENTINEL Google key, so `api/get-translation`
 *     returns deterministic fake text `"[<targetLangCode>] <sourceText>"`
 *     (e.g. "[de-DE] Tank 4") and `api/translation-possible` returns true. No
 *     page.route stubbing is required.
 *   - On save, the create POST body carries a `translates` array of
 *     {name, description, languageId}; Danish (languageId 1) is always present,
 *     Deutsch is languageId 3.
 *
 * Seeds ONE property + TWO property-workers (one Deutsch, one Dansk) so the
 * Danish-only vs +Deutsch distinction is observable. Mirrors the seed +
 * login + property-selection pattern of o/calendar-create-fields.spec.ts and
 * the shard-m worker seeding of m/calendar-fresh-property-worker.spec.ts.
 *
 * Lives in shard `m/`. Uses describe.serial so the seed test runs first and
 * the two assertion tests reuse the seeded property/workers.
 */

const property: PropertyCreateUpdate = {
  name: generateRandmString(5),
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// Worker assigned a non-Danish language → drives showTranslate / targetLanguages.
// 'Deutsch' resolves to the German site language (languageId 3, code de-DE).
const workerDe: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Deutsch',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};

// Danish worker → contributes NO target language (Danish id 1 is the source
// and is always dropped from targetLanguages), so assigning only this worker
// keeps the translate icon hidden.
const workerDa: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};

let seeded = false;

// Predicate: the create backend call (POST .../calendar/tasks) excluding the
// week reload + move/resize sibling routes. Same shape as the model spec.
function isCreatePost(method: string, url: string): boolean {
  return (
    url.includes('/api/backend-configuration-pn/calendar/tasks') &&
    !url.includes('/tasks/week') &&
    !url.includes('/tasks/move') &&
    !url.includes('/tasks/resize') &&
    method === 'POST'
  );
}

test.describe.serial('Calendar task-modal translation', () => {
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
  // Local helpers
  // -----------------------------------------------------------------------

  // Fill the required fields (first eForm + first planning tag) in the
  // already-open create modal. Does NOT set the title or assignee — callers
  // control those for the translate-specific assertions. Does NOT save.
  async function fillEformAndPlanningTag(
    page: import('@playwright/test').Page,
  ): Promise<void> {
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
  }

  // Pick an assignee BY WORKER NAME (not .nth) so the Danish-only vs +Deutsch
  // distinction is deterministic. The assignee option label is the worker's
  // eForm site name ("<firstName> <surname>"), so matching on the unique
  // random first-name token selects exactly the intended worker. Opens the
  // panel each call (the multi-select keeps the panel after a pick, but
  // re-opening is harmless and keeps the call self-contained).
  async function pickAssigneeByName(
    page: import('@playwright/test').Page,
    name: string,
  ): Promise<void> {
    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page
      .locator('.ng-dropdown-panel .ng-option')
      .filter({ hasText: name })
      .first()
      .click();
    await page.waitForTimeout(300);
    // Close the dropdown without mutating other state.
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(200);
  }

  // Open the create modal at (dayOffset, hour), scrolling the week grid to the
  // top first. clickEmptyTimeSlot clicks a raw (x,y) computed from the day
  // column's box + hour*65px; if the grid is auto-scrolled to the current time
  // (e.g. afternoon), an early hour lands off-grid and no modal opens. Scrolling
  // to 00:00 makes the coordinate deterministic regardless of wall-clock time.
  async function openCreateAt(
    page: import('@playwright/test').Page,
    calendarPage: CalendarUiEnhancementsPage,
    dayOffset: number,
    hour: number,
  ): Promise<void> {
    await page.locator('mat-icon:has-text("chevron_right")').first().click();
    await page.waitForTimeout(1500);
    await page.evaluate(() => {
      const w = document.querySelector('.week-grid-wrapper') as HTMLElement | null;
      if (w) { w.scrollTop = 0; }
    });
    await page.waitForTimeout(300);
    await calendarPage.clickEmptyTimeSlot(dayOffset, hour);
    await page.locator('#calendarEventTitle').waitFor({ state: 'visible', timeout: 15000 });
    await page.waitForTimeout(300);
  }

  // The per-language field wrapper whose mat-label contains "(<langName>)".
  // The inputs/textarea inside .gcal-translatable-field have NO id, so we
  // locate by the language token in the label and then reach the control.
  function translatableFieldByLang(
    page: import('@playwright/test').Page,
    langName: string,
  ): import('@playwright/test').Locator {
    return page
      .locator('.gcal-translate-fields .gcal-translatable-field')
      .filter({ hasText: `(${langName})` });
  }

  // Capture the create POST request body, click save, and assert a 200
  // response. Returns the parsed request body for field assertions. Registers
  // both waiters BEFORE clicking save so neither can miss the round-trip.
  async function saveAndCaptureCreate(
    page: import('@playwright/test').Page,
  ): Promise<any> {
    const reqPromise = page.waitForRequest(
      r => isCreatePost(r.method(), r.url()),
      { timeout: 30000 }
    );
    const respPromise = page.waitForResponse(
      r => isCreatePost(r.request().method(), r.url()),
      { timeout: 30000 }
    );
    // The expanded per-language fields leave an animating element overlapping
    // the save button, so a coordinate-based click never lands on it (Playwright
    // reports it as "not stable"). The button is enabled (title valid + a worker
    // assigned), so dispatch the click event directly to fire onSave().
    // NOTE: dispatchEvent bypasses Playwright's disabled/actionability checks.
    // If reqPromise below times out, first confirm titleControl is valid (title
    // filled) and a worker is assigned — onSave() returns early otherwise.
    const saveBtn = page.locator('#calendarEventSaveBtn');
    await saveBtn.scrollIntoViewIfNeeded();
    await saveBtn.dispatchEvent('click');
    const req = await reqPromise;
    const resp = await respPromise;
    expect(resp.status(), 'create POST should return 200').toBe(200);
    return req.postDataJSON();
  }

  // -----------------------------------------------------------------------
  // Seed test — property + TWO workers (Deutsch + Dansk). Runs first.
  // -----------------------------------------------------------------------
  test('seed: create property + Deutsch + Dansk workers', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(workerDe);
    await workersPage.create(workerDa);

    seeded = true;
    // The page-object create() calls assert success internally; the assignee
    // dropdown is exercised by the assertion tests below (which open the modal),
    // so no redundant modal-open sanity check here.
  });

  // =======================================================================
  // Translate icon visibility tracks the assignees' languages.
  // =======================================================================
  test('translate icon visibility: hidden for Danish-only, shown when a Deutsch worker is assigned', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `TR-VIS-${generateRandmString(5)}`;

    // Monday 09:00 (next week).
    await openCreateAt(page, calendarPage,0, 9);
    await page.locator('#calendarEventTitle').fill(title);

    const titleTranslate = page.locator('#calendarEventTitleTranslate');

    // No assignee yet → no target language → icon hidden.
    await expect(titleTranslate).toBeHidden();

    // Assign ONLY the Danish worker → Danish is the source (id 1) and is
    // dropped from targetLanguages, so still no target → icon stays hidden.
    await pickAssigneeByName(page, workerDa.name as string);
    await expect(
      titleTranslate,
      'translate icon must stay hidden when only a Danish worker is assigned'
    ).toBeHidden();

    // Also assign the Deutsch worker → a non-Danish target language now
    // exists → icon becomes visible.
    await pickAssigneeByName(page, workerDe.name as string);
    await expect(
      titleTranslate,
      'translate icon must appear once a non-Danish (Deutsch) worker is assigned'
    ).toBeVisible();

    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // Auto-fill from backend + save translates[] + edit-mode prefill.
  // =======================================================================
  test('auto-fill + save + edit prefill for a Deutsch assignee', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = 'Tank 4';
    const description = 'Check it';

    // Tuesday 09:00 (next week).
    await openCreateAt(page, calendarPage,1, 9);
    await page.locator('#calendarEventTitle').fill(title);
    await page.locator('#calendarEventDescription').fill(description);
    await fillEformAndPlanningTag(page);

    // Assign the Deutsch worker → translate icons appear.
    await pickAssigneeByName(page, workerDe.name as string);
    await expect(page.locator('#calendarEventTitleTranslate')).toBeVisible();
    await expect(page.locator('#calendarEventDescriptionTranslate')).toBeVisible();

    // Click the Title translate icon → the Deutsch title field auto-fills from
    // the sentinel backend: "[de-DE] Tank 4".
    await page.locator('#calendarEventTitleTranslate').click();
    const deTitleInput = translatableFieldByLang(page, 'Deutsch').locator('input[matInput]').first();
    await expect(deTitleInput).toHaveValue('[de-DE] Tank 4', { timeout: 15000 });

    // Click the Description translate icon → the Deutsch description field
    // auto-fills: "[de-DE] Check it".
    await page.locator('#calendarEventDescriptionTranslate').click();
    const deDescTextarea = translatableFieldByLang(page, 'Deutsch').locator('textarea[matInput]').first();
    await expect(deDescTextarea).toHaveValue('[de-DE] Check it', { timeout: 15000 });

    // Save and inspect the create POST body.
    const body = await saveAndCaptureCreate(page);
    console.log(`translation POST body translates=${JSON.stringify(body?.translates)}`);

    const translates: Array<{ name: string; description: string; languageId: number }> =
      Array.isArray(body?.translates) ? body.translates : [];

    // Danish (languageId 1) source entry is always present with the raw title.
    const danish = translates.find(t => t.languageId === 1);
    expect(danish, 'translates[] must include the Danish (languageId 1) source entry').toBeTruthy();
    expect(danish?.name, 'Danish entry carries the untranslated title').toBe('Tank 4');

    // Deutsch (languageId 3) entry carries the auto-translated values.
    const deutsch = translates.find(t => t.languageId === 3);
    expect(deutsch, 'translates[] must include the Deutsch (languageId 3) entry').toBeTruthy();
    expect(deutsch?.name, 'Deutsch entry carries the translated title').toBe('[de-DE] Tank 4');
    expect(deutsch?.description, 'Deutsch entry carries the translated description').toBe('[de-DE] Check it');

    // The created event renders on the grid; reopen it in edit mode and assert
    // the Deutsch title field is prefilled from the persisted translation.
    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });

    await calendarPage.openEditModal(title);
    // In edit mode the translate fields auto-expand when a target translation
    // exists (descTranslateExpanded/titleTranslateExpanded), so the Deutsch
    // title input should be present and prefilled without re-clicking.
    const editDeTitle = translatableFieldByLang(page, 'Deutsch').locator('input[matInput]').first();
    await expect(editDeTitle, 'edit mode prefills the Deutsch title field').toHaveValue(
      '[de-DE] Tank 4',
      { timeout: 15000 }
    );

    await calendarPage.closeEventModal();
  });
});
