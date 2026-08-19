import { test, expect, Page, Request, Response } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import { CalendarPage } from '../l/calendar.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';

/**
 * Calendar eForm-change propagation regression suite.
 *
 * Spec: docs/superpowers/specs/2026-08-19-calendar-eform-change-propagation-design.md
 *
 * THE BUG (fixed by that spec):
 *   Creating a calendar event with eForm A and then editing it to eForm B
 *   updated everything the calendar *displays* (`AreaRule.EformId`, which every
 *   read projection maps to `task.eformId`) but left the already-DEPLOYED rows
 *   frozen at A: `PlanningCase.MicrotingSdkeFormId`,
 *   `PlanningCaseSite.MicrotingSdkeFormId`, `Compliance.MicrotingSdkeFormId`
 *   and — the one that decides what the worker actually fills in — the SDK
 *   `Cases.CheckListId`. Completion resolves
 *   `Compliance.Id -> Compliance.MicrotingSdkCaseId -> Cases.CheckListId` and
 *   returns it as `PrepareComplete.templateId`, so the COMPLETE flow kept
 *   opening A while the calendar showed B.
 *   The fix adds `EventDeployService.RepairEformForOpenOccurrencesAsync`, which
 *   retracts and redeploys every non-completed occurrence with the new eForm
 *   *in place* — the same `Compliance.Id` gets a NEW `MicrotingSdkCaseId` and
 *   the new `MicrotingSdkeFormId` (the row is never deleted and recreated,
 *   because the calendar UI holds `complianceId`).
 *
 * WHY THE FIRST COMPLETION CLICK IS PART OF THE FIXTURE (do not remove it):
 *   An occurrence that was never deployed materialises on demand at completion
 *   time and therefore *already* completed with the new eForm before the fix —
 *   asserting on such an occurrence would pass against the OLD behaviour and
 *   pin nothing. EFC01 therefore FORCES a deployment with eForm A first, by
 *   clicking `.completion-btn` once and cancelling the modal: `PrepareComplete`
 *   calls `EnsureComplianceForOccurrenceAsync` synchronously inside the
 *   POST `/tasks/{id}/prepare-complete`, and cancelling leaves the freshly
 *   materialised Compliance row + SDK case OPEN. This is the same
 *   materialise-then-cancel fixture `r/calendar-compliance-view.spec.ts` uses.
 *   Only after that does the spec swap the eForm — so the assertions run
 *   against an occurrence that WAS deployed with A, which is exactly the case
 *   the old code got wrong.
 *
 * HOW "the rendered form is B, not A" IS PINNED:
 *   `templateId` on the prepare-complete response IS the identity of the form
 *   the complete modal renders — `CalendarCompleteEventModalComponent`
 *   `loadTemplateInfo()` calls `eFormService.getSingle(prepared.templateId)`
 *   and then `compliancesService.getCase(prepared.sdkCaseId, currenteForm.id)`.
 *   The spec captures the REAL eForm ids off the wire (`payload.eformId` in the
 *   create POST and the edit PUT bodies — `eformId` is a declared field on
 *   `CalendarTaskCreateModel` / `CalendarTaskUpdateModel` since this fix) and
 *   asserts:
 *     * prepare-complete #1 (before the swap) → templateId === eformIdA
 *     * prepare-complete #2 (after  the swap) → templateId === eformIdB
 *     * plus the modal really fetched B: GET /api/templates/get/{eformIdB}
 *       and GET .../compliances/cases?...&templateId={eformIdB}
 *   Against the OLD behaviour prepare-complete #2 still returns eformIdA (the
 *   deployed case's frozen `CheckListId`) and the template/case GETs are issued
 *   for A — every one of those four assertions fails.
 *   The two additional assertions come straight from design §2:
 *     * `complianceId` is UNCHANGED (row identity is stable by contract)
 *     * `sdkCaseId` HAS changed (the old case was retracted, a new one deployed)
 *
 * eFORM SEEDING: no template seeding of our own. The
 * BackendConfiguration plugin seeds ~20 genuinely different eForms on
 * activation (`BackendConfigurationSeedEforms.GetForms()` — "01. Standard",
 * "01. Ny opgave", "04. Aktivitet beholder", "06. Numerisk", …), and the
 * calendar's `#calendarEventEform` dropdown lists all of them
 * (`calendar-container.component.ts loadEforms()`). The suite reads the
 * dropdown option labels once and picks the first two that are distinct AND
 * where neither is a substring of the other, so "A vs B" is unambiguous in
 * both the UI text and the ids. Picking BY LABEL (never by `.nth()`) matches
 * the project convention — option order shifts whenever the seed list changes.
 *
 * Lives in `t/` — the Playwright shard matrix is one job per subdirectory, and
 * `t/` is one of the lightest calendar shards (2 suites). Its sibling
 * `calendar-edit-fields.spec.ts` is the edit-modal field round-trip suite, so
 * an "edit-modal field change must propagate" regression sits with its closest
 * relative instead of piling a fourth heavy suite onto `p/`.
 *
 * Matrix coverage (EFC01–EFC03):
 *   EFC01 — one-off event, deployed with A, edited to B: calendar/preview show
 *           B and the COMPLETE flow opens B.                          [core]
 *   EFC02 — recurring event, eForm change under scope "this": the new confirm
 *           dialog appears; Cancel aborts the save; Confirm applies it. [here]
 *   EFC03 — recurring event, eForm change under scope "all": no confirm dialog
 *           (nothing to warn about — "all" already is the series).     [here]
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

// The two eForm template LABELS the suite swaps between. Discovered once from
// the live `#calendarEventEform` dropdown (see ensureEformLabelsPicked) rather
// than hard-coded, so a change to the seeded template set cannot silently
// break the suite.
let eformA = '';
let eformB = '';

// ---------------------------------------------------------------------------
// Network predicates — same matchers the sibling calendar suites use.
// ---------------------------------------------------------------------------

/** Create: POST .../calendar/tasks, excluding the week/move/resize siblings. */
function isCreatePost(method: string, url: string): boolean {
  return (
    url.includes('/api/backend-configuration-pn/calendar/tasks') &&
    !url.includes('/tasks/week') &&
    !url.includes('/tasks/move') &&
    !url.includes('/tasks/resize') &&
    method === 'POST'
  );
}

