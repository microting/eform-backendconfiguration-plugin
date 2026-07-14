import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarPage } from '../l/calendar.page';
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
 * Calendar task-card (opgavekort) preview popover suite — covers the
 * extended preview card of 2026-07-14-calendar-task-card-design.md:
 *
 *   - Future card body rows in spec order, each led by a mat-icon ligature:
 *     schedule, repeat (ALWAYS rendered — "Does not repeat" for one-offs),
 *     domain (property), calendar_today (board + color dot), group
 *     (assignee/team chips), style (tag chips), stacks (report headline),
 *     checklist (eForm name), attach_file (attachment chips),
 *     toggle_on/toggle_off (status), policy (only when complianceEnabled).
 *     Rows with no value are HIDDEN (except repeat).
 *   - Tense-aware action row: future tasks render the edit pencil
 *     (#calendarEventEditBtn); HISTORICAL tasks (completed, or
 *     taskDate+endTime in the past) render #calendarEventViewBtn instead,
 *     which opens the same edit modal in readonly mode.
 *   - Completed cards: group row becomes "Completed by"/"Udført af" +
 *     completer name; toggle/policy rows omitted; Picture-field thumbnails
 *     in an image row → CalendarImageLightboxComponent.
 *   - Live refresh: after an edit-save the tile and a re-opened card show
 *     fresh values WITHOUT a page reload (container loadTasks re-render).
 *
 * REALITY CHECK (drives which rows are real vs. test.fixme — mirrors the
 * conventions established in p/calendar-complete.spec.ts and
 * t/calendar-edit-fields.spec.ts E17):
 *
 *  1. An UNCOMPLETED PAST task cannot be produced through the UI:
 *     - past grid slots are inert (v/calendar-create-validation.spec.ts V03
 *       asserts the create modal never opens on a past slot);
 *     - the create modal's datepicker has minDate = today, and the backend
 *       CreateTask rejects past starts outright
 *       ("CannotCreateTaskInThePast" guard in
 *       BackendConfigurationCalendarService.CreateTask);
 *     - edit-save is blocked for past dates (isInPast guard in onSave) and
 *       the move/resize endpoints silently reject moves into the past.
 *     → the past-card tests (KC05) are test.fixme, same as E17.
 *
 *  2. A COMPLETED task cannot be produced through the UI: completing an
 *     event opens the combined complete modal
 *     (app-calendar-complete-event-modal) whose embedded eForm carries
 *     mandatory fields for every template the seed provisions — saving it
 *     (#completeSaveBtn) is not automatable (see
 *     p/calendar-complete.spec.ts header, X03/X07/X09). The completed-card
 *     behaviors ("Udført af" group row, no toggle/policy rows, image
 *     thumbnails + lightbox) are covered at component level; server-side
 *     coverage lives in CalendarCompleteOccurrenceTests.cs.
 *     → KC06 is test.fixme with the intended assertions documented.
 *
 *  3. Because neither a completed case nor Picture-field data is
 *     producible in e2e, lightbox coverage (ids calendarLightbox,
 *     calendarLightboxPrev / calendarLightboxNext / calendarLightboxClose,
 *     calendarLightboxImage; wrap-around navigation; arrows hidden for a
 *     single image) stays component-level. What IS asserted here: the
 *     image row (icon `image`) is absent on every reachable card.
 *
 * Lives in `m/` — the lightest calendar matrix slot (shares with the
 * fresh-property-worker and translation suites). Seeds its own property +
 * worker like every calendar suite (only shard `a` seeds SQL).
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

// KC01 creates this event; KC04 edits it (serial suite state).
const kc01Title = `KC01-${generateRandmString(5)}`;
// Planning tag doubles as report headline (stacks row) AND set-tag chip
// (style row) — calendar tags and planning tags share the same pool.
const kc01Tag = `KCTag-${generateRandmString(5)}`;

let seeded = false;
let kc01EformLabel = '';

