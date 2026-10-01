import { test, expect, Page, APIResponse } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import { openRowActionMenu } from '../row-action-menu';
import { API_TIMEOUT, SLOW_API_TIMEOUT, UI_TIMEOUT, waitForApiResponse } from '../wait-helpers';

/**
 * Editing and deleting a property, changing its control areas and deleting a
 * compliance are admin-only.
 *
 * NA1: a `user`-role account with property read access opens the properties
 *      page; the row action menu has no Edit and no Delete entry.
 * NA2: the same account's token is refused (403) by
 *        PUT    api/backend-configuration-pn/properties
 *        DELETE api/backend-configuration-pn/properties
 *        PUT    api/backend-configuration-pn/property-areas
 *        DELETE api/backend-configuration-pn/compliances/delete/{id}
 *      and the property is unchanged afterwards.
 *
 * The admin paths of the same endpoints are exercised by the existing
 * property/area specs, which all run as the default admin.
 */

const BASE_URL = 'http://localhost:4200';
const ADMIN_EMAIL = 'admin@admin.com';
const ADMIN_PASSWORD = 'secretpassword';
const USER_PASSWORD = 'Secret_password_2026!';
const PROPERTIES_URL = `${BASE_URL}/plugins/backend-configuration-pn/properties`;

const rand = generateRandmString(6).toLowerCase();