/** Edit: PUT .../calendar/tasks (exact suffix — mirrors confirmEditScope). */
function isEditPut(method: string, url: string): boolean {
  return (
    url.endsWith('/api/backend-configuration-pn/calendar/tasks') && method === 'PUT'
  );
}

/** The week reload that re-renders the grid after a create/edit. */
function isWeekReload(r: Response): boolean {
  return (
    r.url().includes('/api/backend-configuration-pn/calendar/tasks/week') &&
    r.request().method() === 'POST'
  );
}

/** The completion call that opens the combined complete modal. */
function isPrepareComplete(r: Response): boolean {
  return (
    /\/api\/backend-configuration-pn\/calendar\/tasks\/\d+\/prepare-complete/.test(r.url()) &&
    r.request().method() === 'POST'
  );
}

// ---------------------------------------------------------------------------
// Small utilities.
// ---------------------------------------------------------------------------

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** Anchored matcher so `hasText` cannot match a longer sibling label. */
function exactText(value: string): RegExp {
  return new RegExp(`^\\s*${escapeRegExp(value)}\\s*$`);
}

/**
 * The eForm id the modal actually put on the wire. `eformId` is a declared
 * field on CalendarTaskCreateModel / CalendarTaskUpdateModel (§4 of the design)
 * and is what the backend stores as AreaRule.EformId.
 */
function eformIdFromRequest(req: Request): number {
  const body = req.postData();
  expect(body, `${req.method()} ${req.url()} should carry a JSON body`).toBeTruthy();
  const parsed = JSON.parse(body as string);
  const id = parsed?.eformId;
  expect(
    typeof id,
    `payload.eformId should be a number; got ${JSON.stringify(id)}`
  ).toBe('number');
  return id as number;
}

