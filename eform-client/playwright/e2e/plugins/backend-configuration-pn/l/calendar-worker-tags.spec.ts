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
 * E2E for the calendar create/edit modal's "Assign to worker tags" field
 * (id="calendarEventWorkerTags").
 *
 * The field is an mtx-select (multiple) whose items come from ALL existing
 * core tags (GET /api/tags via getAvailableTags(); the modal exposes them as
 * data.workerTags) and is hidden when there are zero tags
 * (*ngIf="data.workerTags.length > 0"). When the user picks worker tags the
 * backend expands each tag to every worker carrying it, so an event can be
 * assigned via tags ALONE — the create-validation was relaxed from
 * "≥1 assignee" to "≥1 assignee OR ≥1 worker tag".
 *
 * The create POST (.../calendar/tasks) buildPayload() ships:
 *   - sites        : number[]  (explicit assignee site ids — LEFT EMPTY here)
 *   - workerTagIds : number[]  (the picked worker-tag ids — the field under test)
 *
 * Step-1 decision (how the worker tag is created + assigned to a worker):
 * UI-only, via the existing, ID-stable property-workers page objects.
 *   (a) Create the tag with workersPage.createTag(name) — the "Manage Tags"
 *       modal on the property-workers page (#sitesManageTagsBtn → #newTagBtn →
 *       #newTagName → #newTagSaveBtn). This persists to the core /api/tags
 *       store, the same source the calendar worker-tags dropdown reads.
 *   (b) Create a worker carrying that tag via workersPage.create({tags:[name]})
 *       — the create modal's #tagSelector ng-select. This proves a worker
 *       actually carries the tag (the tag→workers expansion target), not just
 *       that an orphan tag exists.
 * No direct API call is needed; both flows use stable element ids already
 * exercised by other specs, so the UI path is the most robust choice.
 *
 * Placed in the l/ shard alongside the other calendar specs; reuses
 * CalendarUiEnhancementsPage (parent-level page object).
 */

const rand = generateRandmString(5);

const property: PropertyCreateUpdate = {
  name: `PropWT-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const workerTagName = `WT-${rand}`;

const worker: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
  tags: [workerTagName],
};

const title = `EventWT-${rand}`;

// Predicate: the create backend call (POST .../calendar/tasks) excluding the
// week reload + move/resize sibling routes.
function isCreatePost(method: string, url: string): boolean {
  return (
    url.includes('/api/backend-configuration-pn/calendar/tasks') &&
    !url.includes('/tasks/week') &&
    !url.includes('/tasks/move') &&
    !url.includes('/tasks/resize') &&
    method === 'POST'
  );
}

test.describe.serial('Calendar "Assign to worker tags" field', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);
  });

  test('tag-only event: pick a worker tag, no explicit assignee, save', async ({ page }) => {
    test.setTimeout(900000);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    // ------------------------------------------------------------------
    // Step 1: seed property + a worker carrying a freshly-created tag.
    // ------------------------------------------------------------------
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    await workersPage.goToPropertyWorkers();
    // Create the worker tag first so #tagSelector can pick it when creating
    // the worker (and so it shows up in the calendar worker-tags dropdown).
    await workersPage.createTag(workerTagName);
    await workersPage.create(worker);

    // ------------------------------------------------------------------
    // Step 2: open the calendar, select the property, open create modal on
    // a FUTURE slot (next week, Monday 09:00).
    // ------------------------------------------------------------------
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();

    const folderResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
      { timeout: 60000 }
    );
    await calendarPage.selectProperty(property.name);
    await folderResp.catch(() => undefined);
    await page.waitForTimeout(1500);

    await calendarPage.openCreateModalAtSlot(0, 9);

    // ------------------------------------------------------------------
    // Step 3: fill required fields (title + first eForm + first report
    // headline). Deliberately leave #calendarEventAssignee EMPTY so the
    // event is assigned by WORKER TAG only.
    // ------------------------------------------------------------------
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

    // ------------------------------------------------------------------
    // Step 4: the worker-tags field must be present (the seeded tag makes
    // data.workerTags non-empty). Pick the seeded tag by name.
    // ------------------------------------------------------------------
    const workerTags = page.locator('#calendarEventWorkerTags');
    await expect(
      workerTags,
      'worker-tags field should render once at least one tag exists'
    ).toBeVisible({ timeout: 10000 });

    await workerTags.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await page
      .locator('.ng-dropdown-panel .ng-option')
      .filter({ hasText: workerTagName })
      .first()
      .click();
    await page.waitForTimeout(300);
    // Close the dropdown by clicking the title input.
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    await expect(
      page.locator('#calendarEventWorkerTags .ng-value-label').filter({ hasText: workerTagName }).first(),
      'the picked worker tag should display as a chip'
    ).toBeVisible({ timeout: 5000 });

    // Sanity: assignee was left empty — prove tag-only assignment.
    expect(
      await page.locator('#calendarEventAssignee .ng-value-label').count(),
      'assignee must be empty to prove tag-only assignment'
    ).toBe(0);

    // ------------------------------------------------------------------
    // Step 5: save — capture the create POST body + response.
    // ------------------------------------------------------------------
    const reqPromise = page.waitForRequest(
      r => isCreatePost(r.method(), r.url()),
      { timeout: 30000 }
    );
    const respPromise = page.waitForResponse(
      r => isCreatePost(r.request().method(), r.url()),
      { timeout: 30000 }
    );
    await page.locator('#calendarEventSaveBtn').click();
    const req = await reqPromise;
    const resp = await respPromise;
    const body = req.postDataJSON();
    const resBody = await resp.json().catch(() => null);

    console.log(
      `POST /calendar/tasks: status=${resp.status()}, success=${resBody?.success}, ` +
      `workerTagIds=${JSON.stringify(body?.workerTagIds)}, sites=${JSON.stringify(body?.sites)}`
    );

    // The relaxed validation accepted a tag-only event (200).
    expect(resp.status(), 'create POST should return 200').toBe(200);
    expect(resBody?.success, 'create should report success').toBeTruthy();

    // workerTagIds is a non-empty number[].
    expect(
      Array.isArray(body?.workerTagIds),
      `workerTagIds should be an array; got ${JSON.stringify(body?.workerTagIds)}`
    ).toBeTruthy();
    expect(
      body.workerTagIds.length,
      'workerTagIds should be non-empty (the picked tag)'
    ).toBeGreaterThanOrEqual(1);
    for (const id of body.workerTagIds) {
      expect(typeof id).toBe('number');
      expect(id).toBeGreaterThan(0);
    }

    // No explicit assignee → sites empty/absent (proves relaxed validation).
    const sites = Array.isArray(body?.sites) ? body.sites : [];
    expect(
      sites.length,
      `sites should be empty for a tag-only event; got ${JSON.stringify(body?.sites)}`
    ).toBe(0);

    // ------------------------------------------------------------------
    // Step 6: the event renders on the calendar grid.
    // ------------------------------------------------------------------
    await page.waitForTimeout(1500);
    await calendarPage
      .findEventBlock(title)
      .waitFor({ state: 'visible', timeout: 15000 });

    // ------------------------------------------------------------------
    // Step 7 (round-trip): reopen in edit mode; the worker-tags field
    // should be pre-populated with the seeded tag.
    // ------------------------------------------------------------------
    await calendarPage.openEditModal(title);
    await expect(
      page.locator('#calendarEventWorkerTags .ng-value-label').filter({ hasText: workerTagName }).first(),
      'edit mode should rehydrate the worker-tags field with the seeded tag'
    ).toBeVisible({ timeout: 10000 });
    // Close the edit modal without saving.
    await calendarPage.closeEventModal();
  });

  // Best-effort cleanup; the matrix slot runs against an ephemeral DB.
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
});
