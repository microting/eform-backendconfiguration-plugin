import { test, expect, Page } from '@playwright/test';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import { generateRandmString } from '../../../helper-functions';
import {
  API_TIMEOUT,
  UI_TIMEOUT,
  ignoreUnhandledRejections,
  waitForApiResponse,
} from '../wait-helpers';

// New accounts have no password set, so every worker gets this password through
// setWorkerPasswordViaApi before it can log in.
// Generated at runtime rather than a literal (GitGuardian flags any committed
// password-shaped string, even a fabricated test one) - the fixed "Aa1"
// prefix guarantees lower/upper/digit regardless of generateRandmString's own
// charset, satisfying the server's Identity policy and the set-password
// modal's own rules (>= 8 chars, lower/upper/digit), same shape as
// r/property-worker-set-password-gate.spec.ts's NEW_PASSWORD.
const WORKER_PASSWORD = `Aa1${generateRandmString(12)}`;
const BASE_URL = 'http://localhost:4200';

// `timeout` is optional so pre-existing callers (setupSecurityGroupsViaApi's
// admin login) keep their prior, unbounded-by-this-function behaviour; pass it
// explicitly at any new call site per CLAUDE.md's "every wait carries an
// explicit timeout" rule.
async function loginViaApi(page: Page, email: string, password: string, timeout?: number): Promise<string> {
  const res = await page.request.post(`${BASE_URL}/api/auth/token`, {
    form: { username: email, password: password, grant_type: 'password' },
    timeout,
  });
  console.log(`loginViaApi ${email}: status=${res.status()}`);
  const json = await res.json();
  return json?.model?.accessToken || '';
}

/**
 * Sets a worker's password through api/account/change-password-admin, the
 * endpoint the property-worker "Set password" dialog calls. Driven through the
 * API like the rest of this spec's admin setup (loginViaApi,
 * setupSecurityGroupsViaApi), which skips opening a row menu + dialog for each
 * of the four workers. A direct `request.post` is its own bounded round trip
 * (explicit `timeout`), so there is no separate network event to await with
 * `waitForApiResponse`.
 *
 * Asserts 200 and `success: true` itself, so a failure names itself here
 * instead of surfacing 60+ seconds later as the worker's login silently
 * failing and loginAsWorker timing out on the datepicker.
 */
async function setWorkerPasswordViaApi(
  page: Page,
  adminToken: string,
  email: string,
  password: string
): Promise<void> {
  const res = await page.request.post(`${BASE_URL}/api/account/change-password-admin`, {
    headers: { 'Authorization': `Bearer ${adminToken}`, 'Content-Type': 'application/json' },
    data: { newPassword: password, confirmPassword: password, email },
    timeout: API_TIMEOUT,
  });
  const json = await res.json().catch(() => null);
  if (res.status() !== 200 || json?.success !== true) {
    throw new Error(
      `change-password-admin failed for ${email}: status=${res.status()} body=${JSON.stringify(json)}`
    );
  }
}

/**
 * Replicates deploy_and_configure.py: create security groups, set redirect links, set plugin permissions.
 */
