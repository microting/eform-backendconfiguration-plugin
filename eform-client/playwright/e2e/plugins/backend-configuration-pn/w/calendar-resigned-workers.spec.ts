import { test, expect, Page } from '@playwright/test';
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
import { API_TIMEOUT, UI_TIMEOUT, waitForApiResponse } from '../wait-helpers';

/**
 * Resigned workers must not be offered in the calendar assignee pickers
 * (GitHub issue #1184, part of #1183).
 *
 * `GET properties/get-linked-sites` feeds both the create/edit modal's
 * assignee select (`#calendarEventAssignee`) and the complete-event modal's
 * worker select (`#completeWorkerSelect`). It used to build its list from
 * PropertyWorkers → Sites without ever consulting the SDK `Worker.Resigned`
 * flag, so a worker resigned on the Property Workers page kept showing up.
 *
 * Seed: one property, two workers. `resignee` is resigned and must vanish from
 * both pickers; `keeper` stays active, proving the filter does not over-reach
 * and giving the event an assignable worker (the complete modal needs an
 * event, and the backend refuses to resign a worker assigned to an active
 * event — so `resignee` is never assigned to anything).
 *
 * R1 — resign `resignee` via `#resignedToggle` (reusable helper on the
 *      property-workers page object); the row leaves the default table.
 * R2 — create modal: `#calendarEventAssignee` lists `keeper`, not `resignee`;
 *      then create an event and open the complete modal: `#completeWorkerSelect`
 *      lists `keeper`, not `resignee`.
 * R3 — un-resign `resignee`; the name reappears in `#calendarEventAssignee`
 *      without a backend restart (also what lets afterAll delete the worker —
 *      the row-delete action is hidden for resigned rows).
 */

