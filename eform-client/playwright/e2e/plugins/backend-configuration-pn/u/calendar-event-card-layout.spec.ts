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
 * Adaptive event-card layout regression suite. Verifies the compact (heightPx
 * < 40) vs. tall (heightPx >= 40) rendering modes added to
 * CalendarTaskBlockComponent — see the design doc
 * docs/superpowers/specs/2026-06-01-calendar-event-card-adaptive-layout-design.md
 *
 * Each test creates its own event on a distinct day-of-week slot so cards
 * don't collide on the week grid. Shared property/worker seed pattern
 * matches calendar-ui-enhancements.spec.ts.
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

// Dismiss the worker-selection modal that appears when the task's assigned
// worker is not the current user. With a single worker the modal opens
// pre-selected — clicking the primary button confirms immediately.
async function handleWorkerSelectModal(page: import('@playwright/test').Page): Promise<void> {
  const workerModal = page.locator('app-calendar-select-worker-modal');
  const appeared = await workerModal.waitFor({ state: 'visible', timeout: 3000 }).then(() => true).catch(() => false);
  if (!appeared) return;
  await page.locator('app-calendar-select-worker-modal button.btn-primary').click();
  await workerModal.waitFor({ state: 'detached', timeout: 5000 }).catch(() => undefined);
}

