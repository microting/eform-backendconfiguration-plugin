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
 * E2E coverage for GitHub issue #897 — "calendar UI gaps".
 *
 * Two distinct UI gaps were filed:
 *
 *   U09 — Empty calendar grid space used `cursor: pointer`, falsely
 *         implying every pixel was clickable. The fix (commit 8442a4b3,
 *         "default cursor on empty calendar areas (#855)") removed the
 *         blanket `cursor: pointer` from `.day-cell-content` (week grid)
 *         and `.day-column` (day column), so empty space now shows the
 *         normal arrow. Event tiles, resize handles and past slots keep
 *         their explicit cursors — the fix must NOT over-reach onto them.
 *
 *   U10 — "Active / Overdue" (Aktiv / Vis overskredet) toggles. After
 *         investigating the calendar module there is NO calendar-VIEW
 *         filter control for active/overdue. The "Active / Overdue"
 *         toggles are the CREATE/EDIT MODAL status + compliance toggles:
 *           #calendarEventStatusActive / #calendarEventStatusInactive
 *           #calendarEventComplianceOn / #calendarEventComplianceOff
 *         (task-create-edit-modal.component.html, lines 356–392).
 *
 *         Their WIRE behaviour (status serialises as 1/2, complianceEnabled
 *         flips) is already covered by C26/C27/C28 in
 *         calendar-create-fields.spec.ts and E15/E16 in
 *         calendar-edit-fields.spec.ts. C26 additionally asserts the dimmed
 *         CLASS is present.
 *
 *         What U10 adds here is the observable VIEW invariant that is NOT
 *         covered elsewhere: an inactive task must remain RENDERED but
 *         DIMMED (`.gcal-task--inactive`, bound to `task.status === false`
 *         in calendar-task-block.component.html line 5) — i.e. dimmed, not
 *         FILTERED OUT of the web grid. The design intent is "Opgave
 *         nedtonet i tavle" (dimmed on board) — the row stays on the web
 *         calendar; only the gRPC/mobile path hides it (commit 05ef1739).
 *         We prove "not hidden" with a computed-style probe: the block is
 *         visible, carries the dimmed class, and has reduced opacity (< 1).
 *
 * This suite mirrors `calendar-ui-enhancements.spec.ts`: a `describe.serial`
 * with a seed test creating the shared property + worker, reusing the
 * CalendarUiEnhancementsPage page object, and deriving all dates from `now`
 * (no hard-coded drifting dates). Distinct days + unique titles keep the
 * created events from colliding within the navigated week.
 */

// Shared property/worker — created by the first (seed) test, reused by the
// rest of the suite via `test.describe.serial` semantics.
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

