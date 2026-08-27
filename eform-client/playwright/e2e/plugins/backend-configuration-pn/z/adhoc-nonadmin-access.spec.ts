import { test, expect, Page } from '@playwright/test';
import * as path from 'path';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationPropertiesPage } from '../BackendConfigurationProperties.page';
import { BackendConfigurationAdhocPage } from '../BackendConfigurationAdhoc.page';

/**
 * Adhoc overblik — NON-ADMIN access suite (spec
 * `2026-08-24-adhoc-tasks-unrestricted-access-design.md`, §4.2).
 *
 * End-to-end proof for the permission change: the `adhoc-tasks` route no
 * longer carries `PermissionGuard` + `data.requiredPermission:
 * 'adhoc_enable'` (it is `canActivate: [AuthGuard]` now, like `calendar`/
 * `compliances`/`property-workers`), and `AdhocController` passes the
 * constant `DashboardHasFullAccess = true` instead of
 * `IUserService.IsAdmin()` at every call site — so a plain `user`-role web
 * caller gets exactly the ad-hoc behaviour an admin had.
 *
 * The three non-admin tests below assert the two halves separately,
 * because only the second one distinguishes a real fix from merely
 * unblocking navigation:
 *
 *   1. the list view MOUNTS (the route guard is gone);
 *   2. the seeded task IS VISIBLE even though this user is not assigned to
 *      its property (the server-side identity gate is gone — before the
 *      change the page would have rendered permanently empty, since the
 *      shared service's `CanSee` resolves property access through
 *      `PropertyWorkers.WorkerId == sdkSiteId` and the web pseudo-identity
 *      is worker `0`, which owns no such row);
 *   3. opening that task's photo does not end the session — `GET
 *      .../adhoc/photos/{id}` was the plugin's only `Forbid()`, and the
 *      global `HttpErrorInterceptor` turns any 403 into a token refresh
 *      and, on failure, `authStateService.logout()` (→ `/auth`).
 *
 * `loginViaApi`/`loginAs`/`setupNonAdminUser` are copied VERBATIM from
 * `r/property-workers-nonadmin-no-logout.spec.ts` (`loginViaApi` from line
 * 29, `setupNonAdminUser` lines 70-172), as `q/calendar-admin-gating.spec.ts`
 * and `x/task-list-admin-gating.spec.ts` already do — only the group/user
 * name strings are suite-local. Its plugin-claim list deliberately OMITS
 * `adhoc_enable`: that omission is the whole point of the fixture, so do
 * NOT add the claim. The helper creates a security group, core claims, a
 * redirect link, plugin access and a `user`-role account and NOTHING else —
 * in particular it performs no property assignment, and the account is
 * created with `isDeviceUser: false`, so this user has no SDK worker/site
 * and therefore no `PropertyWorkers` row for the seeded property. That is
 * exactly the condition assertion 2 needs.
 *
 * Structural notes:
 *   - The copied helper sets the group's `redirectLink` to
 *     `/plugins/backend-configuration-pn/property-workers`, so the
 *     non-admin session lands THERE first and `goToAdhocAsNonAdmin()`
 *     carries it to ad-hoc — preferring the SIDEBAR
 *     (`#backend-configuration-pn` → `#backend-configuration-pn-adhoc`,
 *     declared with `Permissions = []`), but BOUNDED, with a direct-URL
 *     fallback, because the sidebar's presence for this claims set is a
 *     server-side/DB question this spec cannot control. See that helper's
 *     doc comment.
 *   - Every positive "the app works" assertion is taken BEFORE any guarded
 *     `page.goto`: after a guard-cancelled INITIAL navigation no route
 *     activates at all, so even the claim-independent footer
 *     (`#sign-out-dropdown`, hosted by `FullLayoutComponent` on the `''`
 *     parent route) is absent — see the same reasoning in
 *     `x/task-list-admin-gating.spec.ts`.
 *   - Shard `z` does NOT load the shard-a DB dump, so this file is
 *     `describe.serial` with `seed:` tests first, like every other `z`
 *     spec.
 */