const property: PropertyCreateUpdate = {
  name: `paa-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

let propertyId = 0;
let userEmail = '';

async function apiToken(page: Page, email: string, password: string): Promise<string> {
  const res = await page.request.post(`${BASE_URL}/api/auth/token`, {
    form: { username: email, password, grant_type: 'password' },
    timeout: API_TIMEOUT,
  });
  const token = (await res.json())?.model?.accessToken || '';
  expect(token, `no access token for ${email} (status ${res.status()})`).not.toBe('');
  return token;
}

function bearer(token: string): Record<string, string> {
  return { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
}

/** UI login for an arbitrary account; `LoginPage.login()` only knows the default admin. */
async function loginAs(page: Page, email: string, password: string): Promise<void> {
  const loginBtn = page.locator('#loginBtn');
  await loginBtn.waitFor({ state: 'visible', timeout: SLOW_API_TIMEOUT });
  await page.locator('#username').fill(email);
  await page.locator('#password').fill(password);
  await Promise.all([
    waitForApiResponse(page, `POST /api/auth/token (${email})`, r => r.url().includes('/api/auth/token'), API_TIMEOUT),
    loginBtn.click(),
  ]);
  await page.waitForURL(url => !url.pathname.startsWith('/auth'), { timeout: UI_TIMEOUT });
}

/**
 * A security group with backend-configuration plugin access and property read
 * access, landing on the properties page, and a `user`-role web account in it.
 * Same API sequence as `s/compliance-page-shell.spec.ts` `setupNonAdminUser`.
 */
async function setupNonAdminUser(page: Page, adminToken: string): Promise<string> {
  const headers = bearer(adminToken);
  const groupName = `paa-user-${rand}`;
  const email = `paauser-${rand}@test.com`;

  const createGroupRes = await page.request.post(`${BASE_URL}/api/security/groups`, {
    headers, data: { userIds: [], name: groupName }, timeout: API_TIMEOUT,
  });
  expect(createGroupRes.ok(), `create group ${groupName}: status ${createGroupRes.status()}`).toBe(true);
  const indexRes = await page.request.post(`${BASE_URL}/api/security/groups/index`, {
    headers,
    data: { sort: 'Id', nameFilter: groupName, pageIndex: 0, pageSize: 100, isSortDsc: false, offset: 0 },
    timeout: API_TIMEOUT,
  });
  const groups = (await indexRes.json())?.model?.entities || [];
  const groupId = groups.find((g: any) => g.groupName === groupName)?.id || 0;
  expect(groupId, `security group ${groupName} was not created`).toBeGreaterThan(0);

  const settingsRes = await page.request.put(`${BASE_URL}/api/security/groups/settings`, {
    headers,
    data: { id: groupId, redirectLink: '/plugins/backend-configuration-pn/properties' },
    timeout: API_TIMEOUT,
  });
  expect(settingsRes.ok(), `group settings for ${groupName}: status ${settingsRes.status()}`).toBe(true);

  const pluginsRes = await page.request.get(
    `${BASE_URL}/api/plugins-management/installed?sort=id&isSortDsc=true&pageSize=1000&pageIndex=0&offset=0`,
    { headers, timeout: API_TIMEOUT },
  );
  const plugins = (await pluginsRes.json())?.model?.pluginsList || [];
  const plugin = plugins.find((p: any) => p.pluginId === 'eform-backend-configuration-plugin');
  expect(plugin, 'backend-configuration plugin is not installed').toBeTruthy();
  const permUrl = `${BASE_URL}/api/plugins-permissions/group-permissions/${plugin.id}`;
  const currentPerms = (await (await page.request.get(permUrl, { headers, timeout: API_TIMEOUT })).json())?.model || [];
  const permIdMap: Record<string, number> = {};
  for (const gp of currentPerms) {
    for (const perm of gp.permissions || []) {
      permIdMap[perm.claimName] = perm.permissionId;
    }
  }
  const pluginPerms = ['backend_configuration_plugin_access', 'properties_get'].map((claimName, i) => ({
    isEnabled: true,
    claimName,
    permissionId: permIdMap[claimName] || i + 1,
    permissionName: claimName,
  }));
  const permsRes = await page.request.put(permUrl, {
    headers, data: [{ permissions: pluginPerms, groupId }], timeout: API_TIMEOUT,
  });
  expect(permsRes.ok(), `plugin permissions for ${groupName}: status ${permsRes.status()}`).toBe(true);

  const createUserRes = await page.request.post(`${BASE_URL}/api/admin/create-user`, {
    headers,
    data: {
      id: 0,
      firstName: `PaaUser${rand}`,
      lastName: 'NonAdmin',
      userName: email,
      email,
      password: USER_PASSWORD,
      passwordConfimation: USER_PASSWORD,
      role: 'user',
      groupId,
      isDeviceUser: false,
    },
    timeout: API_TIMEOUT,
  });
  const createUserJson = await createUserRes.json().catch(() => null);
  expect(createUserJson?.success, `create-user ${email}: status ${createUserRes.status()}`).toBe(true);
  return email;
}

async function findPropertyId(page: Page, adminToken: string): Promise<number> {
  const res = await page.request.post(`${BASE_URL}/api/backend-configuration-pn/properties/index`, {
    headers: bearer(adminToken),
    data: { sort: 'Id', isSortDsc: false, nameFilter: property.name, pageIndex: 0, pageSize: 100, offset: 0 },
    timeout: API_TIMEOUT,
  });
  const entities = (await res.json())?.model?.entities || [];
  return entities.find((p: any) => p.name === property.name)?.id || 0;
}

test.describe.serial('Property edit/delete, control areas and compliance delete are admin-only', () => {
  test.afterAll(async ({ browser }) => {
    if (!propertyId) {
      return;
    }
    const page = await browser.newPage();
    try {
      const adminToken = await apiToken(page, ADMIN_EMAIL, ADMIN_PASSWORD);
      const res = await page.request.delete(
        `${BASE_URL}/api/backend-configuration-pn/properties?propertyId=${propertyId}`,
        { headers: bearer(adminToken), timeout: API_TIMEOUT },
      );
      console.log(`afterAll: admin delete of property ${propertyId} → ${res.status()}`);
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    } finally {
      await page.close();
    }
  });

  test('seed: a property and a user-role account (as admin)', async ({ page }) => {
    // UI property create (SDK folder provisioning) + a handful of admin API calls.
    test.setTimeout(180000);
    await page.goto(BASE_URL);
    await new LoginPage(page).login();

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const adminToken = await apiToken(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    propertyId = await findPropertyId(page, adminToken);
    expect(propertyId, `property ${property.name} not found after create`).toBeGreaterThan(0);
    userEmail = await setupNonAdminUser(page, adminToken);
  });

  test('NA1: a non-admin sees no Edit or Delete in the property row menu', async ({ page }) => {
    // One UI login and one page.
    test.setTimeout(120000);
    expect(userEmail, 'seed did not run').not.toBe('');

    await page.goto(BASE_URL);
    await loginAs(page, userEmail, USER_PASSWORD);
    await page.goto(PROPERTIES_URL);
    await page.locator('app-properties-container').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    const row = page.locator('app-properties-table .mat-mdc-row').filter({ hasText: property.name });
    await expect(row).toHaveCount(1, { timeout: UI_TIMEOUT });
    const menuItem = await openRowActionMenu(page, row, `Property row "${property.name}"`);
    // The menu is open: the always-present entry is there...
    await expect(menuItem('showPropertyAreasBtn')).toBeAttached({ timeout: UI_TIMEOUT });
    // ...and the admin-only entries are not.
    await expect(menuItem('editPropertyBtn')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(menuItem('deletePropertyBtn')).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('NA2: the API refuses the four actions to a non-admin and the property is unchanged', async ({ page }) => {
    // API calls only.
    test.setTimeout(60000);
    expect(userEmail, 'seed did not run').not.toBe('');
    const userHeaders = bearer(await apiToken(page, userEmail, USER_PASSWORD));
    const api = `${BASE_URL}/api/backend-configuration-pn`;

    const calls: [string, () => Promise<APIResponse>][] = [
      ['PUT properties', () => page.request.put(`${api}/properties`, {
        headers: userHeaders, data: { id: propertyId, name: `${property.name}-renamed` }, timeout: API_TIMEOUT,
      })],
      ['DELETE properties', () => page.request.delete(`${api}/properties?propertyId=${propertyId}`, {
        headers: userHeaders, timeout: API_TIMEOUT,
      })],
      ['PUT property-areas', () => page.request.put(`${api}/property-areas`, {
        headers: userHeaders, data: { propertyId, areas: [] }, timeout: API_TIMEOUT,
      })],
      // Authorization runs before the action, so a non-existent id still answers 403.
      ['DELETE compliances/delete/{id}', () => page.request.delete(`${api}/compliances/delete/0`, {
        headers: userHeaders, timeout: API_TIMEOUT,
      })],
    ];
    for (const [name, call] of calls) {
      expect((await call()).status(), `${name} must be refused to the user role`).toBe(403);
    }

    // Reads stay open to the user role.
    const userRead = await page.request.get(`${api}/properties?id=${propertyId}`, {
      headers: userHeaders, timeout: API_TIMEOUT,
    });
    expect(userRead.status(), 'GET properties must stay open to the user role').toBe(200);

    const adminRead = await page.request.get(`${api}/properties?id=${propertyId}`, {
      headers: bearer(await apiToken(page, ADMIN_EMAIL, ADMIN_PASSWORD)), timeout: API_TIMEOUT,
    });
    const body = await adminRead.json();
    expect(body?.success, `admin read of property ${propertyId} failed: ${body?.message}`).toBe(true);
    expect(body?.model?.name).toBe(property.name);
  });
});
