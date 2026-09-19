import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';

/**
 * E2E for #1256: the calendar toolbar's Teams filter (`#calendarAssigneesButton`,
 * Teams section) is PROPERTY-scoped, like its Employees section.
 *
 * Seeding (UI only, ID-stable page objects):
 *   - property A and property B,
 *   - team "shared"  : worker A (on A only) and worker B (on B only),
 *   - team "B-only"  : worker B only — no member on A,
 *   - on B: one event assigned to the shared TEAM, one assigned to worker B by name.
 *
 * What is pinned:
 *   1. On A the B-only team is not offered; the shared team is.
 *   2. Switching to B re-requests the team list FOR B (`worker-tags?propertyId=`)
 *      and now offers both teams.
 *   3. Selecting the shared team on B shows the team's event (not worker B's own
 *      event), and the server reports that event's team members as B's member
 *      ONLY — worker A, who is in the same team but only on property A, is not an
 *      assignee there (the same set the team deploys to).
 *   4. Switching back to A clears the team selection and drops the B-only team again.
 *   5. A property with no linked workers offers no teams: the Teams section is not
 *      rendered at all (the header hides it when the scoped list is empty).
 *
 * Worker B's SDK site id is taken from the `worker-tags?propertyId=B` response itself
 * (`memberSiteIds`), so the team-member assertion is an exact id, not a count.
 */

const rand = generateRandmString(5);

