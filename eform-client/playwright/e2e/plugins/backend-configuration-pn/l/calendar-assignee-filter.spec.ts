import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import { UI_TIMEOUT } from '../wait-helpers';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';

/**
 * E2E for the calendar toolbar's Teams + Employees filter (#1211,
 * `#calendarAssigneesButton`).
 *
 * Before this feature no user-reachable employee filter shipped — the sidebar
 * panel that drove `activeSiteIds` was commented out — so there is nothing
 * here to update, only new coverage.
 *
 * What the three tests pin down, in the order the issue lists them:
 *   1. multi-selecting two employees (and that the SERVER does the filtering),
 *   2. mixing a team with an employee — a union, with the two lists staying
 *      independent of each other,
 *   3. the reset entry.
 *
 * Seeding, all through existing ID-stable page objects, no direct API calls:
 *   - one property,
 *   - one worker tag ("team"),
 *   - worker A, linked to the property, WITHOUT the tag,
 *   - worker B, linked to the property, WITH the tag,
 *   - three events on one future week: one assigned to A, one to B, and one
 *     assigned to the TEAM with no individual assignee.
 *
 * That third event is not redundant. `ShouldIncludeTask` matches the worker
 * tags assigned to the TASK, so an event assigned to worker B — who carries
 * the tag — is not a team event and must NOT appear under a team filter. Only
 * a tag-assigned event can prove the team half works.
 *
 * The matching rule expands in ONE direction, sites -> tags, and the
 * assertions below depend on that asymmetry:
 *   - selecting a SITE also matches tasks tagged with any team that site
 *     belongs to (`GetTasksForWeek` unions the selected sites' tags into
 *     `effectiveWorkerTagIds`), so picking worker B pulls in the team event;
 *   - selecting a TEAM does NOT match that team's members' own events —
 *     there is no tags -> sites expansion, so worker B's event stays out
 *     under a team-only filter.
 *
 * The suite is `serial`: test 1 does the seeding, and tests 2 and 3 reuse what
 * it left in the database rather than paying for it three times.
 */

const rand = generateRandmString(5);