/** Shape of `PrepareComplete`'s `model` (CalendarPrepareCompleteResult). */
interface PrepareCompleteModel {
  sdkCaseId: number;
  templateId: number | null;
  propertyId: number;
  complianceId: number;
  assignedSiteId: number | null;
  deadline: string;
  eventStart: string;
}

// ---------------------------------------------------------------------------
// eForm dropdown helpers. The create/edit modal's `#calendarEventEform` is a
// searchable mtx-select with appendTo="body", so its panel lives OUTSIDE the
// dialog element.
// ---------------------------------------------------------------------------

/**
 * Read every option label from the (already-open) create/edit modal's eForm
 * dropdown, then close the panel again.
 *
 * Closes by clicking the title input — NOT Escape: the panel is a plain
 * ng-dropdown-panel appended to body, so an Escape keypress propagates to the
 * surrounding mat-dialog and would close the whole event modal.
 */
async function readEformOptionLabels(page: Page): Promise<string[]> {
  await page.locator('#calendarEventEform').click();
  const panel = page.locator('.ng-dropdown-panel');
  await panel.waitFor({ state: 'visible', timeout: 10000 });
  await panel.locator('.ng-option').first().waitFor({ state: 'visible', timeout: 10000 });
  const raw = await panel.locator('.ng-option').allInnerTexts();
  await page.locator('#calendarEventTitle').click();
  await page.waitForTimeout(300);
  return raw.map(t => t.trim()).filter(t => t.length > 0);
}

/**
 * Populate `eformA` / `eformB` from the live dropdown the first time an event
 * modal is open. Requires two labels where neither contains the other, so the
 * "shows B, not A" text assertions can never be satisfied by a prefix match
 * (the seeded set has plenty: "01. Standard", "01. Ny opgave", "06. Numerisk", …).
 */
async function ensureEformLabelsPicked(page: Page): Promise<void> {
  if (eformA && eformB) return;

  const labels = await readEformOptionLabels(page);
  expect(
    labels.length,
    'the calendar eForm dropdown must list at least 2 templates for an A -> B swap'
  ).toBeGreaterThanOrEqual(2);

  for (let i = 0; i < labels.length && !eformB; i++) {
    for (let j = i + 1; j < labels.length; j++) {
      const a = labels[i];
      const b = labels[j];
      if (a !== b && !a.includes(b) && !b.includes(a)) {
        eformA = a;
        eformB = b;
        break;
      }
    }
  }

  expect(
    eformB,
    `no two eForm labels are mutually non-overlapping; got ${JSON.stringify(labels)}`
  ).not.toBe('');
}

/**
 * Select an eForm in the currently-open event modal BY LABEL. Never by
 * `.nth()` — the option order follows the seeded template list and shifts
 * whenever it changes.
 *
 * ng-select keeps its typeahead input in the CONTROL while nothing is selected
 * and moves it into the PANEL once a value is picked (the edit modal always
 * arrives with a value), so probe both — same branch as `typeInNgSelect` in
 * `l/calendar.page.ts`.
 */
async function selectEformByLabel(page: Page, label: string): Promise<void> {
  const select = page.locator('#calendarEventEform');
  await select.click();
  const panel = page.locator('.ng-dropdown-panel');
  await panel.waitFor({ state: 'visible', timeout: 10000 });

  const panelInput = panel.locator('input[type="text"]');
  const controlInput = select.locator('input[type="text"]');
  if ((await panelInput.count()) > 0) {
    await panelInput.first().fill(label);
  } else if ((await controlInput.count()) > 0) {
    await controlInput.first().fill(label);
  }
  await page.waitForTimeout(400);

  await panel.locator('.ng-option').filter({ hasText: exactText(label) }).first().click();
  await page.waitForTimeout(300);

  // `.ng-value-label` (not `.ng-value`): the latter's innerText includes the
  // clear-icon glyph.
  await expect(select.locator('.ng-value-label')).toHaveText(label);
}

/** Read the eForm currently selected in the open event modal. */
function selectedEformLabel(page: Page) {
  return page.locator('#calendarEventEform .ng-value-label');
}