async function setupSecurityGroupsViaApi(page: Page): Promise<void> {
  const token = await loginViaApi(page, 'admin@admin.com', 'secretpassword');
  const headers = { 'Authorization': `Bearer ${token}`, 'Content-Type': 'application/json' };

  await page.request.post(`${BASE_URL}/api/security/groups`, {
    headers, data: { userIds: [], name: 'Kun tid' }
  });
  await page.request.post(`${BASE_URL}/api/security/groups`, {
    headers, data: { userIds: [], name: 'Kun arkiv' }
  });

  const indexRes = await page.request.post(`${BASE_URL}/api/security/groups/index`, {
    headers, data: { sort: 'Id', nameFilter: '', pageIndex: 0, pageSize: 10000, isSortDsc: false, offset: 0 }
  });
  const indexJson = await indexRes.json();
  const groups = indexJson?.model?.entities || [];
  console.log(`Security groups: ${groups.map((g: any) => `${g.groupName}(id=${g.id})`).join(', ')}`);

  let kunTidId = 0;
  let kunArkivId = 0;
  for (const g of groups) {
    if (g.groupName === 'Kun tid') kunTidId = g.id;
    if (g.groupName === 'Kun arkiv') kunArkivId = g.id;
  }

  if (kunTidId > 0) {
    await page.request.put(`${BASE_URL}/api/security/groups/settings`, {
      headers, data: { id: kunTidId, redirectLink: '/plugins/time-planning-pn/planning' }
    });
  }
  if (kunArkivId > 0) {
    await page.request.put(`${BASE_URL}/api/security/groups/settings`, {
      headers, data: { id: kunArkivId, redirectLink: '/plugins/backend-configuration-pn/files' }
    });
  }

  const pluginsRes = await page.request.get(
    `${BASE_URL}/api/plugins-management/installed?sort=id&isSortDsc=true&pageSize=1000&pageIndex=0&offset=0`,
    { headers }
  );
  const plugins = (await pluginsRes.json())?.model?.pluginsList || [];

  for (const plugin of plugins) {
    if (plugin.pluginId === 'eform-angular-time-planning-plugin') {
      const permUrl = `${BASE_URL}/api/plugins-permissions/group-permissions/${plugin.id}`;
      const currentPerms = (await (await page.request.get(permUrl, { headers })).json())?.model || [];
      const permIdMap: Record<string, number> = {};
      for (const gp of currentPerms) {
        for (const perm of gp.permissions || []) {
          permIdMap[perm.claimName] = perm.permissionId;
        }
      }
      const timePlanningPerms = [
        { isEnabled: true, claimName: 'time_planning_plugin_access', permissionId: permIdMap['time_planning_plugin_access'] || 1, permissionName: 'Access Time Plannings Plugin' },
        { isEnabled: true, claimName: 'time_planning_flex_get', permissionId: permIdMap['time_planning_flex_get'] || 2, permissionName: 'Obtain flex' },
        { isEnabled: true, claimName: 'time_planning_working_hours_get', permissionId: permIdMap['time_planning_working_hours_get'] || 3, permissionName: 'Obtain working hours' },
      ];
      const payload: any[] = [{ permissions: timePlanningPerms, groupId: 1 }];
      if (kunTidId > 0) payload.push({ permissions: timePlanningPerms, groupId: kunTidId });
      await page.request.put(permUrl, { headers, data: payload });
    }

    if (plugin.pluginId === 'eform-backend-configuration-plugin') {
      const permUrl = `${BASE_URL}/api/plugins-permissions/group-permissions/${plugin.id}`;
      const currentPerms = (await (await page.request.get(permUrl, { headers })).json())?.model || [];
      const permIdMap: Record<string, number> = {};
      for (const gp of currentPerms) {
        for (const perm of gp.permissions || []) {
          permIdMap[perm.claimName] = perm.permissionId;
        }
      }
      const backendPerms = [
        { isEnabled: true, claimName: 'backend_configuration_plugin_access', permissionId: permIdMap['backend_configuration_plugin_access'] || 1, permissionName: 'Access BackendConfiguration Plugin' },
        { isEnabled: true, claimName: 'properties_get', permissionId: permIdMap['properties_get'] || 3, permissionName: 'Get properties' },
        { isEnabled: true, claimName: 'time_registration_enable', permissionId: permIdMap['time_registration_enable'] || 8, permissionName: 'Enable time registration' },
        { isEnabled: true, claimName: 'task_management_enable', permissionId: permIdMap['task_management_enable'] || 7, permissionName: 'Enable task management' },
        { isEnabled: true, claimName: 'document_management_enable', permissionId: permIdMap['document_management_enable'] || 6, permissionName: 'Enable document management' },
      ];
      const payload: any[] = [{ permissions: backendPerms, groupId: 1 }];
      if (kunTidId > 0) payload.push({ permissions: backendPerms, groupId: kunTidId });
      await page.request.put(permUrl, { headers, data: payload });
    }
  }
}