const BASE_URL = 'http://localhost:4200';
const USER_PASSWORD = 'Secret_password_2026!';
// Every sidebar wait/click in `goToAdhocAsNonAdmin` is bounded by this.
// `playwright.config.ts` declares no `actionTimeout`, so an unbounded click
// would inherit `test.setTimeout(300000)` and hang the shard for 15 minutes.
const SIDEBAR_TIMEOUT = 30000;
const rand = generateRandmString(8).toLowerCase();

// Re-uses the existing image fixture (the drawer's file input is
// `accept="image/*"`); there is no adhoc-specific fixture tree, and
// `p/calendar-copy.spec.ts` / `w/calendar-attachments*.spec.ts` already
// resolve this same directory from other shards.
const PHOTO_FIXTURE = path.resolve(__dirname, '../fixtures/calendar-attachments/sample.png');

const property = {
  name: `adhoc-na-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '4444444',
};
const taskTitle = `Adhoc-NonAdmin-Task-${rand}`;

async function loginViaApi(page: Page, email: string, password: string): Promise<string> {
  const res = await page.request.post(`${BASE_URL}/api/auth/token`, {
    form: { username: email, password: password, grant_type: 'password' },
  });
  const json = await res.json();
  return json?.model?.accessToken || '';
}

async function loginAs(page: Page, email: string, password: string): Promise<void> {
  const loginBtn = page.locator('#loginBtn');
  await loginBtn.waitFor({ state: 'visible', timeout: 60000 });
  await page.locator('#username').fill(email);
  await page.locator('#password').fill(password);
  const loginResponsePromise = page.waitForResponse(
    (r) => r.url().includes('/api/auth/token'),
    { timeout: 30000 },
  ).catch(() => null);
  await loginBtn.click();
  await loginResponsePromise;
  // Wait for the post-login redirect to leave /auth (group redirectLink).
  await page.waitForURL((url) => !url.pathname.startsWith('/auth'), { timeout: 20000 })
    .catch(() => console.log(`loginAs ${email}: still on ${page.url()}`));
  await page.waitForTimeout(1000);
}

/**
 * Creates a security group carrying exactly the reported customer setup:
 * core device-users + tags permissions ("backend") and plugin access to
 * backend-configuration + time-planning ("timeregistration"), then a
 * non-admin web user in that group. All via admin API calls.
 * Returns the created user's email.
 *
 * NOTE (this suite): the plugin-claim list below intentionally does NOT
 * contain `adhoc_enable` — the route is open to every plugin user now, and
 * granting the claim would make the suite pass even if the guard came back.
 */
async function setupNonAdminUser(page: Page, rand: string): Promise<string> {
  const token = await loginViaApi(page, 'admin@admin.com', 'secretpassword');
  const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
  const groupName = `adhoc-nonadmin-${rand}`;
  const userEmail = `adhocuser-${rand}@test.com`;

  // 1. Security group
  await page.request.post(`${BASE_URL}/api/security/groups`, {
    headers, data: { userIds: [], name: groupName },
  });
  const indexRes = await page.request.post(`${BASE_URL}/api/security/groups/index`, {
    headers,
    data: { sort: 'Id', nameFilter: groupName, pageIndex: 0, pageSize: 100, isSortDsc: false, offset: 0 },
  });
  const groups = (await indexRes.json())?.model?.entities || [];
  const groupId = groups.find((g: any) => g.groupName === groupName)?.id || 0;
  expect(groupId).toBeGreaterThan(0);

  // 2. Core permissions: device users (page access + create/edit) and eForm
  //    tags (create/edit/assign tags) — the affected customer group's set.
  const coreClaims = [
    'device_users_read', 'device_users_create', 'device_users_update',
    'eforms_read_tags', 'eforms_update_tags',
    'eforms_read', 'cases_read', 'case_read',
  ];
  const permsRes = await page.request.get(`${BASE_URL}/api/security/permissions/${groupId}`, { headers });
  const permTypes = (await permsRes.json())?.model?.permissionTypes || [];
  const permissions: any[] = [];
  for (const pt of permTypes) {
    for (const p of pt.permissions || []) {
      permissions.push({ ...p, isEnabled: p.isEnabled || coreClaims.includes(p.claimName) });
    }
  }
  await page.request.put(`${BASE_URL}/api/security/permissions`, {
    headers, data: { groupId, permissions },
  });

  // Land non-admin logins directly on the property-workers page. Ad-hoc is
  // reached from there via the sidebar (see the suite header) — this
  // redirect is deliberately NOT pointed at adhoc-tasks, so the sidebar
  // navigation, not the landing page, is what proves the route is open.
  await page.request.put(`${BASE_URL}/api/security/groups/settings`, {
    headers,
    data: { id: groupId, redirectLink: '/plugins/backend-configuration-pn/property-workers' },
  });

  // 3. Plugin permissions: backend-configuration + time-planning access.
  //    `adhoc_enable` is omitted ON PURPOSE (see this function's doc).
  const pluginsRes = await page.request.get(
    `${BASE_URL}/api/plugins-management/installed?sort=id&isSortDsc=true&pageSize=1000&pageIndex=0&offset=0`,
    { headers },
  );
  const plugins = (await pluginsRes.json())?.model?.pluginsList || [];
  const wantedPluginClaims: Record<string, string[]> = {
    'eform-backend-configuration-plugin': [
      'backend_configuration_plugin_access', 'properties_get',
      'time_registration_enable', 'task_management_enable', 'document_management_enable',
    ],
    'eform-angular-time-planning-plugin': [
      'time_planning_plugin_access', 'time_planning_flex_get', 'time_planning_working_hours_get',
    ],
  };
  for (const plugin of plugins) {
    const wanted = wantedPluginClaims[plugin.pluginId];
    if (!wanted) continue;
    const permUrl = `${BASE_URL}/api/plugins-permissions/group-permissions/${plugin.id}`;
    const currentPerms = (await (await page.request.get(permUrl, { headers })).json())?.model || [];
    const permIdMap: Record<string, number> = {};
    for (const gp of currentPerms) {
      for (const perm of gp.permissions || []) {
        permIdMap[perm.claimName] = perm.permissionId;
      }
    }
    const pluginPerms = wanted.map((claimName, i) => ({
      isEnabled: true,
      claimName,
      permissionId: permIdMap[claimName] || i + 1,
      permissionName: claimName,
    }));
    await page.request.put(permUrl, { headers, data: [{ permissions: pluginPerms, groupId }] });
  }

  // 4. Non-admin user in that group.
  const createUserRes = await page.request.post(`${BASE_URL}/api/admin/create-user`, {
    headers,
    data: {
      id: 0,
      firstName: `AdhocUser${rand}`,
      lastName: 'NonAdmin',
      userName: userEmail,
      email: userEmail,
      password: USER_PASSWORD,
      passwordConfimation: USER_PASSWORD,
      role: 'user',
      groupId,
      isDeviceUser: false,
    },
  });
  const createUserJson = await createUserRes.json().catch(() => null);
  console.log(`create-user ${userEmail}: status=${createUserRes.status()} success=${createUserJson?.success}`);
  expect(createUserJson?.success).toBe(true);

  return userEmail;
}

/**
 * Navigates an ALREADY-LOGGED-IN non-admin session to "Adhoc overblik".
 *
 * Preferred path is the SIDEBAR (spec 4.2): an in-app navigation, so the SPA
 * never re-bootstraps with the ad-hoc URL as its INITIAL navigation. But
 * whether the plugin sidebar renders for THIS claims set is not settled:
 * every backend-configuration menu item is seeded with `Permissions = []`
 * (`EformBackendConfigurationPlugin.cs` `GetNavigationMenu`) and the
 * front-end only hides a leaf whose guard list is non-empty
 * (`navigation.component.html` `checkGuards`), yet `MenuService`
 * `GetCurrentUserMenu` ALSO filters server-side for non-admins
 * (`FilterMenuForUser`), where a plugin item survives only when it has no
 * `MenuItemSecurityGroups` rows or one of them is this user's group - a DB
 * state this spec does not control. `r/property-workers-nonadmin-no-logout.
 * spec.ts:233-235` records the sidebar as ABSENT for exactly this fixture.
 *
 * `playwright.config.ts` sets no `actionTimeout`, so an unbounded `.click()`
 * on an entry that never appears would burn the whole
 * `test.setTimeout(300000)` - 15 minutes of shard-`z` hang per test. Hence:
 * bounded waits, then a direct `page.goto` fallback. The fallback is safe
 * because `adhoc-tasks` is `canActivate: [AuthGuard]` now - the same reason
 * `q/calendar-admin-gating.spec.ts` navigates its non-admin to `/calendar`
 * with a plain `page.goto`.
 *
 * Either way the load-bearing proof is unchanged: the ROUTE must ACTIVATE.
 * If `PermissionGuard` + `adhoc_enable` came back, the cancelled navigation
 * would leave a blank shell and the `#main-list-view` wait below would fail.
 *
 * CALLERS MUST have taken their positive app-shell assertion
 * (`#sign-out-dropdown`) BEFORE calling this - see the suite header.
 */
async function goToAdhocAsNonAdmin(
  page: Page,
  adhocPage: BackendConfigurationAdhocPage,
): Promise<void> {
  const pluginEntry = adhocPage.backendConfigurationPnButton();
  const adhocEntry = adhocPage.backendConfigurationPnAdhocButton();
  try {
    if (!(await adhocEntry.isVisible())) {
      await pluginEntry.waitFor({ state: 'visible', timeout: SIDEBAR_TIMEOUT });
      await pluginEntry.click({ timeout: SIDEBAR_TIMEOUT });
    }
    await adhocEntry.waitFor({ state: 'visible', timeout: SIDEBAR_TIMEOUT });
    await adhocEntry.click({ timeout: SIDEBAR_TIMEOUT });
  } catch (error) {
    // Sidebar unusable for this claims set - say so loudly, then take the
    // URL. This must NOT swallow a guard regression: the wait below is what
    // proves the route opened.
    console.log(
      `sidebar ad-hoc entry not usable for this non-admin (${(error as Error).message.split('\n')[0]}) - falling back to direct URL`,
    );
    await page.goto(`${BASE_URL}/plugins/backend-configuration-pn/adhoc-tasks`);
  }
  await adhocPage.mainListView().waitFor({ state: 'visible', timeout: 30000 });
}

test.describe.serial('Adhoc overblik — non-admin unrestricted access', () => {
  let userEmail = '';

  // =======================================================================
  // Seed 1 (ADMIN) — property + one ad-hoc task on it + one photo.
  // =======================================================================
  test('seed: property, one ad-hoc task and a photo on it (as admin)', async ({ page }) => {
    test.setTimeout(300000);
    await page.goto(BASE_URL);
    await loginAs(page, 'admin@admin.com', 'secretpassword');
    await page.locator('#newEFormBtn').waitFor({ state: 'visible', timeout: 120000 });

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();

    await adhocPage.openNewTask();
    await adhocPage.selectDrawerProperty(property.name);
    await adhocPage.drawerTitleInput().fill(taskTitle);
    await adhocPage.saveDrawer(true);
    await expect(adhocPage.row(taskTitle)).toBeVisible({ timeout: 15000 });

    // The photo is attached in EDIT mode rather than queued during create:
    // edit-mode selection uploads immediately
    // (`adhoc-task-drawer.component.ts` `onFilesSelected`), so the POST can
    // be awaited and asserted here instead of racing the post-create
    // queued-upload chain.
    await adhocPage.openRowMenu(taskTitle);
    await adhocPage.editMenuItem().click();
    await adhocPage.drawerRoot().waitFor({ state: 'visible', timeout: 15000 });

    const uploadResponsePromise = page.waitForResponse(
      (r) => /\/api\/backend-configuration-pn\/adhoc\/\d+\/photos$/.test(r.url())
        && r.request().method() === 'POST',
      { timeout: 60000 },
    );
    await adhocPage.photoUploadInput().setInputFiles(PHOTO_FIXTURE);
    const uploadResponse = await uploadResponsePromise;
    expect(uploadResponse.status()).toBe(200);

    await expect(adhocPage.photoThumbs()).toHaveCount(1, { timeout: 20000 });
    await adhocPage.saveDrawer();
    await expect(adhocPage.row(taskTitle)).toBeVisible({ timeout: 15000 });
  });

  // =======================================================================
  // Seed 2 (ADMIN) — the `user`-role account WITHOUT `adhoc_enable` and
  // without any assignment to the property seeded above.
  // =======================================================================
  test('seed: create non-admin user without adhoc_enable (as admin)', async ({ page }) => {
    test.setTimeout(300000);
    await page.goto(BASE_URL);
    await loginAs(page, 'admin@admin.com', 'secretpassword');
    await page.locator('#newEFormBtn').waitFor({ state: 'visible', timeout: 120000 });

    userEmail = await setupNonAdminUser(page, rand);
    expect(userEmail).not.toBe('');
  });

  // =======================================================================
  // 1/3 — the route is open: the list view mounts for a non-admin.
  // =======================================================================
  test('non-admin reaches ad-hoc tasks and the list view mounts', async ({ page }) => {
    test.setTimeout(300000);
    expect(userEmail).not.toBe('');

    await page.goto(BASE_URL);
    await loginAs(page, userEmail, USER_PASSWORD);
    expect(page.url()).not.toContain('/auth');

    // Positive app-shell assertion BEFORE any guarded navigation (see the
    // suite header): on the group's `redirectLink` landing page the full
    // layout is mounted, so the claim-independent footer must exist.
    await expect(page.locator('#sign-out-dropdown')).toBeVisible();

    // Sidebar-first navigation with a bounded fallback — see
    // `goToAdhocAsNonAdmin`. Never an unbounded click.
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await goToAdhocAsNonAdmin(page, adhocPage);

    expect(page.url()).toContain('/plugins/backend-configuration-pn/adhoc-tasks');
    await expect(adhocPage.mainListView()).toBeVisible();
    await expect(adhocPage.grid()).toBeVisible({ timeout: 20000 });
    await expect(adhocPage.newTaskBtn()).toBeVisible();
  });

  // =======================================================================
  // 2/3 — the load-bearing assertion: the seeded task is VISIBLE to a user
  // with no assignment to its property. Merely unblocking the route would
  // leave this list empty.
  // =======================================================================
  test('non-admin sees the seeded task on a property it is not assigned to', async ({ page }) => {
    test.setTimeout(300000);
    expect(userEmail).not.toBe('');

    await page.goto(BASE_URL);
    await loginAs(page, userEmail, USER_PASSWORD);
    expect(page.url()).not.toContain('/auth');
    await expect(page.locator('#sign-out-dropdown')).toBeVisible();

    const adhocPage = new BackendConfigurationAdhocPage(page);
    await goToAdhocAsNonAdmin(page, adhocPage);

    await adhocPage.search(taskTitle);
    await expect(adhocPage.row(taskTitle)).toBeVisible({ timeout: 20000 });
    // ...and it really is the admin-seeded task on the unassigned property.
    await expect(adhocPage.columnCell(taskTitle, 'propertyName')).toContainText(property.name);

    // The property itself is offered in the toolbar filter too
    // (`GET /adhoc/properties` returns every property for the customer —
    // §2 consequence 4), which is the same widening seen from the list.
    await adhocPage.selectPropertyFilter(property.name);
    await expect(adhocPage.row(taskTitle)).toBeVisible({ timeout: 20000 });
  });

  // =======================================================================
  // 3/3 — the photo endpoint was the plugin's only `Forbid()`; a 403 there
  // used to be escalated into a logout by the global HttpErrorInterceptor.
  // =======================================================================
  test('non-admin opens the task photo and stays logged in', async ({ page }) => {
    test.setTimeout(300000);
    expect(userEmail).not.toBe('');

    // Collect EVERY photo-endpoint response for the whole test: the
    // thumbnail's `authImage` pipe fetches the same guarded URL when the
    // drawer opens, before the click below, so a 403 there would end the
    // session before the explicit open ever happened.
    const photoStatuses: number[] = [];
    page.on('response', (r) => {
      if (r.url().includes('/api/backend-configuration-pn/adhoc/photos/')) {
        photoStatuses.push(r.status());
      }
    });

    await page.goto(BASE_URL);
    await loginAs(page, userEmail, USER_PASSWORD);
    expect(page.url()).not.toContain('/auth');
    await expect(page.locator('#sign-out-dropdown')).toBeVisible();

    const adhocPage = new BackendConfigurationAdhocPage(page);
    await goToAdhocAsNonAdmin(page, adhocPage);
    await adhocPage.search(taskTitle);
    await expect(adhocPage.row(taskTitle)).toBeVisible({ timeout: 20000 });

    // Open the task read-only (row menu → "Vis"), then click the thumbnail,
    // which fetches every photo blob through `GET .../adhoc/photos/{id}`.
    await adhocPage.openRowMenu(taskTitle);
    await adhocPage.viewMenuItem().click();
    await adhocPage.drawerRoot().waitFor({ state: 'visible', timeout: 20000 });
    await expect(adhocPage.photoThumbs()).toHaveCount(1, { timeout: 20000 });

    const photoResponsePromise = page.waitForResponse(
      (r) => r.url().includes('/api/backend-configuration-pn/adhoc/photos/')
        && r.request().method() === 'GET',
      { timeout: 60000 },
    );
    await adhocPage.photoThumbs().first().click();
    const photoResponse = await photoResponsePromise;

    // Assert NOT-403 rather than ==200, deliberately.
    //
    // What this test exists to prove (spec §4.2 step 5) is that opening a photo
    // does not end the session: `GET .../adhoc/photos/{id}` is the plugin's only
    // `Forbid()`, and the global HttpErrorInterceptor escalates any 403 into
    // `logout()`. Only 401/403 do that; every other status is inert for session
    // survival.
    //
    // ==200 additionally required the blob to come back, which CI cannot do:
    // `AdhocPhotoStorage.GetAsync` reads through `core.GetFileFromS3Storage`
    // and the workflow starts no object-storage service, so retrieval answers
    // 500 there. That is NOT caused by this change — `(workerId 0, isAdmin
    // true)` is exactly the path admins already took, so an admin gets the same
    // 500; no spec had ever fetched a photo, so nothing surfaced it before.
    // Asserting ==200 here would pin CI infrastructure, not this permission
    // change.
    expect([401, 403]).not.toContain(photoResponse.status());

    // Give the interceptor's 403 path (refresh token → retry → logout →
    // router.navigate(['/auth'])) more than enough time to fire if the
    // endpoint had forbidden the read.
    await page.waitForTimeout(5000);

    expect(photoStatuses).not.toContain(403);
    expect(page.url()).not.toContain('/auth');
    await expect(page.locator('#sign-out-dropdown')).toBeVisible();

    // Hard proof the session survived rather than merely the URL: `logout()`
    // resets the auth store to `authInitialState` (accessToken ''), and
    // `AuthSyncStorageService` mirrors that state into localStorage['auth'].
    const accessTokenAfter = await page.evaluate(() => {
      try {
        return JSON.parse(localStorage.getItem('auth') ?? '{}')?.token?.accessToken ?? '';
      } catch {
        return '';
      }
    });
    expect(accessTokenAfter).not.toBe('');
  });
});