// ---------------------------------------------------------------------------
// Event create / edit plumbing.
// ---------------------------------------------------------------------------

/**
 * Fill + save the create modal that is ALREADY open at the desired slot, with
 * an explicitly chosen eForm. Mirrors `CalendarUiEnhancementsPage.
 * fillAndSaveEvent` (first planning tag + first assignee, both backend-required)
 * except that the eForm is picked by label instead of "first option".
 *
 * Returns the eForm id the create POST carried, so later assertions can compare
 * `PrepareComplete.templateId` against a real id rather than a label.
 */
async function createEventWithEform(
  page: Page,
  calendarPage: CalendarUiEnhancementsPage,
  title: string,
  eformLabel: string,
  options: { repeat?: 'weeklyOne' } = {},
): Promise<number> {
  await page.locator('#calendarEventTitle').fill(title);
  await selectEformByLabel(page, eformLabel);

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

  if (options.repeat) {
    await calendarPage.selectRepeatPreset(options.repeat);
  }

  const reqWait = page.waitForRequest(r => isCreatePost(r.method(), r.url()), { timeout: 30000 });
  const respWait = page.waitForResponse(
    r => isCreatePost(r.request().method(), r.url()),
    { timeout: 30000 }
  );
  await page.locator('#calendarEventSaveBtn').click();
  const req = await reqWait;
  const resp = await respWait;
  expect(resp.status(), 'create POST should return 200').toBe(200);

  await page.waitForTimeout(1500);
  await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 15000 });

  return eformIdFromRequest(req);
}

/**
 * Save an open edit modal for a NON-recurring event: no RepeatScopeModal and —
 * by design §3 — no eForm confirmation either (a one-off edit only ever touches
 * its own occurrence, and the backend forces its scope to "all" anyway). So the
 * PUT fires straight through.
 *
 * The PUT is long: `UpdateTask` runs the retract+redeploy repair pass
 * SYNCHRONOUSLY before responding, so this waits generously.
 *
 * Returns the eForm id the PUT carried.
 */
async function saveNonRecurringEdit(page: Page): Promise<number> {
  const reqWait = page.waitForRequest(r => isEditPut(r.method(), r.url()), { timeout: 60000 });
  const respWait = page.waitForResponse(
    r => isEditPut(r.request().method(), r.url()),
    { timeout: 60000 }
  );
  const reloadWait = page.waitForResponse(isWeekReload, { timeout: 60000 });

  await page.locator('#calendarEventSaveBtn').click();
  const req = await reqWait;
  const resp = await respWait;
  expect(resp.status(), 'edit PUT should return 200').toBe(200);
  await reloadWait;
  await page.waitForTimeout(1000);

  return eformIdFromRequest(req);
}

// ---------------------------------------------------------------------------
// Complete-flow plumbing (shared with p/calendar-complete.spec.ts's contract).
// ---------------------------------------------------------------------------

/**
 * Click the event's completion indicator, wait for the prepare-complete POST
 * and the combined complete modal, and return the POST's `model`.
 *
 * When `expectTemplateId` is given, ALSO waits for the two GETs the modal
 * issues to render that form — `eFormService.getSingle(templateId)` and
 * `compliancesService.getCase(sdkCaseId, templateId)` — so the assertion covers
 * what was actually rendered, not only what the API returned. Both waiters are
 * registered BEFORE the click so they cannot miss the round trip.
 */