test.describe.serial('Calendar event card — adaptive layout', () => {
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
  // L1. 30-min event renders in compact mode with title + time on the
  //     same baseline.
  // =======================================================================
  test('L1: 30-min event renders compact with title+time inline', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `L1-${generateRandmString(5)}`;

    // Open the modal at Monday 08:00. Default duration is 1h; shrink the end
    // to 08:30 so we get a 30-min event (~22px tall at HOUR_HEIGHT=52 → compact).
    await calendarPage.openCreateModalAtSlot(0, 8);
    await calendarPage.fillAndSaveEvent(title, { endTime: '08:30' });

    const block = calendarPage.findEventBlock(title);
    await expect(block).toHaveClass(/(^|\s)compact(\s|$)/);

    const titleEl = block.locator('.task-title');
    const timeEl = block.locator('.task-time');
    await expect(titleEl).toBeVisible();
    await expect(timeEl).toBeVisible();

    const titleBox = await titleEl.boundingBox();
    const timeBox = await timeEl.boundingBox();
    expect(titleBox).not.toBeNull();
    expect(timeBox).not.toBeNull();
    // Compact mode: title + time on the same line, tops within 2 px.
    expect(Math.abs(titleBox!.y - timeBox!.y)).toBeLessThanOrEqual(2);
  });

  // =======================================================================
  // L2. 90-min event renders in tall mode — time row sits below the title.
  // =======================================================================
  test('L2: 90-min event renders tall with time stacked below title', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `L2-${generateRandmString(5)}`;

    // Tuesday 09:00 – 10:30 (90 min ~ 74 px tall → not compact).
    await calendarPage.openCreateModalAtSlot(1, 9);
    await calendarPage.fillAndSaveEvent(title, { endTime: '10:30' });

    const block = calendarPage.findEventBlock(title);
    // Should NOT have the .compact class on the outer task-block.
    const cls = (await block.getAttribute('class')) ?? '';
    expect(cls.split(/\s+/).includes('compact')).toBe(false);

    const titleEl = block.locator('.task-title');
    const timeEl = block.locator('.task-time');
    await expect(titleEl).toBeVisible();
    await expect(timeEl).toBeVisible();

    const titleBox = await titleEl.boundingBox();
    const timeBox = await timeEl.boundingBox();
    expect(titleBox).not.toBeNull();
    expect(timeBox).not.toBeNull();
    // Tall mode: time row is below the title; tops differ by > 8 px.
    expect(timeBox!.y - titleBox!.y).toBeGreaterThan(8);
  });

  // =======================================================================
  // L3. Time never wraps — single line in both compact and tall mode.
  //     Inspects the cards created in L1 (compact) and L2 (tall).
  // =======================================================================
  test('L3: .task-time renders on a single line in both modes', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `L3-${generateRandmString(5)}`;

    // Wednesday: create one compact (30 min) and one tall (90 min) event so
    // this test is independent of L1/L2's state (each test logs in fresh).
    // First call advances the week; the second stays on the same week.
    await calendarPage.openCreateModalAtSlot(2, 8);
    await calendarPage.fillAndSaveEvent(`${title}-compact`, { endTime: '08:30' });

    await calendarPage.openCreateModalOnCurrentWeek(2, 10);
    await calendarPage.fillAndSaveEvent(`${title}-tall`, { endTime: '11:30' });

    // Title is known to be one line (it has nowrap + ellipsis). Time on the
    // same single-line rule should be ≈ one line tall. We compare against
    // the title's box rather than a hard pixel bound because the line-height
    // inheritance differs between local dev (~14 px) and CI (~22 px) — a
    // 22 px tall .task-time is still one line if the title beside it is
    // also 22 px tall (both inherit the same "normal" line-height from the
    // parent). A wrap would push .task-time to ≥ 1.6 × the title height.
    const compactBlock = calendarPage.findEventBlock(`${title}-compact`);
    const tallBlock = calendarPage.findEventBlock(`${title}-tall`);

    const compactTimeBox = await compactBlock.locator('.task-time').boundingBox();
    const compactTitleBox = await compactBlock.locator('.task-title').boundingBox();
    const tallTimeBox = await tallBlock.locator('.task-time').boundingBox();
    const tallTitleBox = await tallBlock.locator('.task-title').boundingBox();

    expect(compactTimeBox).not.toBeNull();
    expect(compactTitleBox).not.toBeNull();
    expect(tallTimeBox).not.toBeNull();
    expect(tallTitleBox).not.toBeNull();

    expect(compactTimeBox!.height).toBeLessThanOrEqual(compactTitleBox!.height * 1.6);
    expect(tallTimeBox!.height).toBeLessThanOrEqual(tallTitleBox!.height * 1.6);
  });

  // =======================================================================
  // L4. Long title in compact mode ellipsises; time stays visible (not
  //     squeezed to 0 width).
  // =======================================================================
  test('L4: long title ellipsises in compact mode, time still visible', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    // 60+ characters — guaranteed to overflow a narrow compact card.
    const title =
      `L4-${generateRandmString(5)}-` +
      'this-title-is-deliberately-very-long-to-force-ellipsis-overflow';

    await calendarPage.openCreateModalAtSlot(3, 8);
    await calendarPage.fillAndSaveEvent(title, { endTime: '08:30' });

    const block = calendarPage.findEventBlock(title);
    await expect(block).toHaveClass(/(^|\s)compact(\s|$)/);

    const titleEl = block.locator('.task-title');
    const titleMetrics = await titleEl.evaluate((el: Element) => ({
      scrollWidth: (el as HTMLElement).scrollWidth,
      clientWidth: (el as HTMLElement).clientWidth,
    }));
    // Title overflowed its allocated box → ellipsis kicked in.
    expect(titleMetrics.scrollWidth).toBeGreaterThan(titleMetrics.clientWidth);

    const timeEl = block.locator('.task-time');
    await expect(timeEl).toBeVisible();
    const timeBox = await timeEl.boundingBox();
    expect(timeBox).not.toBeNull();
    // Time row must keep a non-zero width — `flex: 0 0 auto` guarantees it
    // never gets squeezed away by an oversized title.
    expect(timeBox!.width).toBeGreaterThan(0);
  });

  // =======================================================================
  // L5. 15-min event still renders the time string. Pre-change the
  //     `*ngIf="task.duration >= 0.5"` gate hid it; now isCompact forces it.
  // =======================================================================
  test('L5: 15-min event still renders formatted time text', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `L5-${generateRandmString(5)}`;

    // Thursday 08:00 – 08:15 (15 min ~ 9 px tall → compact).
    await calendarPage.openCreateModalAtSlot(4, 8);
    await calendarPage.fillAndSaveEvent(title, { endTime: '08:15' });

    const block = calendarPage.findEventBlock(title);
    const timeEl = block.locator('.task-time');
    await expect(timeEl).toHaveCount(1);
    const text = ((await timeEl.textContent()) ?? '').trim();
    // Match HH:MM – HH:MM. The en-dash separator comes from the template
    // (calendar-task-block.component.html line 40).
    expect(text).toMatch(/\d{2}:\d{2}\s*[–-]\s*\d{2}:\d{2}/);
    expect(text).toContain('08:00');
    expect(text).toContain('08:15');
  });

  // =======================================================================
  // L6. The 12-px completion circle on a compact card is still a reachable
  //     click target. We assert the click triggers the completion flow —
  //     PUT /tasks/{id}/complete fires and the eForm submission dialog
  //     opens. The actual `completed` class transition requires submitting
  //     that form (the seeded task has an associated eForm template),
  //     which is out of scope for the layout suite — what matters here is
  //     that the shrunken hit target still receives the click.
  // =======================================================================
  test('L6: completion button is clickable on compact card', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const title = `L6-${generateRandmString(5)}`;

    // Saturday 08:00 – 08:30 (30 min → compact). dayOffset 5 = Saturday
    // (matches the convention used in calendar-resize.spec.ts E2).
    await calendarPage.openCreateModalAtSlot(5, 8);
    await calendarPage.fillAndSaveEvent(title, { endTime: '08:30' });

    const block = calendarPage.findEventBlock(title);
    await expect(block).toHaveClass(/(^|\s)compact(\s|$)/);

    const completionWait = page.waitForResponse(
      r => /\/api\/backend-configuration-pn\/calendar\/tasks\/\d+\/complete/.test(r.url())
        && r.request().method() === 'PUT',
      { timeout: 30000 }
    );
    await block.locator('.completion-btn').click();
    await handleWorkerSelectModal(page);
    await completionWait;

    // The eForm submission dialog opens in response to the PUT (the seeded
    // task carries an eForm template). The dialog's mere appearance proves
    // the click landed and the completion workflow kicked off; we don't
    // need to submit the form here.
    await expect(page.locator('mat-dialog-container').first())
      .toBeVisible({ timeout: 10000 });

    // Cancel so the dialog doesn't leak into the next test.
    const cancelBtn = page
      .locator('mat-dialog-container button')
      .filter({ hasText: /Annuller|Cancel/i })
      .first();
    if ((await cancelBtn.count()) > 0) {
      await cancelBtn.click();
      await page
        .locator('mat-dialog-container')
        .waitFor({ state: 'detached', timeout: 5000 })
        .catch(() => undefined);
    }
  });

  // =======================================================================
  // L7. Overlapping events cascade — both cards render, at least one has
  //     a narrower computed width than the day column (cascade applied).
  // =======================================================================
  test('L7: cascade overlap still renders both events', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    const titleA = `L7a-${generateRandmString(5)}`;
    const titleB = `L7b-${generateRandmString(5)}`;

    // Sunday at 11:00–12:00 and 11:30–12:30 overlap by 30 min. dayOffset 6
    // = Sunday (mirrors F1 in calendar-resize.spec.ts). First call advances
    // the week; the second stays on the same week so both events land on
    // the SAME Sunday and overlap.
    await calendarPage.openCreateModalAtSlot(6, 11);
    await calendarPage.fillAndSaveEvent(titleA, { endTime: '12:00' });

    // Open the modal at a fresh slot on the same Sunday — clickEmptyTimeSlot
    // aims 5 px into the H:00 band. We click 13:00 to avoid landing on the
    // existing 11:00–12:00 block, then set start=11:30 and end=12:30 via
    // the time fields so the second event overlaps the first by 30 min.
    await calendarPage.openCreateModalOnCurrentWeek(6, 13);
    await calendarPage.fillAndSaveEvent(titleB, {
      startTime: '11:30',
      endTime: '12:30',
    });

    const blockA = calendarPage.findEventBlock(titleA);
    const blockB = calendarPage.findEventBlock(titleB);
    await expect(blockA).toBeVisible();
    await expect(blockB).toBeVisible();

    // Read the day column width — both events live on Sunday (data-day=6).
    const dayCell = page.locator('.day-cell-content[data-day="6"]');
    const dayBox = await dayCell.boundingBox();
    const boxA = await blockA.boundingBox();
    const boxB = await blockB.boundingBox();
    expect(dayBox).not.toBeNull();
    expect(boxA).not.toBeNull();
    expect(boxB).not.toBeNull();

    // Cascade narrows at least one of the overlapping cards' computed width
    // to below the full day-column width.
    const narrower = Math.min(boxA!.width, boxB!.width);
    expect(narrower).toBeLessThan(dayBox!.width);
  });
});