async function loginAs(page: Page, email: string, password: string): Promise<void> {
  const loginBtn = page.locator('#loginBtn');
  await loginBtn.waitFor({ state: 'visible', timeout: 60000 });
  await page.locator('#username').fill(email);
  await page.locator('#password').fill(password);
  const loginResponsePromise = page.waitForResponse(
    r => r.url().includes('/api/auth/token') || r.url().includes('/api/account/login'),
    { timeout: 30000 }
  ).catch(() => null);
  await loginBtn.click();
  await loginResponsePromise;
  // No settle sleep here on purpose: both callers below assert their own
  // post-login landmark (#newEFormBtn for admin, the planning datepicker for a
  // worker), which is the real "logged in and rendered" condition.
  console.log(`Login ${email}: URL=${page.url()}`);
}

async function loginAsAdmin(page: Page): Promise<void> {
  await loginAs(page, 'admin@admin.com', 'secretpassword');
  await page.locator('#newEFormBtn').waitFor({ state: 'visible', timeout: 120000 });
}

/**
 * Logs a worker in and waits until the planning page KNOWS how many sites that
 * worker may see.
 *
 * `#workingHoursSite` sits behind `*ngIf="availableSites.length > 1"`
 * (time-plannings-container.component.html), fed by an async
 * `GET /api/time-planning-pn/settings/sites`. Until that response lands,
 * `availableSites` is `[]` and the dropdown is absent — so the phases below
 * that assert `not.toBeVisible()` would pass against a page that had simply
 * not loaded yet. That is what the `waitForTimeout(2000)` this replaces was
 * really guarding, badly: a sleep cannot tell "hidden because the worker has
 * one site" from "hidden because the call has not come back".
 *
 * `expectedSiteCount` therefore asserts the gate's own input. It also gives a
 * regression here a name — "the server returned 2 sites for a worker" — rather
 * than an unexplained dropdown appearing in a DOM assertion.
 */
async function loginAsWorker(page: Page, email: string, expectedSiteCount: number): Promise<void> {
  // Registered before the login click: the request fires on the planning page's
  // init, i.e. after the post-login redirect.
  const availableSites = waitForApiResponse(
    page,
    `GET /api/time-planning-pn/settings/sites (sites visible to ${email})`,
    r =>
      r.url().includes('/api/time-planning-pn/settings/sites') &&
      r.request().method() === 'GET',
    API_TIMEOUT
  );
  ignoreUnhandledRejections(availableSites);

  await loginAs(page, email, WORKER_PASSWORD);
  await page.waitForURL('**/plugins/time-planning-pn/planning**', { timeout: 30000 }).catch(() => {
    console.log(`Worker ${email}: did not navigate to planning, URL=${page.url()}`);
  });
  console.log(`Worker ${email}: final URL=${page.url()}`);
  // Wait for the planning page to finish loading (date picker is always visible)
  await page.locator('mat-datepicker-toggle').first().waitFor({ state: 'visible', timeout: 120000 });

  const sitesBody = await (await availableSites).json().catch(() => null);
  console.log(
    `Worker ${email}: settings/sites returned ${JSON.stringify(sitesBody?.model?.map((s: any) => s.siteName))}`
  );
  expect(
    sitesBody?.model?.length,
    `${email}: GET settings/sites is what gates the worker dropdown`
  ).toBe(expectedSiteCount);
}

async function logout(page: Page): Promise<void> {
  await page.evaluate(() => {
    localStorage.removeItem('auth');
    localStorage.removeItem('token');
  });
  await page.goto(`${BASE_URL}/auth`);
  await page.locator('#loginBtn').waitFor({ state: 'visible', timeout: 60000 });
}

