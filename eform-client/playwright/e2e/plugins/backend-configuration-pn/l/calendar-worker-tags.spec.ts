import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import { assigneeTeamOptions, assigneeWorkerOptions } from '../calendar-assignee.helper';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';

/**
 * E2E for the task modal's merged "Vælg medarbejder / team" picker (#1295).
 *
 * The separate "Assign to worker tags" select (id="calendarEventWorkerTags") is
 * GONE: teams (worker tags maintained under Medarbejdere → Etiketter) and
 * workers are picked in ONE grouped select, `#calendarEventAssignee` — a Teams
 * group FIRST, then the workers group LAST; the Teams group is absent when the
 * selected property has no teams. Teams are property-scoped: only teams with at
 * least one live member linked to the selected property are offered.
 *
 * The create POST (.../calendar/tasks) still ships the two unchanged fields:
 *   - sites        : number[]  (worker picks — LEFT EMPTY here)
 *   - workerTagIds : number[]  (team picks — the field under test)
 * so a team-only pick proves the modal splits the merged selection back.
 *
 * Seeding is UI-only through the ID-stable property-workers page objects:
 *   (a) the team (tag) via workersPage.createTag(name),
 *   (b) a worker on property A carrying it via workersPage.create({tags:[name]}),
 *   (c) a worker on property B WITHOUT it — B must offer no Teams group.
 */

const rand = generateRandmString(5);