const property: PropertyCreateUpdate = {
  name: `PropAF-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const teamName = `TeamAF-${rand}`;

// Names are asserted on as displayed, so they are built from the parts the
// employee list joins: `fullName` is "<first> <last>".
const workerAFirst = `Anna${rand}`;
const workerALast = `Alpha${rand}`;
const workerBFirst = `Bo${rand}`;
const workerBLast = `Beta${rand}`;
const workerAName = `${workerAFirst} ${workerALast}`;
const workerBName = `${workerBFirst} ${workerBLast}`;

const workerA: PropertyWorker = {
  name: workerAFirst,
  surname: workerALast,
  language: 'Dansk',
  properties: [property.name],
  workerEmail: `${generateRandmString(5)}@test.com`,
};

const workerB: PropertyWorker = {
  name: workerBFirst,
  surname: workerBLast,
  language: 'Dansk',
  properties: [property.name],
  workerEmail: `${generateRandmString(5)}@test.com`,
  tags: [teamName],
};

const eventA = `EvtA-${rand}`;
const eventB = `EvtB-${rand}`;
const eventTeam = `EvtTeam-${rand}`;

// The events are created one week ahead (openCreateModalAtSlot advances a
// week so the slot is in the future and the click is accepted). Every later
// test has to stand on that same week before it can see them.
const EVENT_WEEK_OFFSET = 1;

test.describe.serial('Calendar toolbar employee/team filter', () => {
  test.beforeEach(async ({ page }) => {
    // `login()` ends on a real post-condition (#newEFormBtn visible), so there
    // is nothing left to wait for here.
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
  });

  test('multi-select: two employees narrow the grid, and the filter reaches the server', async ({ page }) => {
    // Seeding walks the properties page, the workers page and three create
    // modals, each of which provisions through the SDK. The 120s default is
    // for a single flow, not for a fixture this size.
    test.setTimeout(900000);

    const calendar = new CalendarUiEnhancementsPage(page);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    // --- seed -------------------------------------------------------------
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    await workersPage.goToPropertyWorkers();
    // The tag must exist before worker B can be created carrying it.
    await workersPage.createTag(teamName);
    await workersPage.create(workerA);
    await workersPage.create(workerB);

    await calendar.goToCalendar();
    await calendar.selectProperty(property.name);

    // Three events on ONE week: the first call advances to it, the other two
    // stay on it.
    await calendar.openCreateModalAtSlot(0, 9);
    await calendar.fillAndSaveEventForWorker(eventA, workerAName);

    await calendar.openCreateModalOnCurrentWeek(1, 9);
    await calendar.fillAndSaveEventForWorker(eventB, workerBName);

    await calendar.openCreateModalOnCurrentWeek(2, 9);
    await calendar.fillAndSaveEventForTeam(eventTeam, teamName);

    // --- the unfiltered baseline -----------------------------------------
    await expect(calendar.findEventBlock(eventA)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(eventB)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(eventTeam)).toBeVisible({ timeout: UI_TIMEOUT });

    await calendar.openAssigneeFilter();

    // Both workers are offered even though neither is "the first option", and
    // the button starts on the reset label.
    await expect(calendar.filterEmployeeRow(workerAName)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(calendar.filterEmployeeRow(workerBName)).toBeVisible({ timeout: UI_TIMEOUT });
    expect(await calendar.checkedFilterCount(), 'nothing is selected on arrival').toBe(0);
    expect(
      await calendar.assigneeFilterLabel(),
      'the empty filter labels itself with the reset entry'
    ).toBe(await calendar.assigneeResetLabel());

    // --- one employee -----------------------------------------------------
    const firstPick = await calendar.captureNextWeekRequest(() =>
      calendar.toggleFilterEmployee(workerAName)
    );
    expect(
      Array.isArray(firstPick.siteIds) && firstPick.siteIds.length,
      `siteIds should carry the one picked worker; got ${JSON.stringify(firstPick.siteIds)}`
    ).toBe(1);
    // The filter is an assignee filter, not a team one — nothing leaks across.
    expect(firstPick.workerTagIds, 'no team was picked').toEqual([]);

    // With exactly one entry selected the button names it.
    expect(await calendar.assigneeFilterLabel()).toBe(workerAName);

    await expect(calendar.findEventBlock(eventA)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(eventB)).toBeHidden({ timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(eventTeam)).toBeHidden({ timeout: UI_TIMEOUT });

    // --- two employees: additive, not replacing ---------------------------
    const secondPick = await calendar.captureNextWeekRequest(() =>
      calendar.toggleFilterEmployee(workerBName)
    );
    expect(
      secondPick.siteIds.length,
      `the second pick must ADD to the first; got ${JSON.stringify(secondPick.siteIds)}`
    ).toBe(2);
    // The first pick's id survived — proof this is a multi-select and not a
    // radio group wearing checkboxes.
    expect(secondPick.siteIds).toContain(firstPick.siteIds[0]);

    expect(await calendar.checkedFilterCount()).toBe(2);
    // Past one entry the label is a count. Asserted on the digit rather than
    // the whole phrase so the test does not encode the UI language.
    expect(await calendar.assigneeFilterLabel()).toMatch(/\b2\b/);

    await expect(calendar.findEventBlock(eventA)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(eventB)).toBeVisible({ timeout: UI_TIMEOUT });
    // The tag-assigned event IS in now, and no team was picked. That is the
    // documented expansion, not a leak: `GetTasksForWeek` seeds
    // `effectiveWorkerTagIds` from the requested WorkerTagIds and then unions
    // in the tags of every requested SITE, so selecting worker B — who carries
    // the team tag — makes every task tagged with that team match too.
    // Contrast the single-employee step above: with only the UNTAGGED worker A
    // selected there is nothing to expand to, and this same event is hidden.
    // That pair is what proves the expansion is membership-driven rather than
    // "any selection shows everything".
    await expect(calendar.findEventBlock(eventTeam)).toBeVisible({ timeout: UI_TIMEOUT });

    // --- unticking is the inverse ----------------------------------------
    const unpick = await calendar.captureNextWeekRequest(() =>
      calendar.toggleFilterEmployee(workerAName)
    );
    expect(unpick.siteIds.length).toBe(1);
    expect(unpick.siteIds).not.toContain(firstPick.siteIds[0]);
    expect(await calendar.assigneeFilterLabel()).toBe(workerBName);
    await expect(calendar.findEventBlock(eventA)).toBeHidden({ timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(eventB)).toBeVisible({ timeout: UI_TIMEOUT });
    // Worker B is the tagged one, so the expansion survives the untick: the
    // remaining selection still resolves to the team and the tag-assigned
    // event stays on screen. Unticking A removes A's event, not B's team.
    await expect(calendar.findEventBlock(eventTeam)).toBeVisible({ timeout: UI_TIMEOUT });
  });

  test('a team and an employee combine as a union, and the two lists stay independent', async ({ page }) => {
    test.setTimeout(300000);

    const calendar = new CalendarUiEnhancementsPage(page);
    await calendar.goToCalendar();
    await calendar.selectProperty(property.name);
    await navigateToEventWeek(calendar);

    await calendar.openAssigneeFilter();
    const employeesBefore = await calendar.filterEmployeeNames();
    expect(employeesBefore, 'both seeded workers are listed').toEqual(
      expect.arrayContaining([workerAName, workerBName])
    );

    // --- team alone -------------------------------------------------------
    const teamOnly = await calendar.captureNextWeekRequest(() =>
      calendar.toggleFilterTeam(teamName)
    );
    expect(
      teamOnly.workerTagIds.length,
      `workerTagIds should carry the picked team; got ${JSON.stringify(teamOnly.workerTagIds)}`
    ).toBe(1);
    expect(teamOnly.siteIds, 'picking a team must not populate siteIds').toEqual([]);

    // Picking a team must NOT tick its members. Worker B carries the tag; if
    // the control auto-synced, B's row would now be checked and the request
    // above would have carried B's site id.
    expect(
      await calendar.checkedFilterCount(),
      'exactly one row — the team — is ticked; no member was auto-ticked'
    ).toBe(1);
    await expect(calendar.filterEmployeeRow(workerBName)).toHaveAttribute('aria-checked', 'false', { timeout: UI_TIMEOUT });

    // The employee list is every worker on the property regardless of the team
    // filter (`tagIds: []`) — it must not shrink to the team's members.
    expect(
      await calendar.filterEmployeeNames(),
      'the employee list does not narrow when a team is selected'
    ).toEqual(employeesBefore);

    // Only the tag-ASSIGNED event matches. Worker B's own event does not,
    // even though B is in the team.
    await expect(calendar.findEventBlock(eventTeam)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(eventA)).toBeHidden({ timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(eventB)).toBeHidden({ timeout: UI_TIMEOUT });

    // --- team + employee: a union ----------------------------------------
    const mixed = await calendar.captureNextWeekRequest(() =>
      calendar.toggleFilterEmployee(workerAName)
    );
    expect(mixed.workerTagIds, 'the team survives an employee pick').toEqual(teamOnly.workerTagIds);
    expect(mixed.siteIds.length, 'the employee is added beside it').toBe(1);

    // Both halves are on screen at once. An intersection would render neither.
    await expect(calendar.findEventBlock(eventTeam)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(eventA)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(eventB)).toBeHidden({ timeout: UI_TIMEOUT });

    // And the reverse independence: picking an employee left the team ticked
    // and ticked nothing else.
    expect(await calendar.checkedFilterCount()).toBe(2);
    await expect(calendar.filterTeamRow(teamName)).toHaveAttribute('aria-checked', 'true', { timeout: UI_TIMEOUT });
    await expect(calendar.filterEmployeeRow(workerBName)).toHaveAttribute('aria-checked', 'false', { timeout: UI_TIMEOUT });
  });

  test('the reset entry clears both lists in one reload', async ({ page }) => {
    test.setTimeout(300000);

    const calendar = new CalendarUiEnhancementsPage(page);
    await calendar.goToCalendar();
    await calendar.selectProperty(property.name);
    await navigateToEventWeek(calendar);

    // Build a mixed selection so the reset has both kinds to clear.
    await calendar.openAssigneeFilter();
    await calendar.toggleFilterTeam(teamName);
    await calendar.toggleFilterEmployee(workerAName);
    expect(await calendar.checkedFilterCount()).toBe(2);
    await expect(calendar.findEventBlock(eventB)).toBeHidden({ timeout: UI_TIMEOUT });

    const cleared = await calendar.captureNextWeekRequest(() => calendar.resetAssigneeFilter());
    expect(cleared.siteIds, 'the reset clears the employees').toEqual([]);
    expect(cleared.workerTagIds, 'the reset clears the teams').toEqual([]);

    expect(await calendar.checkedFilterCount(), 'no row is left ticked').toBe(0);
    // The label follows the reset — compared against the reset row's own text
    // so the assertion holds in any UI language.
    expect(await calendar.assigneeFilterLabel()).toBe(await calendar.assigneeResetLabel());

    // Everything is back, including the event neither half of the previous
    // selection matched.
    await expect(calendar.findEventBlock(eventA)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(eventB)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(calendar.findEventBlock(eventTeam)).toBeVisible({ timeout: UI_TIMEOUT });
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
      await workersPage.clearTable();
      // The worker tag is installation-scoped, not property-scoped, so neither
      // clearTable() removes it: without this it survives into every later
      // spec in the shard. Deleted after the workers, so no live member is
      // still carrying it when it goes.
      await workersPage.deleteTag(teamName);

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

/**
 * Step forward to the week the seeded events live on, awaiting the grid reload
 * each step fires rather than sleeping through it.
 */
async function navigateToEventWeek(calendar: CalendarUiEnhancementsPage): Promise<void> {
  for (let i = 0; i < EVENT_WEEK_OFFSET; i++) {
    await calendar.navigateToNextWeek();
  }
}