/**
 * Navigates to the device-user list and waits for the LIST, not just the page
 * chrome.
 *
 * `goToPropertyWorkers()` only clicks the menu item, and `clearTable()` counts
 * `.mat-mdc-row` the moment it is called — so clearing before the grid has
 * rendered counts 0 rows and silently deletes nothing. The page builds its rows
 * from a `forkJoin` of three calls (property-workers-page.component.ts
 * `updateTable`), of which two are observable here: the POST that fetches the
 * device users and the GET that fetches their property assignments. Both
 * landing is the real signal that the grid has its data; the pre-existing
 * `waitForTimeout(1000)` this replaces was a guess at the same thing.
 */
async function goToPropertyWorkersAndAwaitGrid(
  page: Page,
  workersPage: BackendConfigurationPropertyWorkersPage
): Promise<void> {
  const deviceUsers = waitForApiResponse(
    page,
    'POST /api/backend-configuration-pn/properties/assignment/index-device-user (device user list)',
    r =>
      r.url().includes('/api/backend-configuration-pn/properties/assignment/index-device-user') &&
      r.request().method() === 'POST',
    API_TIMEOUT
  );
  const assignments = waitForApiResponse(
    page,
    'GET /api/backend-configuration-pn/properties/assignment (worker property assignments)',
    r =>
      r.url().includes('/api/backend-configuration-pn/properties/assignment') &&
      r.request().method() === 'GET',
    API_TIMEOUT
  );
  // Either wait can reject before the `await` below reaches it.
  ignoreUnhandledRejections(deviceUsers, assignments);

  await workersPage.goToPropertyWorkers();
  await Promise.all([deviceUsers, assignments]);
  await workersPage.newDeviceUserBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
}

async function navigateToPlannings(page: Page): Promise<void> {
  const planningBtn = page.locator('#time-planning-pn-planning');
  if (!await planningBtn.isVisible()) {
    await page.locator('#time-planning-pn').click();
    await planningBtn.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  }
  await planningBtn.click();
  // The caller's own `#workingHoursSite` wait is the landing condition.
}

async function getAvailableSiteNames(page: Page): Promise<string[]> {
  const siteSelector = page.locator('#workingHoursSite');
  await siteSelector.waitFor({ state: 'visible', timeout: 30000 });
  await siteSelector.click();
  const dropdownPanel = page.locator('ng-dropdown-panel');
  await dropdownPanel.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  // The panel element appears before ng-select has rendered its options, so
  // reading `allInnerTexts()` on a bare panel can return []. Every caller here
  // expects at least one site, so the first option being present is the real
  // "options rendered" condition.
  await dropdownPanel.locator('.ng-option').first().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  const names = await dropdownPanel.locator('.ng-option').allInnerTexts();
  await page.keyboard.press('Escape');
  await dropdownPanel.waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
  return names.map(n => n.trim());
}