test.describe.serial('Calendar task-card preview (task-card-preview-fields)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);

    const uiPage = new CalendarUiEnhancementsPage(page);
    await uiPage.goToCalendar();
    await uiPage.ensureSidebarOpen();

    if (seeded) {
      const folderResp = page.waitForResponse(
        r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
        { timeout: 60000 }
      );
      await uiPage.selectProperty(property.name);
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
  // KC01 — future card shows every populated row in spec order and hides
  //   the empty ones.
  //
  //   Creates a one-off next-week Monday 09:00 event with: an eForm, an
  //   inline-created planning tag (report headline) that is ALSO added as a
  //   set-tag, and the seeded worker as assignee — then asserts the card:
  //     schedule row (date + 09:00), repeat row ("Does not repeat" — always
  //     rendered), domain row (property name), calendar_today row (board
  //     name + color dot), group row (worker chip), style row (tag chip),
  //     stacks row (planning-tag name), checklist row (eForm label);
  //     attach_file + image rows ABSENT (no value); toggle_on row (status
  //     defaults active); policy row PRESENT (complianceEnabled defaults
  //     true). Action row shows the edit pencil, NOT the historical View
  //     button.
  // =======================================================================
  test('KC01: future card shows populated rows and hides empty ones', async ({ page }) => {
    test.setTimeout(300000);

    const uiPage = new CalendarUiEnhancementsPage(page);
    const calendarPage = new CalendarPage(page);

    // Monday 09:00 next week (openCreateModalAtSlot advances one week, so
    // the slot is guaranteed future regardless of CI wall-clock).
    await uiPage.openCreateModalAtSlot(0, 9);

    await page.locator('#calendarEventTitle').fill(kc01Title);

    // eForm: first option; capture its label for the checklist-row assert.
    const eform = page.locator('#calendarEventEform');
    await eform.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);
    kc01EformLabel = await calendarPage.getSelectValue('#calendarEventEform');
    expect(kc01EformLabel).toBeTruthy();

    // Planning tag (report headline): inline-create so we know its exact
    // name, then add the SAME tag to the set-tags multi-select (the two
    // fields share the tag pool — see l/calendar-tags-roundtrip.spec.ts).
    await calendarPage.inlineCreatePlanningTag(kc01Tag);
    await calendarPage.addExistingTag(kc01Tag);

    // Assignee: the seeded worker (only option).
    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    const createResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
        && !r.url().includes('/tasks/week')
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await createResp;
    await page.waitForTimeout(1500);
    await uiPage.findEventBlock(kc01Title).waitFor({ state: 'visible', timeout: 10000 });

    // ----- Open the card and assert the tense-aware action row ------------
    await uiPage.openEventPreview(kc01Title);
    await expect(calendarPage.getPreviewEditButton()).toBeVisible();
    await expect(calendarPage.getPreviewViewButton()).toHaveCount(0);

    expect(await calendarPage.getPreviewTitleText()).toContain(kc01Title);

    // schedule — localized "Weekday D. Month YYYY; 09:00 – 10:00". Assert
    // the stable tokens (year of next week's Monday + the 09:00 start)
    // rather than the locale-dependent weekday/month names.
    const now = new Date();
    const nextMonday = new Date(now);
    nextMonday.setDate(now.getDate() - ((now.getDay() + 6) % 7) + 7);
    const scheduleRow = calendarPage.getPreviewRowByIcon('schedule');
    await expect(scheduleRow).toBeVisible();
    await expect(scheduleRow).toContainText(String(nextMonday.getFullYear()));
    await expect(scheduleRow).toContainText('09:00');

    // repeat — ALWAYS rendered; one-off shows the "Does not repeat"
    // translation (da: "Gentages ikke").
    const repeatRow = calendarPage.getPreviewRowByIcon('repeat');
    await expect(repeatRow).toBeVisible();
    await expect(repeatRow).toContainText(/Does not repeat|Gentages ikke/);

    // domain — property name.
    await expect(calendarPage.getPreviewRowByIcon('domain')).toContainText(property.name as string);

    // calendar_today — board name + color dot (every property has an
    // auto-created default board; we assert presence, not its name).
    const boardRow = calendarPage.getPreviewRowByIcon('calendar_today');
    await expect(boardRow).toBeVisible();
    await expect(boardRow.locator('.preview-board-dot')).toBeVisible();

    // group — assignee chip with the seeded worker (future card: chips,
    // never the "Completed by" variant).
    const groupRow = calendarPage.getPreviewRowByIcon('group');
    await expect(groupRow).toBeVisible();
    await expect(groupRow.locator('.preview-chip').first()).toContainText(worker.name as string);

    // style — the set-tag chip.
    const tagsRow = calendarPage.getPreviewRowByIcon('style');
    await expect(tagsRow).toBeVisible();
    expect(await calendarPage.getPreviewChipTexts('style')).toContain(kc01Tag);

    // stacks — report headline resolves to the planning tag's name.
    await expect(calendarPage.getPreviewRowByIcon('stacks')).toContainText(kc01Tag);

    // checklist — eForm name resolved via the container's eForm list (loads
    // async on card-open, so rely on the assertion's retry).
    await expect(calendarPage.getPreviewRowByIcon('checklist')).toContainText(kc01EformLabel, { timeout: 10000 });

    // Empty rows are hidden: no attachments → no attach_file row; not a
    // completed card → no image (case thumbnails) row.
    await expect(calendarPage.getPreviewRowByIcon('attach_file')).toHaveCount(0);
    await expect(calendarPage.getPreviewRowByIcon('image')).toHaveCount(0);

    // toggle — status defaults ACTIVE → toggle_on row (and no toggle_off).
    await expect(calendarPage.getPreviewRowByIcon('toggle_on')).toBeVisible();
    await expect(calendarPage.getPreviewRowByIcon('toggle_off')).toHaveCount(0);

    // policy — complianceEnabled defaults true → row present on a future card.
    await expect(calendarPage.getPreviewRowByIcon('policy')).toBeVisible();

    await calendarPage.closePreviewPopover();
  });

  // =======================================================================
  // KC02 — the policy row is gated on the overdue/compliance toggle, and
  //   value-less optional rows stay hidden.
  //
  //   Creates a plain event (no set-tags, no attachments) with
  //   #calendarEventComplianceOff picked in the create modal
  //   (onPickOverdueHidden → status stays/becomes active,
  //   complianceEnabled=false — see t/calendar-edit-fields.spec.ts) and
  //   asserts: policy row ABSENT, toggle_on row present, style +
  //   attach_file + image rows ABSENT, repeat row still present.
  // =======================================================================
  test('KC02: compliance-off card hides the policy row; empty optional rows absent', async ({ page }) => {
    test.setTimeout(300000);

    const uiPage = new CalendarUiEnhancementsPage(page);
    const calendarPage = new CalendarPage(page);
    const title = `KC02-${generateRandmString(5)}`;

    // Tuesday 09:00 next week — distinct slot from KC01 (Monday).
    await uiPage.openCreateModalAtSlot(1, 9);

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

    // Overdue policy OFF (also forces status active).
    await page.locator('#calendarEventComplianceOff').click();
    await page.waitForTimeout(300);

    const createResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
        && !r.url().includes('/tasks/week')
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await createResp;
    await page.waitForTimeout(1500);
    await uiPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });

    await uiPage.openEventPreview(title);

    // Repeat row is unconditional; schedule row present.
    await expect(calendarPage.getPreviewRowByIcon('schedule')).toBeVisible();
    await expect(calendarPage.getPreviewRowByIcon('repeat')).toBeVisible();

    // complianceEnabled=false → NO policy row.
    await expect(calendarPage.getPreviewRowByIcon('policy')).toHaveCount(0);

    // The compliance radio forces status active → toggle_on.
    await expect(calendarPage.getPreviewRowByIcon('toggle_on')).toBeVisible();
    await expect(calendarPage.getPreviewRowByIcon('toggle_off')).toHaveCount(0);

    // No set-tags, no attachments, not completed → rows hidden.
    await expect(calendarPage.getPreviewRowByIcon('style')).toHaveCount(0);
    await expect(calendarPage.getPreviewRowByIcon('attach_file')).toHaveCount(0);
    await expect(calendarPage.getPreviewRowByIcon('image')).toHaveCount(0);

    await calendarPage.closePreviewPopover();
  });

  // =======================================================================
  // KC03 — an INACTIVE (dimmed) event's card shows the toggle_off row.
  // =======================================================================
  test('KC03: status-inactive card shows the toggle_off row', async ({ page }) => {
    test.setTimeout(300000);

    const uiPage = new CalendarUiEnhancementsPage(page);
    const calendarPage = new CalendarPage(page);
    const title = `KC03-${generateRandmString(5)}`;

    // Wednesday 09:00 next week.
    await uiPage.openCreateModalAtSlot(2, 9);

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

    // Status → inactive (task dims on the grid and hides in the app).
    await page.locator('#calendarEventStatusInactive').click();
    await page.waitForTimeout(300);

    const createResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
        && !r.url().includes('/tasks/week')
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await createResp;
    await page.waitForTimeout(1500);
    await uiPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });

    await uiPage.openEventPreview(title);

    await expect(calendarPage.getPreviewRowByIcon('toggle_off')).toBeVisible();
    await expect(calendarPage.getPreviewRowByIcon('toggle_on')).toHaveCount(0);

    await calendarPage.closePreviewPopover();
  });

  // =======================================================================
  // KC04 — LIVE REFRESH: edit KC01's event (new title + planning tag added
  //   as a set-tag) and, WITHOUT page.reload(), assert both the grid tile
  //   and a re-opened card show the fresh values. The refresh is driven by
  //   the container's post-save loadTasks (/tasks/week POST) re-render.
  // =======================================================================
  test('KC04: tile and re-opened card show edited values without a page reload', async ({ page }) => {
    test.setTimeout(300000);

    const uiPage = new CalendarUiEnhancementsPage(page);
    const calendarPage = new CalendarPage(page);
    // Deliberately NOT containing kc01Title, so the old-tile-gone assert
    // can't accidentally match the new tile.
    const newTitle = `KC04-${generateRandmString(5)}`;
    const newTag = `KCTag-${generateRandmString(5)}`;

    // KC01's event lives on next week's Monday.
    await uiPage.navigateToNextWeek();
    await uiPage.findEventBlock(kc01Title).waitFor({ state: 'visible', timeout: 10000 });

    // Open the card → edit modal (tense-aware click helper).
    await uiPage.openEventPreview(kc01Title);
    await calendarPage.clickEditOrViewInPreview();

    // New title + a second inline-created planning tag, also added as a
    // set-tag (same dual-pool mechanics as KC01).
    await page.locator('#calendarEventTitle').fill(newTitle);
    await calendarPage.inlineCreatePlanningTag(newTag);
    await calendarPage.addExistingTag(newTag);

    // One-off event → the save PUTs directly (no repeat-scope modal).
    // Register BOTH waiters before clicking save so neither can miss.
    const putWait = page.waitForResponse(
      r => r.url().endsWith('/api/backend-configuration-pn/calendar/tasks')
        && r.request().method() === 'PUT',
      { timeout: 30000 }
    );
    const reloadWait = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/week')
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await putWait;
    await reloadWait;
    await page.waitForTimeout(800);

    // NO page.reload() — the re-rendered grid must already show the new
    // title, and the old title must be gone.
    await uiPage.findEventBlock(newTitle).waitFor({ state: 'visible', timeout: 10000 });
    await expect(page.locator('.task-block').filter({ hasText: kc01Title })).toHaveCount(0);

    // Re-open the card: fresh title, fresh tag chip, fresh report headline.
    await uiPage.openEventPreview(newTitle);
    expect(await calendarPage.getPreviewTitleText()).toContain(newTitle);
    expect(await calendarPage.getPreviewChipTexts('style')).toContain(newTag);
    await expect(calendarPage.getPreviewRowByIcon('stacks')).toContainText(newTag);
    // Still a future one-off card: edit pencil, toggle + policy rows intact.
    await expect(calendarPage.getPreviewEditButton()).toBeVisible();
    await expect(calendarPage.getPreviewViewButton()).toHaveCount(0);

    await calendarPage.closePreviewPopover();
  });

  // =======================================================================
  // KC05 — HISTORICAL (past, uncompleted) card: #calendarEventViewBtn
  //   replaces #calendarEventEditBtn; clicking it opens the edit modal in
  //   readonly mode; toggle/policy rows are omitted; the group row still
  //   shows the planned assignees (Q2 — "Udført af" is completed-only).
  //
  //   fixme rationale: an uncompleted PAST event cannot be produced via the
  //   UI — past grid slots are inert (v/ V03), the create modal's
  //   datepicker floors at today, the backend CreateTask guard rejects past
  //   starts ("CannotCreateTaskInThePast",
  //   BackendConfigurationCalendarService.CreateTask), and move/resize into
  //   the past is silently rejected server-side. Same conclusion as
  //   t/calendar-edit-fields.spec.ts E17. The isHistorical branch
  //   (completed || taskDate+endTime < now, mirroring the edit modal's
  //   isInPast rule) is covered at component level.
  // =======================================================================
  test.fixme('KC05: past card shows the View button and opens the readonly modal', async () => {
    // Intended (unreachable — see fixme rationale above): produce an event
    // whose taskDate+endTime is in the past, then:
    //   await uiPage.openEventPreview(title);
    //   await expect(calendarPage.getPreviewViewButton()).toBeVisible();
    //   await expect(calendarPage.getPreviewEditButton()).toHaveCount(0);
    //   await expect(calendarPage.getPreviewRowByIcon('toggle_on')).toHaveCount(0);
    //   await expect(calendarPage.getPreviewRowByIcon('toggle_off')).toHaveCount(0);
    //   await expect(calendarPage.getPreviewRowByIcon('policy')).toHaveCount(0);
    //   // Q2: uncompleted historical → planned assignee chips, no "Udført af".
    //   await expect(calendarPage.getPreviewRowByIcon('group').locator('.preview-chip')).toHaveCount(1);
    //   await calendarPage.clickEditOrViewInPreview();
    //   // Readonly modal: all controls disabled.
    //   await expect(page.locator('#calendarEventTitle')).toBeDisabled();
  });

  // =======================================================================
  // KC06 — COMPLETED card: group row shows "Udført af"/"Completed by" +
  //   completer name, no toggle/policy rows, image row with case-photo
  //   thumbnails → lightbox (ids calendarLightbox, calendarLightboxPrev,
  //   calendarLightboxNext, calendarLightboxClose, calendarLightboxImage;
  //   wrap-around ‹ ›, arrows hidden for a single image).
  //
  //   fixme rationale: a completed task cannot be produced in e2e — saving
  //   the combined complete modal requires driving its embedded eForm's
  //   mandatory fields (impractical/flaky; see p/calendar-complete.spec.ts
  //   header + X03/X09, which are fixme for the same reason). A completed
  //   case WITH Picture-field data is doubly unreachable (no spec in the
  //   suite produces picture data — the w/ attachment uploads are event
  //   file attachments, not case photos). Completed-card rendering and the
  //   lightbox stay component-level; DoneBy/DoneAt population is covered
  //   server-side (CalendarTaskResponseModel.DoneByName fill in
  //   GetTasksForWeek). The absence of the image row on non-completed cards
  //   IS asserted (KC01–KC03).
  // =======================================================================
  test.fixme('KC06: completed card shows "Udført af" + thumbnails and no toggle/policy rows', async () => {
    // Intended (unreachable — see fixme rationale above): fully complete an
    // event via `.completion-btn` + app-calendar-complete-event-modal save
    // (#completeWorkerSelect / #completeDoneAt / #completeSaveBtn), then:
    //   await uiPage.openEventPreview(title);
    //   await expect(calendarPage.getPreviewViewButton()).toBeVisible();
    //   await expect(calendarPage.getPreviewRowByIcon('group'))
    //     .toContainText(/Udført af|Completed by/);
    //   await expect(calendarPage.getPreviewRowByIcon('group')).toContainText(worker.name);
    //   await expect(calendarPage.getPreviewRowByIcon('toggle_on')).toHaveCount(0);
    //   await expect(calendarPage.getPreviewRowByIcon('toggle_off')).toHaveCount(0);
    //   await expect(calendarPage.getPreviewRowByIcon('policy')).toHaveCount(0);
    //   // With case photos: thumbnails → lightbox
    //   await calendarPage.getPreviewRowByIcon('image').locator('.preview-thumb').first().click();
    //   await expect(page.locator('#calendarLightbox')).toBeVisible();
    //   await page.locator('#calendarLightboxNext').click();   // wrap-around
    //   await page.locator('#calendarLightboxClose').click();
  });
});