async function openCompleteModalAndRead(
  page: Page,
  calendarPage: CalendarUiEnhancementsPage,
  title: string,
  expectTemplateId?: number,
): Promise<PrepareCompleteModel> {
  const block = calendarPage.findEventBlock(title);
  await expect(block).toBeVisible({ timeout: 15000 });

  const prepareWait = page.waitForResponse(isPrepareComplete, { timeout: 60000 });
  const templateGetWait = expectTemplateId != null
    ? page.waitForResponse(
        r => new RegExp(`/api/templates/get/${expectTemplateId}(\\?|$)`).test(r.url())
          && r.request().method() === 'GET',
        { timeout: 60000 }
      )
    : null;
  const caseGetWait = expectTemplateId != null
    ? page.waitForResponse(
        r => r.url().includes('/api/backend-configuration-pn/compliances/cases')
          // anchored so templateId=12 cannot match templateId=123
          && new RegExp(`[?&]templateId=${expectTemplateId}(&|$)`).test(r.url())
          && r.request().method() === 'GET',
        { timeout: 60000 }
      )
    : null;

  await block.locator('.completion-btn').click();

  const resp = await prepareWait;
  expect(resp.status(), 'prepare-complete should return 200').toBe(200);
  const body = await resp.json();
  expect(
    body?.success,
    `prepare-complete should succeed; message: ${JSON.stringify(body?.message ?? '')}`
  ).toBe(true);

  await page
    .locator('app-calendar-complete-event-modal')
    .first()
    .waitFor({ state: 'visible', timeout: 20000 });

  if (templateGetWait) await templateGetWait;
  if (caseGetWait) await caseGetWait;

  return body.model as PrepareCompleteModel;
}

/**
 * Cancel the combined complete modal without saving. Cancelling deliberately
 * LEAVES the materialised Compliance row + SDK case open — that is the fixture
 * the eForm swap then has to repair.
 */
async function closeCompleteModal(page: Page): Promise<void> {
  const modal = page.locator('app-calendar-complete-event-modal').first();
  if ((await modal.count()) === 0) return;
  const cancelBtn = page.locator('#completeCancelBtn');
  if ((await cancelBtn.count()) > 0) {
    await cancelBtn.click();
  } else {
    await page.keyboard.press('Escape');
  }
  await modal.waitFor({ state: 'detached', timeout: 10000 }).catch(() => undefined);
  await page.waitForTimeout(500);
}

