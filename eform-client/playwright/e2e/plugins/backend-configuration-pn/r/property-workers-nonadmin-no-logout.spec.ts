import { test, expect, Page } from '@playwright/test';
import {
  BackendConfigurationPropertiesPage,
} from '../BackendConfigurationProperties.page';
import { BackendConfigurationPropertyWorkersPage } from '../BackendConfigurationPropertyWorkers.page';
import { generateRandmString } from '../../../helper-functions';

// Regression for the user report: a NON-ADMIN user whose security group has
// backend-configuration + time-planning plugin access and the core
// device-users/tags permissions could create/edit workers and manage tags on
// /plugins/backend-configuration-pn/property-workers. The pay-rule-set
// selector feature (3903669b) added an unconditional
// GET api/time-planning-pn/pay-rule-sets in the create/edit modal's ngOnInit,
// but the endpoint was [Authorize(Roles = Admin)]; for non-admins that 403s and
// the global HttpErrorInterceptor escalates it (refresh token -> retry -> still
// 403) into a forced logout.
//
// The fix opened up the READ side instead of gating the call: listing and
// reading pay rule sets is now allowed for any authenticated user, so a
// non-admin can actually select a pay rule set for a worker. Creating, editing
// and deleting pay rule sets stays admin-only. This spec therefore guards that
// the modal's fetch runs for BOTH roles and that the endpoint answers 200 for a
// non-admin instead of the 403 that used to end the session.

const BASE_URL = 'http://localhost:4200';
const USER_PASSWORD = 'Secret_password_2026!';
const PAY_RULE_SETS_URL = 'api/time-planning-pn/pay-rule-sets';

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

async function logout(page: Page): Promise<void> {
  await page.evaluate(() => {
    localStorage.removeItem('auth');
    localStorage.removeItem('token');
  });
  await page.goto(`${BASE_URL}/auth`);
  await page.locator('#loginBtn').waitFor({ state: 'visible', timeout: 60000 });
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

test.describe.serial('Property-workers as non-admin (backend + timeregistration)', () => {
  const rand = generateRandmString(8).toLowerCase();
  const propertyName = `pw-reg-${rand}`;
  const tagName = `pw-tag-${rand}`;
  let userEmail = '';

  test('seed: security group, non-admin user, property (as admin)', async ({ page }) => {
    test.setTimeout(300000);
    await page.goto(BASE_URL);
    await loginAs(page, 'admin@admin.com', 'secretpassword');
    await page.locator('#newEFormBtn').waitFor({ state: 'visible', timeout: 120000 });

    userEmail = await setupNonAdminUser(page, rand);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty({
      name: propertyName,
      cvrNumber: '1111111',
      chrNumber: rand.substring(0, 6),
      address: 'Regression Street 1',
    });

    // Positive direction: for an ADMIN the create-worker modal must still
    // fetch the pay rule sets. The fix opened the read endpoint up rather than
    // gating the call, so the fetch has to keep firing for every role — this
    // guards against "fixing" it by suppressing the request instead.
    const adminPayRuleSetRequests: string[] = [];
    page.on('request', (req) => {
      if (req.url().includes(PAY_RULE_SETS_URL)) {
        adminPayRuleSetRequests.push(req.url());
      }
    });
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.openCreateModal({ name: 'AdminProbe', surname: 'PayRules' });
    await expect
      .poll(() => adminPayRuleSetRequests.length, { timeout: 15000 })
      .toBeGreaterThan(0);
    await workersPage.closeCreateModal(true); // cancel
  });

  test('non-admin opens create-worker modal and manages tags without being logged out', async ({ page }) => {
    test.setTimeout(300000);
    expect(userEmail).not.toBe('');

    // Track the modal's pay-rule-sets requests — this user is a non-admin, and
    // the endpoint is expected to answer them (200), not 403.
    const payRuleSetRequests: string[] = [];
    page.on('request', (req) => {
      if (req.url().includes(PAY_RULE_SETS_URL)) {
        payRuleSetRequests.push(req.url());
      }
    });

    await page.goto(BASE_URL);
    await loginAs(page, userEmail, USER_PASSWORD);
    expect(page.url()).not.toContain('/auth');

    // The group's redirectLink lands the user directly on property-workers
    // (the sidebar plugin menu is not rendered for this claims set, so the
    // menu-based goToPropertyWorkers() helper is not usable here).
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    if (!page.url().includes('/plugins/backend-configuration-pn/property-workers')) {
      await page.goto(`${BASE_URL}/plugins/backend-configuration-pn/property-workers`);
    }
    await workersPage.newDeviceUserBtn().waitFor({ state: 'visible', timeout: 30000 });
    // Reaching the page at all proves the group claims work.
    expect(page.url()).toContain('/plugins/backend-configuration-pn/property-workers');

    // Tag management (the reported "create tags" flow) must survive.
    await workersPage.createTag(tagName);
    await page.waitForTimeout(1000);
    expect(page.url()).not.toContain('/auth');

    // THE regression: opening the create/edit worker modal fired the
    // admin-only pay-rule-sets fetch and force-logged the user out. A minimal
    // fill is enough — the fetch fires unconditionally in the modal's ngOnInit.
    await workersPage.openCreateModal({
      name: `Wk${rand}`,
      surname: 'NoLogout',
    });
    // Give the interceptor's 403 -> refresh -> retry -> logout chain (the
    // buggy behavior) ample time to fire if it is going to.
    await page.waitForTimeout(5000);

    // Still logged in, still on the page, modal still open.
    expect(page.url()).not.toContain('/auth');
    expect(page.url()).toContain('/plugins/backend-configuration-pn/property-workers');
    await expect(page.locator('mat-dialog-container')).toBeVisible();

    // The fetch now DOES happen for a non-admin: listing pay rule sets is no
    // longer [Authorize(Roles = Admin)], so a non-admin can pick one for a
    // worker. The point of this spec is that it returns 200 instead of the 403
    // that used to be escalated into a logout — hence the assertions above.
    expect(payRuleSetRequests.length).toBeGreaterThan(0);
  });

  test('the pay-rule-sets list returns 200 for a non-admin (no longer admin-only)', async ({ page }) => {
    test.setTimeout(300000);
    expect(userEmail).not.toBe('');

    // Straight at the API: this endpoint used to be [Authorize(Roles = Admin)]
    // and answered 403 for this exact group, which the HttpErrorInterceptor
    // turned into a forced logout. Asserting the status directly is what pins
    // the authorization change; the UI consequence is covered by the test
    // above (the modal fetches it and the user stays logged in).
    const token = await loginViaApi(page, userEmail, USER_PASSWORD);
    expect(token).not.toBe('');
    const res = await page.request.get(
      `${BASE_URL}/api/time-planning-pn/pay-rule-sets?offset=0&pageSize=1000`,
      { headers: { Authorization: `Bearer ${token}` } },
    );
    expect(res.status()).toBe(200);
    expect((await res.json())?.success).toBe(true);

    // Authoring pay rule sets stays admin-only for the same user.
    const createRes = await page.request.post(`${BASE_URL}/api/time-planning-pn/pay-rule-sets`, {
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      data: { name: `nonadmin-should-not-create-${rand}`, payDayRules: [], payDayTypeRules: [] },
    });
    expect(createRes.status()).toBe(403);
  });
});