const propertyA: PropertyCreateUpdate = {
  name: `PropTSA-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const propertyB: PropertyCreateUpdate = {
  name: `PropTSB-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// No worker is ever linked to C, so no team has a member on it.
const propertyC: PropertyCreateUpdate = {
  name: `PropTSC-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// Neither name is a substring of the other, so filtering by text is unambiguous.
const sharedTeam = `TShared-${rand}`;
const bOnlyTeam = `TBonly-${rand}`;

const workerAFirst = `Ada${rand}`;
const workerALast = `Aone${rand}`;
const workerBFirst = `Ben${rand}`;
const workerBLast = `Btwo${rand}`;
const workerBName = `${workerBFirst} ${workerBLast}`;
const workerAName = `${workerAFirst} ${workerALast}`;

const workerA: PropertyWorker = {
  name: workerAFirst,
  surname: workerALast,
  language: 'Dansk',
  properties: [propertyA.name],
  workerEmail: `${generateRandmString(5)}@test.com`,
  tags: [sharedTeam],
};

const workerB: PropertyWorker = {
  name: workerBFirst,
  surname: workerBLast,
  language: 'Dansk',
  properties: [propertyB.name],
  workerEmail: `${generateRandmString(5)}@test.com`,
  tags: [sharedTeam, bOnlyTeam],
};

const teamEventOnB = `EvtTeamB-${rand}`;
const workerEventOnB = `EvtWorkerB-${rand}`;

interface ScopedTeam {
  id: number;
  name: string;
  memberSiteIds?: number[] | null;
}

/**
 * Select a property and wait for ITS property-scoped team list to arrive. Returns the
 * request url and the teams it offered (with their property-linked `memberSiteIds`).
 */
async function selectPropertyAndAwaitTeams(
  page: Page,
  calendar: CalendarUiEnhancementsPage,
  name: string,
  propertyId: number
): Promise<{ url: string; teams: ScopedTeam[] }> {
  // Match THIS property's id exactly: the calendar's own initial loadTeams() for the
  // auto-selected default property can still be in flight, and a loose
  // `propertyId=` match would be satisfied by that stale response.
  const teamsLoaded = page.waitForResponse(
    r => {
      const url = new URL(r.url());
      return url.pathname.endsWith('/api/backend-configuration-pn/worker-tags')
        && url.searchParams.get('propertyId') === String(propertyId);
    },
    { timeout: API_TIMEOUT }
  );
  teamsLoaded.catch(() => undefined);
  await calendar.selectProperty(name);
  const response = await teamsLoaded;
  const body = await response.json();
  return { url: response.url(), teams: (body?.model ?? []) as ScopedTeam[] };
}

/** The property-linked member site ids the scoped list reports for one team. */
function memberSiteIdsOf(teams: ScopedTeam[], teamName: string): number[] {
  const team = teams.find(t => t.name === teamName);
  expect(team, `the scoped team list offers ${teamName}`).toBeTruthy();
  return [...(team?.memberSiteIds ?? [])].sort((x, y) => x - y);
}

/** A chip in the open preview popover, matched by its exact label (anchored). */
function previewChip(page: Page, label: string) {
  return page.locator('app-task-preview-modal .preview-chip', { hasText: new RegExp(`^\\s*${label}\\s*$`) });
}

async function closePreviewPopover(page: Page): Promise<void> {
  await page
    .locator('app-task-preview-modal .preview-actions-row button')
    .filter({ has: page.locator('mat-icon', { hasText: 'close' }) })
    .first()
    .click();
  await page.locator('app-task-preview-modal').waitFor({ state: 'detached', timeout: UI_TIMEOUT });
}

test.describe.serial('Calendar toolbar Teams filter is property-scoped (#1256)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
  });

  test('teams are offered per property, and a team shows only this property\'s members', async ({ page }) => {
    test.setTimeout(900000);

    const calendar = new CalendarUiEnhancementsPage(page);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    // --- seed -------------------------------------------------------------
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(propertyA);
    await propertiesPage.createProperty(propertyB);
    await propertiesPage.createProperty(propertyC);

    await workersPage.goToPropertyWorkers();
    // The tags must exist before a worker can be created carrying them.
    await workersPage.createTag(sharedTeam);
    await workersPage.createTag(bOnlyTeam);
    await workersPage.create(workerA);
    await workersPage.create(workerB);

    // Resolve the seeded properties' ids from the calendar's own property list, so each
    // team-list wait below can match its property's `worker-tags?propertyId=` exactly.
    const dictionaryLoaded = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/properties/dictionary'),
      { timeout: API_TIMEOUT }
    );
    dictionaryLoaded.catch(() => undefined);
    await calendar.goToCalendar();
    const dictionary = ((await (await dictionaryLoaded).json())?.model ?? []) as { id: number; name: string }[];
    const idOf = (propertyName: string): number => {
      const match = dictionary.find(p => p.name === propertyName);
      expect(match, `the calendar's property list offers ${propertyName}`).toBeTruthy();
      return match!.id;
    };
    const propertyAId = idOf(propertyA.name);
    const propertyBId = idOf(propertyB.name);
    const propertyCId = idOf(propertyC.name);

    // --- 1. property A: the B-only team is not offered ---------------------
    const { url: urlA, teams: teamsOnA } = await selectPropertyAndAwaitTeams(page, calendar, propertyA.name, propertyAId);
    const workerASiteIds = memberSiteIdsOf(teamsOnA, sharedTeam);
    expect(workerASiteIds.length, 'on A the shared team\'s only linked member is worker A').toBe(1);
    expect(teamsOnA.find(t => t.name === bOnlyTeam), 'the B-only team is not in A\'s scoped list').toBeUndefined();
    await calendar.openAssigneeFilter();
    await expect(calendar.filterTeamRow(sharedTeam), 'the shared team has a member on A')
      .toBeVisible({ timeout: UI_TIMEOUT });
    await expect(
      calendar.filterTeamRow(bOnlyTeam),
      'a team whose only member works on another property must not be offered (#1256)'
    ).toHaveCount(0);
    await calendar.closeAssigneeFilter();

    // --- 2. property B: the list is reloaded for B --------------------------
    const { url: urlB, teams: teamsOnB } = await selectPropertyAndAwaitTeams(page, calendar, propertyB.name, propertyBId);
    expect(urlB, 'switching property must request the NEW property\'s teams').not.toEqual(urlA);

    // Worker B's site id, as the server itself reports it: the shared team's ONLY member
    // linked to B, and likewise the B-only team's. Worker A is in the shared team too, but
    // is linked only to A, so is not a member here (#1256).
    const workerBSiteIds = memberSiteIdsOf(teamsOnB, sharedTeam);
    expect(workerBSiteIds.length, 'on B the shared team\'s only linked member is worker B').toBe(1);
    const workerBSiteId = workerBSiteIds[0];
    expect(workerBSiteId, 'worker B is not worker A').not.toEqual(workerASiteIds[0]);
    expect(memberSiteIdsOf(teamsOnB, bOnlyTeam), 'the B-only team\'s member on B is worker B').toEqual([workerBSiteId]);

    await calendar.openAssigneeFilter();
    await expect(calendar.filterTeamRow(sharedTeam)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(calendar.filterTeamRow(bOnlyTeam), 'B\'s own team is offered on B')
      .toBeVisible({ timeout: UI_TIMEOUT });
    await calendar.closeAssigneeFilter();

    // --- seed events on B ---------------------------------------------------
    await calendar.openCreateModalAtSlot(0, 9);
    await calendar.fillAndSaveEventForTeam(teamEventOnB, sharedTeam);
    await calendar.openCreateModalOnCurrentWeek(1, 9);
    await calendar.fillAndSaveEventForWorker(workerEventOnB, workerBName);

    await expect(calendar.findEventBlock(teamEventOnB)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(workerEventOnB)).toBeVisible({ timeout: UI_TIMEOUT });

    // The preview names the assignment: the team event by the team's name only (the team
    // is resolved to members server-side, never listed by worker on the tile), worker B's
    // own event by worker B — and neither ever by worker A, who is not on property B.
    await calendar.openEventPreview(teamEventOnB);
    await expect(previewChip(page, sharedTeam), 'the team event previews its team').toBeVisible({ timeout: UI_TIMEOUT });
    await expect(previewChip(page, workerAName)).toHaveCount(0);
    await expect(previewChip(page, workerBName)).toHaveCount(0);
    await closePreviewPopover(page);

    await calendar.openEventPreview(workerEventOnB);
    await expect(previewChip(page, workerBName), 'worker B\'s own event previews worker B')
      .toBeVisible({ timeout: UI_TIMEOUT });
    await expect(previewChip(page, workerAName), 'worker A is never named on property B').toHaveCount(0);
    await closePreviewPopover(page);

    // --- 3. select the shared team on B -------------------------------------
    await calendar.openAssigneeFilter();
    const weekResponse = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/week')
        && r.request().method() === 'POST',
      { timeout: API_TIMEOUT }
    );
    weekResponse.catch(() => undefined);
    await calendar.toggleFilterTeam(sharedTeam);
    const body = await (await weekResponse).json();

    await expect(calendar.findEventBlock(teamEventOnB), 'the team\'s event on this property is shown')
      .toBeVisible({ timeout: UI_TIMEOUT });
    await expect(
      calendar.findEventBlock(workerEventOnB),
      'a team filter matches team-assigned events, not a member\'s own events'
    ).toBeHidden({ timeout: UI_TIMEOUT });

    const teamTasks = (body?.model ?? []).filter((t: any) => t.title === teamEventOnB);
    expect(teamTasks.length, 'the week response carries the team event').toBeGreaterThan(0);
    for (const task of teamTasks) {
      expect(
        task.teamAssigneeIds ?? [],
        'the team event\'s members on B are exactly worker B — worker A is in the same team '
          + 'but only on property A (#1256)'
      ).toEqual([workerBSiteId]);
      expect(task.teamAssigneeIds ?? [], 'worker A is never a team assignee on property B')
        .not.toContain(workerASiteIds[0]);
    }

    // The B-only team is selectable on B too; select it so step 4 has a B-only
    // selection that property A cannot offer.
    await calendar.toggleFilterTeam(bOnlyTeam);
    expect(await calendar.checkedFilterCount()).toBe(2);
    await calendar.closeAssigneeFilter();

    // --- 4. back to A: selection cleared, B-only team gone again ------------
    const backToA = await calendar.captureNextWeekRequest(() => calendar.selectProperty(propertyA.name));
    expect(backToA.workerTagIds ?? [], 'a property switch clears the team selection').toEqual([]);

    await calendar.openAssigneeFilter();
    await expect(calendar.filterTeamRow(sharedTeam)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(calendar.filterTeamRow(bOnlyTeam)).toHaveCount(0);
    expect(await calendar.checkedFilterCount(), 'nothing is left ticked').toBe(0);
    await calendar.closeAssigneeFilter();

    // --- 5. property C (no linked workers): no teams at all -----------------
    const { teams: teamsOnC } = await selectPropertyAndAwaitTeams(page, calendar, propertyC.name, propertyCId);
    expect(teamsOnC, 'a property with no linked workers is offered no teams (#1256)').toEqual([]);

    await calendar.openAssigneeFilter();
    await expect(
      page.locator('#calendarTeamsHeading'),
      'with no team to offer, the Teams section is not rendered'
    ).toHaveCount(0);
    await expect(page.locator('.team-row')).toHaveCount(0);
    await expect(calendar.filterTeamRow(sharedTeam)).toHaveCount(0);
    await calendar.closeAssigneeFilter();
  });

  // Best-effort cleanup; the matrix slot runs against an ephemeral DB.
  test.afterAll(async ({ browser }) => {
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
      await workersPage.clearTable();
      // Worker tags are installation-scoped: neither clearTable() removes them.
      await workersPage.deleteTag(sharedTeam);
      await workersPage.deleteTag(bOnlyTeam);

      const propertiesPage = new BackendConfigurationPropertiesPage(page);
      await propertiesPage.goToProperties();
      await propertiesPage.clearTable();
    };
    try {
      await Promise.race([cleanup(), new Promise(resolve => setTimeout(resolve, 60000))]);
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    }
    try {
      await page.close();
    } catch {}
  });
});
