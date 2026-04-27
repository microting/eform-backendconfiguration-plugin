import { test, expect } from '@playwright/test';
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
 * Drag-resize regression suite — each test creates its own event so the
 * resize result can be asserted independently. Behaviour is exhaustively
 * covered by the C# integration tests (CalendarResizeTests.cs); this
 * suite proves the UI plumbing (gesture → optimistic update → scope
 * modal → backend → reload) works end to end.
 *
 * Lives in `r/` to share the matrix slot with the existing UI-enhancement
 * suite. Reuses the same property/worker seed pattern.
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

// HOUR_HEIGHT = 52 px; resize snaps to 15-min (13 px) increments.
const HOUR_PX = 52;

test.describe.serial('Calendar event resize', () => {
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

  // Helper — open create modal, save a 1-h non-recurring event with the
  // given title at Monday 09:00 of next week. Returns nothing; the title
  // is the lookup key for the resulting block.
  async function createSimpleEvent(page: import('@playwright/test').Page, calendarPage: CalendarUiEnhancementsPage, title: string) {
    await calendarPage.openCreateModalAt9AM();
    await page.locator('#calendarEventTitle').fill(title);
    // Pick first eForm (required by backend validation in the suite)
    const eform = page.locator('#calendarEventEform');
    await eform.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);
    // Pick first planning tag
    const planningTag = page.locator('#calendarEventPlanningTag');
    await planningTag.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.waitForTimeout(300);
    // Pick first assignee
    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page.locator('.ng-dropdown-panel .ng-option').first().click();
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    const createResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
        && r.request().method() === 'POST',
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    await createResp;
    await page.waitForTimeout(1500);
    await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: 10000 });
  }

  // =======================================================================
  // D. Single (non-recurring) event resize — modal does NOT pop, backend
  // commits with scope='this' silently.
  // =======================================================================
  test.describe('Single event resize', () => {
    test('D1: expand from end (drag bottom down) extends duration', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D1-${generateRandmString(5)}`;
      await createSimpleEvent(page, calendarPage, title);

      // Default: 09:00 – 10:00. Drag bottom down 1 hour → 09:00 – 11:00.
      // dragResizeHandle awaits the post-resize /tasks/week POST internally.
      await calendarPage.dragResizeHandle(title, 'bottom', HOUR_PX);

      const timeText = await calendarPage.getEventTimeText(title);
      expect(timeText).toContain('09:00');
      expect(timeText).toContain('11:00');
    });

    test('D2: shrink from end (drag bottom up) shortens duration', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D2-${generateRandmString(5)}`;
      await createSimpleEvent(page, calendarPage, title);

      // Default: 09:00 – 10:00. Drag bottom up 30 min → 09:00 – 09:30.
      await calendarPage.dragResizeHandle(title, 'bottom', -Math.round(HOUR_PX / 2));

      // Block height is now 30 min — .task-time only renders when
      // duration ≥ 0.5 h; 0.5 is the boundary so it should render. If
      // empty, fall back to height-based assertion.
      const timeText = await calendarPage.getEventTimeText(title);
      if (timeText.length > 0) {
        expect(timeText).toContain('09:00');
        expect(timeText).toContain('09:30');
      } else {
        const block = calendarPage.findEventBlock(title);
        const box = await block.boundingBox();
        expect(box).not.toBeNull();
        // 30 min = 26 px (half of HOUR_PX); -4 padding = 22 px height.
        expect(box!.height).toBeLessThan(40);
      }
    });

    test('D3: expand from start (drag top up) earlier-shifts start', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D3-${generateRandmString(5)}`;
      await createSimpleEvent(page, calendarPage, title);

      // Default 09:00 – 10:00. Drag top up 1 hour → 08:00 – 10:00.
      await calendarPage.dragResizeHandle(title, 'top', -HOUR_PX);

      const timeText = await calendarPage.getEventTimeText(title);
      expect(timeText).toContain('08:00');
      expect(timeText).toContain('10:00');
    });

    test('D4: shrink from start (drag top down) later-shifts start', async ({ page }) => {
      const calendarPage = new CalendarUiEnhancementsPage(page);
      const title = `D4-${generateRandmString(5)}`;
      await createSimpleEvent(page, calendarPage, title);

      // Default 09:00 – 10:00. Drag top down 30 min → 09:30 – 10:00.
      await calendarPage.dragResizeHandle(title, 'top', Math.round(HOUR_PX / 2));

      const timeText = await calendarPage.getEventTimeText(title);
      if (timeText.length > 0) {
        expect(timeText).toContain('09:30');
        expect(timeText).toContain('10:00');
      } else {
        const block = calendarPage.findEventBlock(title);
        const box = await block.boundingBox();
        expect(box).not.toBeNull();
        expect(box!.height).toBeLessThan(40);
      }
    });
  });
});
