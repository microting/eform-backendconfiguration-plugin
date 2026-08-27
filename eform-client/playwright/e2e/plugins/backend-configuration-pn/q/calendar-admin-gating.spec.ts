import { test, expect, Page } from '@playwright/test';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import { generateRandmString } from '../../../helper-functions';

/**
 * Calendar COMPLIANCE admin-gating suite.
 *
 * The view-mode dropdown's admin-only option ("Compliance") is built
 * conditionally on `isAdmin` in calendar-header.component.ts's
 * `buildViewModeOptions()`. "Måned"/Month is NOT gated — it is available to
 * every user, exactly like Dag/Uge/Tidsplan (see
 * `q/calendar-month-view.spec.ts`, MV1, for the ADMIN dropdown order:
 * Dag, Uge, Måned, Tidsplan, Compliance). A NON-ADMIN user must see exactly
 * ['Dag', 'Uge', 'Måned', 'Tidsplan'] — never 'Compliance'.
 *
 * `loginViaApi`/`loginAs`/`setupNonAdminUser` are copied VERBATIM from
 * `r/property-workers-nonadmin-no-logout.spec.ts` (lines 23-166) — the same
 * minimal non-admin security-group setup (backend-configuration +
 * time-planning plugin access, core device-users/eform-tags claims) already
 * proven to reach backend-configuration-pn pages as a non-admin.
 *
 * Navigation note (brief step 3 fallback): `CalendarUiEnhancementsPage
 * .goToCalendar()` navigates directly via `page.goto(...)`, not through the
 * sidebar menu, so this test does not depend on whether the calendar link
 * is rendered in this non-admin's sidebar — only on the calendar route
 * itself being reachable with these claims (the same
 * backend_configuration_plugin_access / task_management_enable claims this
 * non-admin setup already grants, and the same route the ADMIN suite hits).
 * The menu-based fallback (`page.goto('http://localhost:4200/plugins/
 * backend-configuration-pn/calendar')` directly, bypassing any menu click)
 * is therefore already what `goToCalendar()` does — no extra adaptation was
 * needed.
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
  const groupName = `pw-regression-${rand}`;
  const userEmail = `pwuser-${rand}@test.com`;

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
  //    tags (create/edit/assign tags) — what the affected customer group has.
  //    eforms_read/cases_read/case_read are the baseline app-entry claims the
  //    BC helper also grants to its auto-created groups ("Kun tid").
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

  // Land non-admin logins directly on the property-workers page (same
  // mechanism the k-shard spec uses for its "Kun tid" group).
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
      firstName: `PwUser${rand}`,
      lastName: 'Regression',
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

test.describe.serial('Calendar compliance view — non-admin dropdown gating', () => {
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
  // Non-admin dropdown gating — Måned stays present, Compliance does not.
  // =======================================================================
  test('non-admin sees Måned but not Compliance', async ({ page }) => {
    test.setTimeout(300000);
    expect(userEmail).not.toBe('');

    await page.goto(BASE_URL);
    await loginAs(page, userEmail, USER_PASSWORD);
    expect(page.url()).not.toContain('/auth');

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await page.waitForTimeout(2000);
    await page.locator('#calendarViewModeSelect').click();
    const options = page.locator('.ng-dropdown-panel .ng-option');
    await expect(options).toHaveText(['Dag', 'Uge', 'Måned', 'Tidsplan']);
  });
});