const property: PropertyCreateUpdate = {
  name: `PropWT-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// Property B: a worker WITHOUT the team, so B has workers but no teams.
const propertyB: PropertyCreateUpdate = {
  name: `PropWTB-${rand}`,
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

const workerB: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [propertyB.name],
  workerEmail: generateRandmString(5) + '@test.com',
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

test.describe.serial('Calendar merged worker/team picker (#1295)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);
  });

  test('team-only event: pick a team in the merged picker, no worker, save', async ({ page }) => {
    test.setTimeout(900000);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    // ------------------------------------------------------------------
    // Step 1: seed property + a worker carrying a freshly-created tag.
    // ------------------------------------------------------------------
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await propertiesPage.createProperty(propertyB);

    await workersPage.goToPropertyWorkers();
    // Create the worker tag first so #tagSelector can pick it when creating
    // the worker (and so it shows up as a team for the worker's property).
    await workersPage.createTag(workerTagName);
    await workersPage.create(worker);
    await workersPage.create(workerB);

    // ------------------------------------------------------------------
    // Step 2: open the calendar, select the property, open create modal on
    // a FUTURE slot (next week, Monday 09:00).
    // ------------------------------------------------------------------
    await calendarPage.goToCalendar();

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
    // headline). Pick ONLY the team in #calendarEventAssignee so the event
    // is assigned by team alone.
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
    // Step 4: the separate worker-tags select is gone; the merged picker
    // lists the Teams group FIRST and the workers LAST. Pick the team by
    // label inside the Teams group.
    // ------------------------------------------------------------------
    await expect(
      page.locator('#calendarEventWorkerTags'),
      'the separate "Assign to worker tags" select must be removed (#1295)'
    ).toHaveCount(0);

    const assignee = page.locator('#calendarEventAssignee');
    await assignee.click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });

    // Two groups on property A: Teams, then workers.
    await expect(page.locator('.ng-dropdown-panel .ng-optgroup')).toHaveCount(2);
    await expect(
      assigneeTeamOptions(page).filter({ hasText: workerTagName }),
      'the team must be listed in the FIRST (Teams) group'
    ).toHaveCount(1);
    await expect(
      assigneeWorkerOptions(page).filter({ hasText: worker.name }),
      'the worker must be listed in the LAST (workers) group'
    ).toHaveCount(1);

    await assigneeTeamOptions(page).filter({ hasText: workerTagName }).click();
    await page.waitForTimeout(300);
    // Close the dropdown by clicking the title input.
    await page.locator('#calendarEventTitle').click();
    await page.waitForTimeout(300);

    const teamChip = page
      .locator('#calendarEventAssignee .ng-value')
      .filter({ has: page.locator('.ng-value-label', { hasText: workerTagName }) });
    await expect(teamChip, 'the picked team should display as a chip').toHaveCount(1);
    await expect(
      teamChip.locator('mat-icon'),
      'a team chip carries the group icon'
    ).toHaveCount(1);

    // Sanity: the team is the ONLY chip — proves team-only assignment.
    expect(
      await page.locator('#calendarEventAssignee .ng-value-label').count(),
      'only the team chip may be selected to prove team-only assignment'
    ).toBe(1);

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

    // A team-only event is accepted (200).
    expect(resp.status(), 'create POST should return 200').toBe(200);
    expect(resBody?.success, 'create should report success').toBeTruthy();

    // workerTagIds is a non-empty number[].
    expect(
      Array.isArray(body?.workerTagIds),
      `workerTagIds should be an array; got ${JSON.stringify(body?.workerTagIds)}`
    ).toBeTruthy();
    expect(
      body.workerTagIds.length,
      'workerTagIds should be non-empty (the picked team)'
    ).toBeGreaterThanOrEqual(1);
    for (const id of body.workerTagIds) {
      expect(typeof id).toBe('number');
      expect(id).toBeGreaterThan(0);
    }

    // No worker picked → sites empty/absent (the merged keys split correctly).
    const sites = Array.isArray(body?.sites) ? body.sites : [];
    expect(
      sites.length,
      `sites should be empty for a team-only event; got ${JSON.stringify(body?.sites)}`
    ).toBe(0);

    // ------------------------------------------------------------------
    // Step 6: the event renders on the calendar grid.
    // ------------------------------------------------------------------
    await page.waitForTimeout(1500);
    await calendarPage
      .findEventBlock(title)
      .waitFor({ state: 'visible', timeout: 15000 });

    // ------------------------------------------------------------------
    // Step 7 (round-trip): reopen in edit mode; the merged picker should be
    // pre-populated with the team chip.
    // ------------------------------------------------------------------
    await calendarPage.openEditModal(title);
    await expect(
      page.locator('#calendarEventAssignee .ng-value-label').filter({ hasText: workerTagName }).first(),
      'edit mode should rehydrate the merged picker with the team'
    ).toBeVisible({ timeout: 10000 });
    // Close the edit modal without saving.
    await calendarPage.closeEventModal();

    // ------------------------------------------------------------------
    // Step 8: teams are PROPERTY-scoped. Property B has a worker but no
    // member of the team, so its picker has no Teams group at all.
    // ------------------------------------------------------------------
    await calendarPage.selectProperty(propertyB.name);
    await page.waitForTimeout(1500);
    await calendarPage.openCreateModalAtSlot(1, 9);
    await page.locator('#calendarEventAssignee').click();
    await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await expect(
      page.locator('.ng-dropdown-panel .ng-optgroup'),
      'only the workers group on a property without teams'
    ).toHaveCount(1);
    await expect(assigneeTeamOptions(page)).toHaveCount(0);
    await expect(
      page.locator('.ng-dropdown-panel .ng-option').filter({ hasText: workerTagName }),
      'a team with no member on this property must not be offered'
    ).toHaveCount(0);
    await expect(assigneeWorkerOptions(page).filter({ hasText: workerB.name })).toHaveCount(1);
    await page.locator('#calendarEventTitle').click();
    await calendarPage.closeEventModal();
  });

  // Best-effort cleanup; the matrix slot runs against an ephemeral DB.
  test.afterAll(async ({ browser }) => {
    // browser.newPage() can itself reject — a browser that crashed or got
    // disconnected during a long run — and an exception thrown here escapes the
    // hook and fails the job, which is exactly what this non-fatal teardown
    // exists to prevent. Record it and give up on cleanup instead.
    const page = await browser.newPage().catch((err: any) => {
      console.log(`afterAll cleanup failed (non-fatal): could not open a cleanup page: ${err?.message ?? err}`);
      return undefined;
    });
    if (!page) {
      return;
    }
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