test.describe.serial('Calendar eForm-change propagation', () => {
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
  // Seed — property + worker. Runs first via describe.serial.
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
  // EFC01 — THE REGRESSION.
  //
  //   one-off event created with eForm A
  //     -> forced deployment with A (completion click + cancel)
  //     -> edited to eForm B
  //     -> preview shows B
  //     -> the COMPLETE flow opens B, not A.
  //
  //   Every assertion after the swap fails against the pre-fix behaviour,
  //   where the deployed SDK case kept `CheckListId = A`.
  // =======================================================================
  test('EFC01: editing a deployed event\'s eForm re-points the COMPLETE flow at the new eForm', async ({ page }) => {
    test.setTimeout(600000);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    const previewPage = new CalendarPage(page);
    const title = `EFC01-${generateRandmString(5)}`;

    // --- create with eForm A (Monday 08:00, next week, one-off) ----------
    await calendarPage.openCreateModalAtSlot(0, 8);
    await ensureEformLabelsPicked(page);
    const eformIdA = await createEventWithEform(page, calendarPage, title, eformA);

    // --- FIXTURE: force the occurrence to be DEPLOYED with A -------------
    // Without this the occurrence would only materialise at completion time,
    // and would then pick up the new eForm even WITHOUT the fix — the test
    // would pin nothing. See the file header.
    const before = await openCompleteModalAndRead(page, calendarPage, title, eformIdA);
    expect(
      before.templateId,
      'the freshly deployed occurrence must complete with the eForm it was created with'
    ).toBe(eformIdA);
    expect(before.sdkCaseId, 'a deployed occurrence has an SDK case').toBeGreaterThan(0);
    expect(before.complianceId, 'a deployed occurrence has a Compliance row').toBeGreaterThan(0);
    await closeCompleteModal(page);

    // --- edit A -> B ------------------------------------------------------
    await calendarPage.openEditModal(title);
    await expect(
      selectedEformLabel(page),
      'the edit modal should rehydrate with the eForm the event was created with'
    ).toHaveText(eformA);

    await selectEformByLabel(page, eformB);
    // The in-dialog eForm preview resolves the new template server-side, so it
    // is the first place the swap becomes observable.
    await expect(page.locator('.eform-preview-label').first()).toHaveText(eformB, { timeout: 15000 });

    const eformIdB = await saveNonRecurringEdit(page);
    expect(eformIdB, 'the edit PUT must carry a DIFFERENT eForm id').not.toBe(eformIdA);
    // A one-off edit has no series to warn about — the confirmation is for
    // recurring events only (EFC02).
    await expect(page.locator('app-eform-change-scope-modal')).toHaveCount(0);

    // --- the calendar / preview show B ------------------------------------
    await calendarPage.openEventPreview(title);
    const checklistRow = previewPage.getPreviewRowByIcon('checklist');
    await expect(checklistRow, 'the preview card must name the NEW eForm').toContainText(
      eformB, { timeout: 15000 }
    );
    await expect(checklistRow, 'the preview card must not still name the OLD eForm')
      .not.toContainText(eformA);
    await previewPage.closePreviewPopover();

    // --- THE REGRESSION ASSERTION: the COMPLETE flow opens B --------------
    // openCompleteModalAndRead(…, eformIdB) additionally awaits
    //   GET /api/templates/get/{eformIdB}
    //   GET .../compliances/cases?id={sdkCaseId}&templateId={eformIdB}
    // i.e. the modal really loaded and rendered B's definition. Pre-fix both
    // GETs are issued for eformIdA and these waits time out.
    const after = await openCompleteModalAndRead(page, calendarPage, title, eformIdB);

    expect(
      after.templateId,
      'REGRESSION: completing the event must open the NEW eForm — pre-fix the deployed ' +
      'SDK case kept its creation-time CheckListId, so the complete modal rendered the OLD one'
    ).toBe(eformIdB);
    expect(after.templateId, 'the complete flow must not still open the old eForm').not.toBe(eformIdA);

    // Design §2: the case is retracted and redeployed…
    expect(
      after.sdkCaseId,
      'the open occurrence must be redeployed onto a NEW SDK case'
    ).not.toBe(before.sdkCaseId);
    // …but the Compliance row is updated IN PLACE (the calendar UI holds
    // complianceId and the compliance view depends on row stability).
    expect(
      after.complianceId,
      'the Compliance row must be updated in place, never deleted and recreated'
    ).toBe(before.complianceId);

    await expect(page.locator('app-calendar-complete-event-modal').first()).toBeVisible();
    await expect(page.locator('#completeSaveBtn')).toHaveCount(1);

    await closeCompleteModal(page);
  });

  // =======================================================================
  // EFC02 — the eForm-change confirmation on a RECURRING series.
  //
  //   The eForm stays a SERIES-level property (design §3): the backend
  //   re-points every uncompleted occurrence regardless of the edit scope. So
  //   when the eForm changed and the picked scope is narrower than "all",
  //   `TaskCreateEditModalComponent.saveWithScope` opens
  //   `app-eform-change-scope-modal` first.
  //     * Cancel  -> the save is ABORTED (no PUT at all) and the edit modal
  //                  stays open so the user can reset the eForm.
  //     * Confirm -> the save proceeds and the SERIES carries the new eForm.
  // =======================================================================
  test('EFC02: changing a recurring event\'s eForm under scope "this" confirms first; Cancel aborts, Confirm applies', async ({ page }) => {
    test.setTimeout(600000);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `EFC02-${generateRandmString(5)}`;

    // Tuesday 08:00 next week, weekly series.
    await calendarPage.openCreateModalAtSlot(1, 8);
    await ensureEformLabelsPicked(page);
    await createEventWithEform(page, calendarPage, title, eformA, { repeat: 'weeklyOne' });

    await calendarPage.openEditModal(title);
    await expect(selectedEformLabel(page)).toHaveText(eformA);
    await selectEformByLabel(page, eformB);

    // ---- Cancel path: the confirmation must abort the save --------------
    let putCount = 0;
    const countPut = (req: Request) => {
      if (isEditPut(req.method(), req.url())) putCount++;
    };
    page.on('request', countPut);
    try {
      await calendarPage.clickSaveInEditModal();
      await calendarPage.pickScopeInModal('this');

      const confirmModal = page.locator('app-eform-change-scope-modal');
      await expect(
        confirmModal,
        'a narrower-than-"all" scope with a changed eForm must confirm the series-wide blast radius'
      ).toBeVisible({ timeout: 15000 });
      await expect(page.locator('#eformChangeScopeMessage')).toBeVisible();
      await expect(page.locator('#eformChangeScopeMessage')).not.toHaveText('');
      await expect(page.locator('#eformChangeScopeConfirmBtn')).toBeVisible();

      await page.locator('#eformChangeScopeCancelBtn').click();
      await confirmModal.waitFor({ state: 'detached', timeout: 10000 });
      await page.waitForTimeout(2000);

      expect(
        putCount,
        'cancelling the eForm-change confirmation must abort the save entirely'
      ).toBe(0);
      // The edit dialog stays open (with the new eForm still picked) so the
      // user can put the old one back.
      await expect(page.locator('#calendarEventTitle')).toBeVisible();
      await expect(selectedEformLabel(page)).toHaveText(eformB);
    } finally {
      page.off('request', countPut);
    }

    // ---- Confirm path: the same dialog, confirmed, applies the change ----
    await calendarPage.clickSaveInEditModal();
    await calendarPage.pickScopeInModal('this');
    await expect(page.locator('app-eform-change-scope-modal')).toBeVisible({ timeout: 15000 });

    const putWait = page.waitForResponse(
      r => isEditPut(r.request().method(), r.url()),
      { timeout: 60000 }
    );
    const reloadWait = page.waitForResponse(isWeekReload, { timeout: 60000 });
    await page.locator('#eformChangeScopeConfirmBtn').click();
    const putResp = await putWait;
    expect(putResp.status(), 'confirming must send the edit PUT').toBe(200);
    await reloadWait;
    await page.waitForTimeout(1000);

    // The SERIES now carries the new eForm.
    await calendarPage.openEditModal(title);
    await expect(
      selectedEformLabel(page),
      'confirming applies the eForm to the whole series'
    ).toHaveText(eformB);
    await calendarPage.closeEventModal();
  });

  // =======================================================================
  // EFC03 — scope "all" needs no confirmation.
  //
  //   `saveWithScope` short-circuits on `scope === 'all'`: the user already
  //   asked for a series-wide edit, so there is no wider blast radius to warn
  //   about and the PUT goes straight out.
  // =======================================================================
  test('EFC03: changing a recurring event\'s eForm under scope "all" saves without the confirmation dialog', async ({ page }) => {
    test.setTimeout(600000);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `EFC03-${generateRandmString(5)}`;

    // Wednesday 08:00 next week, weekly series.
    await calendarPage.openCreateModalAtSlot(2, 8);
    await ensureEformLabelsPicked(page);
    await createEventWithEform(page, calendarPage, title, eformA, { repeat: 'weeklyOne' });

    await calendarPage.openEditModal(title);
    await expect(selectedEformLabel(page)).toHaveText(eformA);
    await selectEformByLabel(page, eformB);

    const putWait = page.waitForResponse(
      r => isEditPut(r.request().method(), r.url()),
      { timeout: 60000 }
    );
    const reloadWait = page.waitForResponse(isWeekReload, { timeout: 60000 });

    await calendarPage.clickSaveInEditModal();
    // pickScopeInModal settles ~800 ms after Confirm, so a confirmation dialog
    // would already be mounted by the time this count runs.
    await calendarPage.pickScopeInModal('all');
    await expect(
      page.locator('app-eform-change-scope-modal'),
      'scope "all" is already series-wide — nothing to confirm'
    ).toHaveCount(0);

    const putResp = await putWait;
    expect(putResp.status(), 'the edit PUT should fire straight through').toBe(200);
    await reloadWait;
    await page.waitForTimeout(1000);

    await calendarPage.openEditModal(title);
    await expect(selectedEformLabel(page)).toHaveText(eformB);
    await calendarPage.closeEventModal();
  });
});