const property: PropertyCreateUpdate = {
  name: `Res ${generateRandmString(5)}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// GUID slices: hex + hyphens only, so the names are safe inside a RegExp.
const resignee: PropertyWorker = {
  name: `Res${generateRandmString(5)}`,
  surname: 'Gone',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};
const keeper: PropertyWorker = {
  name: `Keep${generateRandmString(5)}`,
  surname: 'Stays',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};
const resigneeFullName = `${resignee.name} ${resignee.surname}`;
const keeperFullName = `${keeper.name} ${keeper.surname}`;

// Playwright regex hasText matches the RAW text, and Material wraps option
// labels in whitespace — anchor with \s* on both sides.
function exactOption(page: Page, name: string) {
  return page.locator('.ng-dropdown-panel .ng-option').filter({ hasText: new RegExp(`^\\s*${name}\\s*$`) });
}

function isGetLinkedSites(r: import('@playwright/test').Response): boolean {
  return r.url().includes('/api/backend-configuration-pn/properties/get-linked-sites') && r.request().method() === 'GET';
}

function isPrepareComplete(r: import('@playwright/test').Response): boolean {
  return (
    /\/api\/backend-configuration-pn\/calendar\/tasks\/\d+\/prepare-complete/.test(r.url()) &&
    r.request().method() === 'POST'
  );
}

/**
 * Navigate to the calendar, select the seeded property and open the create
 * modal on next week's slot. Resolves once the modal's get-linked-sites call
 * (the assignee list) has returned.
 */
async function openCreateModal(page: Page, dayOffset: number, hour: number): Promise<CalendarUiEnhancementsPage> {
  const calendarPage = new CalendarUiEnhancementsPage(page);
  await calendarPage.goToCalendar();
  const folderResp = page.waitForResponse(
    r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
    { timeout: API_TIMEOUT }
  );
  await calendarPage.selectProperty(property.name);
  await folderResp.catch(() => undefined);
  await page.waitForTimeout(1000);

  const linkedSites = waitForApiResponse(page, 'GET properties/get-linked-sites (create modal assignees)', isGetLinkedSites, API_TIMEOUT);
  await calendarPage.openCreateModalAtSlot(dayOffset, hour);
  await linkedSites;
  return calendarPage;
}

/** Open the assignee dropdown, run `assertions`, then close it by focusing the title input. */
async function withAssigneeDropdownOpen(page: Page, assertions: () => Promise<void>): Promise<void> {
  await page.locator('#calendarEventAssignee').click();
  await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await assertions();
  await page.locator('#calendarEventTitle').click();
  await page.locator('.ng-dropdown-panel').waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
}

test.describe.serial('Calendar pickers exclude resigned workers (#1184)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);
  });

  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage();
    const cleanup = async () => {
      await page.goto('http://localhost:4200');
      await new LoginPage(page).login();

      const workersPage = new BackendConfigurationPropertyWorkersPage(page);
      await workersPage.goToPropertyWorkers();
      await page.waitForTimeout(1000);
      // R3 un-resigns the worker; if it did not get that far the row is only
      // reachable (and only deletable after un-resigning) with the filter on.
      await workersPage.setShowResignedFilter(true);
      if ((await workersPage.workerRow(resigneeFullName).count()) > 0) {
        await workersPage.setResigned(resigneeFullName, false).catch(() => undefined);
      }
      // The filter is resigned-ONLY: with it on, the (now all active) workers
      // are not listed and clearTable() would delete nothing. Flip it off
      // (awaits the index refresh) so both workers are in the table.
      await workersPage.setShowResignedFilter(false);
      await workersPage.clearTable();

      const propertiesPage = new BackendConfigurationPropertiesPage(page);
      await propertiesPage.goToProperties();
      await page.waitForTimeout(1000);
      await propertiesPage.clearTable();
    };
    try {
      await Promise.race([cleanup(), new Promise(resolve => setTimeout(resolve, 90000))]);
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    }
    try { await page.close(); } catch {}
  });

  test('seed: property + two workers', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(resignee);
    await expect(workersPage.workerRow(resigneeFullName)).toHaveCount(1);
    await workersPage.create(keeper);
    await expect(workersPage.workerRow(keeperFullName)).toHaveCount(1);
  });

  test('R1: resigning a worker on the Property Workers page removes it from the default list', async ({ page }) => {
    test.setTimeout(300000);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.workerRow(resigneeFullName).waitFor({ state: 'visible', timeout: API_TIMEOUT });

    await workersPage.setResigned(resigneeFullName, true);

    // The default filter (showResigned=false) drops the row; the other worker stays.
    await expect(workersPage.workerRow(resigneeFullName)).toHaveCount(0, { timeout: API_TIMEOUT });
    await expect(workersPage.workerRow(keeperFullName)).toHaveCount(1);
  });

  test('R2: create-modal assignee and complete-modal worker pickers omit the resigned worker', async ({ page }) => {
    test.setTimeout(300000);

    // Monday 08:00 next week.
    const calendarPage = await openCreateModal(page, 0, 8);

    await withAssigneeDropdownOpen(page, async () => {
      await expect(exactOption(page, keeperFullName)).toHaveCount(1);
      await expect(exactOption(page, resigneeFullName)).toHaveCount(0);
    });

    // Create an event (fillAndSaveEvent picks the first assignee — the only
    // one now offered is `keeper`) and open its complete modal.
    const title = `R2-${generateRandmString(5)}`;
    await calendarPage.fillAndSaveEvent(title);
    const block = calendarPage.findEventBlock(title);
    await expect(block).toBeVisible();

    const completeLinkedSites = waitForApiResponse(page, 'GET properties/get-linked-sites (complete modal workers)', isGetLinkedSites, API_TIMEOUT);
    const prepareComplete = waitForApiResponse(page, 'POST calendar/tasks/{id}/prepare-complete', isPrepareComplete, API_TIMEOUT);
    await block.locator('.completion-btn').click();
    await prepareComplete;
    await completeLinkedSites;

    const modal = page.locator('app-calendar-complete-event-modal').first();
    await modal.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const workerSelect = page.locator('#completeWorkerSelect');
    await workerSelect.waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    // Single active property worker → preselected; the value must be `keeper`.
    await expect(workerSelect.locator('.ng-value-label').first()).toHaveText(new RegExp(`^\\s*${keeperFullName}\\s*$`), {
      timeout: UI_TIMEOUT,
    });
    await workerSelect.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await expect(exactOption(page, keeperFullName)).toHaveCount(1);
    await expect(exactOption(page, resigneeFullName)).toHaveCount(0);

    // Close the dropdown by clicking the dialog title (Escape would close the
    // MatDialog itself), then cancel — never save: the embedded eForm carries
    // mandatory fields.
    await modal.locator('h2[mat-dialog-title]').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    await page.locator('#completeCancelBtn').click();
    await modal.waitFor({ state: 'detached', timeout: UI_TIMEOUT });
  });

  test('R3: un-resigning brings the worker back into the assignee picker without a restart', async ({ page }) => {
    test.setTimeout(300000);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.setShowResignedFilter(true);
    await workersPage.workerRow(resigneeFullName).waitFor({ state: 'visible', timeout: API_TIMEOUT });
    await workersPage.setResigned(resigneeFullName, false);

    // The "Show resigned" filter lists resigned workers ONLY (index-device-user
    // filters Resigned == ShowResigned), so the un-resigned row leaves the
    // filtered table and reappears once the filter is off.
    await expect(workersPage.workerRow(resigneeFullName)).toHaveCount(0, { timeout: API_TIMEOUT });
    await workersPage.setShowResignedFilter(false);
    await expect(workersPage.workerRow(resigneeFullName)).toHaveCount(1, { timeout: API_TIMEOUT });

    // Tuesday 08:00 next week — a different slot from R2's event.
    const calendarPage = await openCreateModal(page, 1, 8);
    await withAssigneeDropdownOpen(page, async () => {
      await expect(exactOption(page, resigneeFullName)).toHaveCount(1);
      await expect(exactOption(page, keeperFullName)).toHaveCount(1);
    });
    await calendarPage.closeEventModal();
  });
});
