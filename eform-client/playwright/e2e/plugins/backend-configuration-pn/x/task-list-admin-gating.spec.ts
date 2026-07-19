import { test, expect, Page } from '@playwright/test';
import { generateRandmString } from '../../../helper-functions';
import { TaskListPage } from '../task-list.page';

/**
 * Task list ADMIN-GATING suite (backend-configuration-task-list-page
 * feature).
 *
 * Unlike the calendar view-mode dropdown (`q/calendar-admin-gating.spec.ts`),
 * which hides options client-side while the ROUTE stays reachable, the
 * task-list route itself carries `canActivate: [IsAdminGuard]`
 * (`backend-configuration-pn.routing.ts`) — the ONLY route in the app using
 * this guard (grepped; `IsAdminGuard`/`PermissionGuard` both simply resolve
 * a boolean with no `router.navigate(...)` call on denial — see
 * `admin.guard.ts`/`permission.guard.ts`). Per the browser-verified finding
 * from Task 9 of the implementation plan ("deny-no-redirect matches
 * PermissionGuard convention"), a non-admin who navigates straight to
 * `/plugins/backend-configuration-pn/task-list` stays on that URL but the
 * page's content (`#taskListGrid` / `app-task-list-page`) never mounts —
 * there is no redirect to `/` or `/auth` to assert against.
 *
 * `loginViaApi`/`loginAs`/`setupNonAdminUser` are copied VERBATIM from
 * `r/property-workers-nonadmin-no-logout.spec.ts` (lines 23-166, also reused
 * by `q/calendar-admin-gating.spec.ts`) — the same minimal non-admin
 * security-group setup (backend-configuration + time-planning plugin
 * access, core device-users/eform-tags claims) already proven to reach
 * backend-configuration-pn routes as a non-admin. The menu item itself
 * carries no `Permissions` requirement (`EformBackendConfigurationPlugin.cs`
 * `GetNavigationMenu`, `Permissions = []`), so this non-admin may still see
 * the "Task list" sidebar entry — only the route guard is under test here,
 * not menu visibility.
 */

const BASE_URL = 'http://localhost:4200';
const USER_PASSWORD = 'Secret_password_2026!';

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
 */
async function setupNonAdminUser(page: Page, rand: string): Promise<string> {
  const token = await loginViaApi(page, 'admin@admin.com', 'secretpassword');
  const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
  const groupName = `tl-admin-gate-${rand}`;
  const userEmail = `tluser-${rand}@test.com`;

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

  // Land non-admin logins directly on the (permission-guarded, not
  // admin-guarded) property-workers page.
  await page.request.put(`${BASE_URL}/api/security/groups/settings`, {
    headers,
    data: { id: groupId, redirectLink: '/plugins/backend-configuration-pn/property-workers' },
  });

  // 3. Plugin permissions: backend-configuration + time-planning access.
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
      firstName: `TlUser${rand}`,
      lastName: 'AdminGate',
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

test.describe.serial('Task list — non-admin route gating', () => {
  const rand = generateRandmString(8).toLowerCase();
  let userEmail = '';

  // -----------------------------------------------------------------------
  // Seed test — security group + non-admin user (as admin). Runs first via
  // describe.serial.
  // -----------------------------------------------------------------------
  test('seed: create non-admin user (as admin)', async ({ page }) => {
    test.setTimeout(300000);
    await page.goto(BASE_URL);
    await loginAs(page, 'admin@admin.com', 'secretpassword');
    await page.locator('#newEFormBtn').waitFor({ state: 'visible', timeout: 120000 });

    userEmail = await setupNonAdminUser(page, rand);
    expect(userEmail).not.toBe('');
  });

  // =======================================================================
  // Non-admin direct-URL navigation is denied without a redirect: the URL
  // stays on task-list, but the grid never mounts.
  // =======================================================================
  test('non-admin cannot reach the task-list page', async ({ page }) => {
    test.setTimeout(300000);
    expect(userEmail).not.toBe('');

    await page.goto(BASE_URL);
    await loginAs(page, userEmail, USER_PASSWORD);
    expect(page.url()).not.toContain('/auth');

    const taskListPage = new TaskListPage(page);
    await page.goto(`${BASE_URL}/plugins/backend-configuration-pn/task-list`);
    await page.waitForTimeout(3000);

    // Positive app-shell assertion FIRST: proves the SPA actually rendered
    // for this non-admin (not a blank page / crashed bootstrap) before the
    // negative denial assertions below are trusted. `#backend-configuration-pn`
    // is the plugin's sidebar group button (same element `TaskListPage.
    // goToViaMenu()` clicks to expand the submenu) — it renders for any user
    // whose group has backend_configuration_plugin_access (this non-admin
    // does, per `setupNonAdminUser`), independent of locale/i18n text.
    await expect(page.locator('#backend-configuration-pn')).toBeVisible();

    // IsAdminGuard denies without an explicit redirect — assert the guarded
    // content never rendered (the load-bearing assertion) and note the URL
    // for diagnostics without hard-failing on its exact value, since Angular
    // Router's post-denial URL isn't itself the contract being protected.
    await expect(taskListPage.getGrid()).toHaveCount(0);
    await expect(page.locator('app-task-list-page')).toHaveCount(0);
    console.log(`non-admin task-list attempt landed on: ${page.url()}`);
  });
});