test.describe.serial('Calendar UI gaps (#897)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();

    if (seeded) {
      const folderResponsePromise = page.waitForResponse(
        r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
        { timeout: 60000 }
      );
      await calendarPage.selectProperty(property.name);
      await folderResponsePromise.catch(() => undefined);
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
  // Seed test — create property + worker. Runs first thanks to
  // describe.serial. Subsequent beforeEach picks up the seeded property.
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
  // U09 — Empty calendar areas use the default cursor, not pointer.
  // =======================================================================
  test.describe('U09 — empty-area cursor', () => {
    test('U09: empty calendar areas use the default cursor, not pointer', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);

      // Week is the default viewMode; wait for the grid to render.
      await page
        .locator('app-calendar-week-grid')
        .waitFor({ state: 'visible', timeout: 10000 });
      await page.waitForTimeout(500);

      // ----- Leg 1: empty .day-cell-content shows the default arrow -------
      // Hover the EMPTY top of Monday's column (data-day="0"), well clear of
      // any task tile, then read the resolved cursor via getComputedStyle.
      const emptyCell = page.locator('.day-cell-content[data-day="0"]').first();
      await emptyCell.waitFor({ state: 'visible', timeout: 10000 });

      const emptyBox = await emptyCell.boundingBox();
      expect(emptyBox, 'Monday day-cell-content must have a bounding box').not.toBeNull();
      // Hover 5px in from the top so we land on genuinely empty grid space.
      await page.mouse.move(emptyBox!.x + emptyBox!.width / 2, emptyBox!.y + 5);
      await page.waitForTimeout(150);

      const emptyCursor = await emptyCell.evaluate(
        el => getComputedStyle(el as HTMLElement).cursor
      );
      // The fix removed `cursor: pointer`; the element now inherits the
      // document default. Browsers resolve an unset/`auto` cursor on a
      // <div> to `auto` (getComputedStyle returns the computed keyword).
      // Accept either `default` or `auto` — both mean "normal arrow" — and
      // assert the regressed value (`pointer`) is gone.
      expect(
        ['default', 'auto'],
        `empty .day-cell-content cursor should be default/auto, got "${emptyCursor}"`
      ).toContain(emptyCursor);
      expect(emptyCursor, 'empty grid space must NOT be pointer').not.toBe('pointer');

      // ----- Leg 2: regression guard — task tile keeps a non-default cursor.
      // Create an event, then assert the rendered .task-block's computed
      // cursor is NOT `default`/`auto` (the fix removed the blanket grid
      // pointer but tiles keep their explicit cursor — `pointer` per
      // calendar-task-block.component.scss line 7). This proves the fix
      // didn't over-reach onto interactive elements.
      const title = `U09-${generateRandmString(5)}`;
      // Tuesday 09:00 (next week) — distinct day, future slot accepted.
      await calendarPage.openCreateModalAtSlot(1, 9);
      await calendarPage.fillAndSaveEvent(title);

      const block = calendarPage.findEventBlock(title);
      await block.waitFor({ state: 'visible', timeout: 10000 });
      // Hover the tile so any :hover cursor rule would also resolve; we read
      // the base cursor from the element regardless.
      await block.hover();
      await page.waitForTimeout(100);

      const blockCursor = await block.evaluate(
        el => getComputedStyle(el as HTMLElement).cursor
      );
      // The component sets `cursor: pointer` on .task-block. Assert it is a
      // real interactive cursor (not the normal arrow). We deliberately do
      // NOT pin the exact keyword (it could be pointer/grab/move as the
      // drag wiring evolves) — only that it stays distinct from the empty
      // grid's default/auto.
      expect(
        ['default', 'auto'],
        `task tile cursor should NOT be the empty-grid default, got "${blockCursor}"`
      ).not.toContain(blockCursor);
      // Concretely, the current component value is `pointer`.
      expect(blockCursor, 'task tile cursor should be pointer').toBe('pointer');
    });
  });

  // =======================================================================
  // U10 — "Active / Overdue" toggles.
  //
  // FINDING: these are the CREATE-MODAL status/compliance toggles, NOT a
  // calendar-view filter (see header comment). Wire behaviour is covered by
  // C26/C27/C28 + E15/E16. Here we cover only the VIEW invariant: inactive
  // = dimmed, NOT hidden.
  // =======================================================================
  test.describe('U10 — inactive task renders dimmed, not hidden', () => {
    test('U10: an inactive (status off) task renders dimmed on the grid, not filtered out', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `U10-${generateRandmString(5)}`;

      // Wednesday 09:00 (next week) — distinct day from U09's Tuesday so the
      // two created events never collide within the navigated week.
      await calendarPage.openCreateModalAtSlot(2, 9);

      // Fill the required fields the same way fillAndSaveEvent does, but flip
      // the status toggle to INACTIVE before saving. We inline the field
      // fill (rather than call fillAndSaveEvent) so the toggle is set while
      // the modal is still open, and so we can capture the create POST body
      // and assert the inactive wire value as a cross-check.
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

      // Flip status → Inactive ("Task dimmed on board and not shown in app").
      // onPickStatusInactive() sets statusControl=false; buildPayload then
      // serialises status as 2.
      await page.locator('#calendarEventStatusInactive').click();
      await page.waitForTimeout(300);

      const createResp = page.waitForResponse(
        r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
          && !r.url().includes('/tasks/week')
          && !r.url().includes('/tasks/move')
          && !r.url().includes('/tasks/resize')
          && r.request().method() === 'POST',
        { timeout: 30000 }
      );
      await page.locator('#calendarEventSaveBtn').click();
      const resp = await createResp;
      await page.waitForTimeout(1500);

      // Cross-check the wire value (status 2 = inactive) — already covered by
      // C26 but cheap and clarifying here as the precondition for the view
      // assertion below.
      let postedStatus: unknown = undefined;
      try {
        postedStatus = (resp.request().postDataJSON() as any)?.status;
      } catch {
        // postData may be unavailable in some transports; the view assertion
        // below is the real subject of this test, so don't hard-fail here.
      }
      if (postedStatus !== undefined) {
        expect(postedStatus, 'inactive status should serialise as 2').toBe(2);
      }

      // ----- The U10 subject: inactive = dimmed, NOT hidden ----------------
      // 1) The block must still be RENDERED on the web grid (not filtered).
      const block = calendarPage.findEventBlock(title);
      await block.waitFor({ state: 'visible', timeout: 10000 });
      await expect(block, 'inactive task must remain on the web grid (not hidden)').toBeVisible();

      // 2) It carries the dimmed class (.gcal-task--inactive ← status===false).
      await expect(block).toHaveClass(/gcal-task--inactive/);

      // 3) Computed-style probe: the dimming is actually applied — opacity is
      //    reduced below 1 (the SCSS sets opacity: 0.45 on .gcal-task--inactive).
      //    A filtered-out task would be absent; a non-dimmed task would be at
      //    opacity 1. This is the regression guard that "dimmed, not hidden"
      //    holds at the rendered-pixel level, not just the class level.
      const opacity = await block.evaluate(
        el => parseFloat(getComputedStyle(el as HTMLElement).opacity)
      );
      expect(
        opacity,
        `inactive task should be visibly dimmed (opacity < 1), got ${opacity}`
      ).toBeLessThan(1);
    });

    // ----- Overdue filter: intentionally not implementable via the UI ------
    // There is no calendar-view "Overdue" FILTER control (the toggles are
    // the modal compliance toggles, covered by C28/E16). Even if a view
    // filter existed, an OVERDUE task requires `taskDate + startHour` to be
    // before `now`, which CANNOT be created through the modal — the create
    // flow blocks past slots (openCreateModalAtSlot advances a week to land
    // on a FUTURE slot precisely because past slots are rejected). Time-aware
    // overdue detection is server-side and covered there. Parked as fixme
    // with that rationale so the gap is documented, not silently dropped.
    test.fixme(
      'U10b: overdue tasks cannot be created via the UI (past slots blocked); time-aware overdue is server-covered',
      async () => {
        // Intentionally empty — see rationale above. An overdue task needs a
        // past taskDate+startHour, which the create modal disallows, so this
        // leg is unreachable from the UI and is asserted at the server layer.
      }
    );
  });
});