test.describe('Time Registration Dashboard Visibility', () => {
  // Hoisted out of the test body so `afterAll` can name the same entities it
  // has to delete. Everything is suffixed with `rand`, so a leaked row from an
  // earlier run can never be mistaken for one of ours.
  const rand = generateRandmString(8);
  const tagName = `TeamAlpha-${rand}`;
  const propertyName = `TestProp-${rand}`;

  const managerEmail = `manager-${rand}@test.com`;
  const taggedWorkerEmail = `tagged-${rand}@test.com`;
  const untaggedWorkerEmail = `untagged-${rand}@test.com`;
  const notagMgrEmail = `notagmgr-${rand}@test.com`;

  const managerName = `MgrFirst-${rand}`;
  const managerSurname = `MgrLast-${rand}`;
  const taggedName = `TaggedFirst-${rand}`;
  const taggedSurname = `TaggedLast-${rand}`;
  const untaggedName = `UntaggedFirst-${rand}`;
  const untaggedSurname = `UntaggedLast-${rand}`;
  const notagMgrName = `NotagMgrFirst-${rand}`;
  const notagMgrSurname = `NotagMgrLast-${rand}`;

  /**
   * Teardown (#1146). This used to be the test body's own phase 7, which made
   * it unreachable the moment any assertion above it threw — a failed run left
   * four device users, a property and a tag behind — and made it compete for
   * the test's timeout, so it was the part that got squeezed when the earlier
   * phases ran slow.
   *
   * Same shape as the task-list suites (b/task-list-inline-rename.spec.ts):
   * one wall-clock budget across all phases, every failure collected and
   * logged rather than thrown, so cleanup can never itself fail the run.
   */
  test.afterAll(async ({ browser }) => {
    // Playwright gives a hook the config-level 120s timeout, which is below the
    // budget this teardown needs (four device-user deletes, each an SDK-backed
    // round trip bounded at API_TIMEOUT, plus an admin login). Raised so our
    // own guard is what stops the work, not the harness killing the hook
    // mid-delete.
    const CLEANUP_BUDGET_MS = 150000;
    test.setTimeout(CLEANUP_BUDGET_MS + 30000);

    const deadline = Date.now() + CLEANUP_BUDGET_MS;
    const problems: string[] = [];
    let aborted = false;

    // Never throws. Returns whether the phase actually completed.
    const phase = async (label: string, fn: () => Promise<void>): Promise<boolean> => {
      if (aborted) {
        problems.push(`${label}: skipped, an earlier phase did not complete`);
        return false;
      }
      const budget = deadline - Date.now();
      if (budget <= 0) {
        problems.push(`${label}: skipped, cleanup budget exhausted`);
        aborted = true;
        return false;
      }
      let timer: ReturnType<typeof setTimeout> | undefined;
      try {
        const outcome = await Promise.race([
          fn().then(() => 'done' as const),
          new Promise<'timeout'>(resolve => {
            timer = setTimeout(() => resolve('timeout'), budget);
          }),
        ]);
        if (outcome === 'timeout') {
          aborted = true;
          problems.push(`${label}: timed out after ${budget}ms`);
          return false;
        }
        return true;
      } catch (err: any) {
        problems.push(`${label}: ${err?.message ?? err}`);
        return false;
      } finally {
        if (timer) {
          clearTimeout(timer);
        }
      }
    };

    // browser.newPage() can itself reject — a browser that crashed or got
    // disconnected during a long run — and an exception thrown here escapes the
    // hook and fails the job, which is exactly what this guarded teardown exists
    // to prevent. Record it and fall through to the reporting in `finally`.
    const page = await browser.newPage().catch((err: any) => {
      problems.push(`cleanup harness: browser.newPage() failed: ${err?.message ?? err}`);
      return undefined;
    });
    try {
      if (!page) {
        // Nothing to drive the cleanup with; `finally` still reports what the
        // next spec in this shard inherits.
        return;
      }
      const workersPage = new BackendConfigurationPropertyWorkersPage(page);
      const propertiesPage = new BackendConfigurationPropertiesPage(page);

      const loggedIn = await phase('login', async () => {
        await page.goto(BASE_URL);
        await loginAsAdmin(page);
      });
      if (!loggedIn) {
        // Nothing below can work unauthenticated; stop rather than spend the
        // remaining budget on waits that are certain to time out.
        aborted = true;
      }

      // Workers first: the property cannot go while a worker is assigned to it.
      await phase('clear workers', async () => {
        await goToPropertyWorkersAndAwaitGrid(page, workersPage);
        await workersPage.clearTable();
      });

      // The tags dialog is opened from the device-user page we are already on,
      // and the tag is only free to delete once no worker carries it.
      await phase('delete tag', async () => {
        await workersPage.deleteTag(tagName);
      });

      await phase('clear properties', async () => {
        // goToProperties() and clearTable() both wait for the properties table
        // themselves, so there is nothing left to pad here.
        await propertiesPage.goToProperties();
        await propertiesPage.clearTable();
      });

      await phase('verify', async () => {
        await propertiesPage.goToProperties();
        await page.locator('app-properties-table').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
        const propertiesLeft = await propertiesPage.rowNum();
        if (propertiesLeft > 0) {
          problems.push(`verify: ${propertiesLeft} property row(s) still present`);
        }
        await goToPropertyWorkersAndAwaitGrid(page, workersPage);
        const workersLeft = await workersPage.rowNum();
        if (workersLeft > 0) {
          problems.push(`verify: ${workersLeft} worker row(s) still present`);
        }
      });
    } catch (err: any) {
      problems.push(`cleanup harness: ${err?.message ?? err}`);
    } finally {
      if (problems.length > 0) {
        console.log(
          '[time-registration-dashboard-visibility] afterAll cleanup INCOMPLETE (non-fatal) — ' +
          `may have left property "${propertyName}", tag "${tagName}" and up to four device ` +
          `users (${managerEmail}, ${taggedWorkerEmail}, ${untaggedWorkerEmail}, ` +
          `${notagMgrEmail}) for the next spec in this shard: ` +
          problems.join(' | '),
        );
      }
      if (page) {
        try { await page.close(); } catch {}
      }
    }
  });

  test('should show correct workers based on user role and tags', async ({ page }) => {
    // 7 min: the heaviest spec in the suite — one tag, one property, four device
    // users (each a ~60s SDK provisioning call at worst) and six login/logout
    // phases. Kept generous because the work is genuinely long, not to cover for
    // an unbounded wait. Cleanup is NOT in here: it has its own budget in
    // afterAll (#1146). For scale, the whole k shard (this spec plus two others)
    // ran in 86s on green trunk run 33406521590.
    test.setTimeout(420000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    // ==================== PHASE 1: SETUP (as admin) ====================

    await page.goto(BASE_URL);
    await loginAsAdmin(page);
    await setupSecurityGroupsViaApi(page);

    // Used by setWorkerPasswordViaApi below - see the comment on WORKER_PASSWORD.
    const adminToken = await loginViaApi(page, 'admin@admin.com', 'secretpassword', API_TIMEOUT);
    if (!adminToken) {
      throw new Error(
        'loginViaApi returned an empty admin token - cannot set worker passwords without one, ' +
          'and every loginAsWorker call below would otherwise fail silently and surface 120s later ' +
          'as an unrelated-looking datepicker timeout.'
      );
    }

    // Create a property. createProperty() already waits for the list refresh
    // and for the dialog to close, so it needs no padding after it.
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty({
      name: propertyName, cvrNumber: '1111111',
      chrNumber: rand.substring(0, 6), address: 'Test Address 1',
    });

    // Navigate to property workers and create tag. createTag() waits for the
    // saved row inside the dialog and for the dialog to close.
    await goToPropertyWorkersAndAwaitGrid(page, workersPage);
    await workersPage.createTag(tagName);

    // Worker A: Manager with tag and managing tag.
    // create() waits for the create-device-user PUT, the assignment POST and
    // the list refresh, then for the dialog to close — nothing to pad.
    await workersPage.create({
      name: managerName, surname: managerSurname, workerEmail: managerEmail,
      language: 'Dansk', properties: [propertyName],
      timeRegistrationEnabled: true, enableMobileAccess: true,
      isManager: true, managingTags: [tagName], tags: [tagName],
    });
    await setWorkerPasswordViaApi(page, adminToken, managerEmail, WORKER_PASSWORD);

    // Worker B: Tagged worker (same tag as manager)
    await workersPage.create({
      name: taggedName, surname: taggedSurname, workerEmail: taggedWorkerEmail,
      language: 'Dansk', properties: [propertyName],
      timeRegistrationEnabled: true, enableMobileAccess: true, tags: [tagName],
    });
    await setWorkerPasswordViaApi(page, adminToken, taggedWorkerEmail, WORKER_PASSWORD);

    // Worker C: Untagged worker
    await workersPage.create({
      name: untaggedName, surname: untaggedSurname, workerEmail: untaggedWorkerEmail,
      language: 'Dansk', properties: [propertyName],
      timeRegistrationEnabled: true, enableMobileAccess: true,
    });
    await setWorkerPasswordViaApi(page, adminToken, untaggedWorkerEmail, WORKER_PASSWORD);

    // Worker D: Manager without managing tags
    await workersPage.create({
      name: notagMgrName, surname: notagMgrSurname, workerEmail: notagMgrEmail,
      language: 'Dansk', properties: [propertyName],
      timeRegistrationEnabled: true, enableMobileAccess: true, isManager: true,
    });
    await setWorkerPasswordViaApi(page, adminToken, notagMgrEmail, WORKER_PASSWORD);

    // ==================== PHASE 2: ADMIN sees all workers ====================

    await navigateToPlannings(page);
    await page.locator('#workingHoursSite').waitFor({ state: 'visible', timeout: 30000 });

    const adminSiteNames = await getAvailableSiteNames(page);
    console.log(`Admin sees ${adminSiteNames.length} sites: ${JSON.stringify(adminSiteNames)}`);

    expect(adminSiteNames.length).toBeGreaterThanOrEqual(4);
    expect(adminSiteNames.some(n => n.includes(managerName) || n.includes(managerSurname))).toBe(true);
    expect(adminSiteNames.some(n => n.includes(taggedName) || n.includes(taggedSurname))).toBe(true);
    expect(adminSiteNames.some(n => n.includes(untaggedName) || n.includes(untaggedSurname))).toBe(true);
    expect(adminSiteNames.some(n => n.includes(notagMgrName) || n.includes(notagMgrSurname))).toBe(true);

    // ==================== PHASE 3: MANAGER WITH TAGS sees self + tagged workers ====================

    await logout(page);
    await loginAsWorker(page, managerEmail, 2);
    expect(page.url()).toContain('/plugins/time-planning-pn/planning');

    // Manager with tags should see the dropdown (more than 1 site)
    const siteDropdown = page.locator('#workingHoursSite');
    await siteDropdown.waitFor({ state: 'visible', timeout: 30000 });

    const managerSiteNames = await getAvailableSiteNames(page);
    console.log(`Manager sees ${managerSiteNames.length} sites: ${JSON.stringify(managerSiteNames)}`);
    expect(managerSiteNames.length).toBe(2);
    expect(managerSiteNames.some(n => n.includes(managerName) || n.includes(managerSurname))).toBe(true);
    expect(managerSiteNames.some(n => n.includes(taggedName) || n.includes(taggedSurname))).toBe(true);
    // Manager should NOT see untagged workers
    expect(managerSiteNames.some(n => n.includes(untaggedName))).toBe(false);
    expect(managerSiteNames.some(n => n.includes(notagMgrName))).toBe(false);

    // ==================== PHASE 4: TAGGED WORKER sees only self, no dropdown ====================

    await logout(page);
    await loginAsWorker(page, taggedWorkerEmail, 1);
    expect(page.url()).toContain('/plugins/time-planning-pn/planning');

    // Non-manager should NOT see the dropdown (only 1 site returned, dropdown hidden)
    await expect(page.locator('#workingHoursSite')).not.toBeVisible();
    console.log('Tagged worker: dropdown correctly hidden (single site)');

    // ==================== PHASE 5: UNTAGGED WORKER sees only self, no dropdown ====================

    await logout(page);
    await loginAsWorker(page, untaggedWorkerEmail, 1);
    expect(page.url()).toContain('/plugins/time-planning-pn/planning');

    await expect(page.locator('#workingHoursSite')).not.toBeVisible();
    console.log('Untagged worker: dropdown correctly hidden (single site)');

    // ==================== PHASE 6: MANAGER WITHOUT TAGS sees only self, no dropdown ====================

    await logout(page);
    await loginAsWorker(page, notagMgrEmail, 1);
    expect(page.url()).toContain('/plugins/time-planning-pn/planning');

    await expect(page.locator('#workingHoursSite')).not.toBeVisible();
    console.log('Manager without tags: dropdown correctly hidden (single site)');

    // Teardown is afterAll's job (#1146) — deliberately not done here.
  });
});
