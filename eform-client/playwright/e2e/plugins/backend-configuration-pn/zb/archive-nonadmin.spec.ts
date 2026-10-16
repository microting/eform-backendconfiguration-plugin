import { APIRequestContext, expect, Page, test } from '@playwright/test';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationArchivePage, DEFAULT_FOLDER, FOLDERS_PATH } from '../BackendConfigurationArchive.page';
import DatabaseConfigurationConstants from '../../../Constants/DatabaseConfigurationConstants';
import LoginConstants from '../../../Constants/LoginConstants';
import { API_TIMEOUT, UI_TIMEOUT, waitForApiResponse } from '../wait-helpers';

/**
 * Arkiv as a plain user (D5): folders can be navigated, but there is no "Ny mappe", no ⋮ folder menu, no
 * Læseadgang column and no Indbakke tab. The user has no linked worker, so it is not disabled (D7) and sees
 * the open folders. Setup goes through the admin API, the check through the UI.
 */
const BASE_URL = 'http://localhost:4200';
/** The CI admin. */
const ADMIN_EMAIL: string = DatabaseConfigurationConstants.email;
const ADMIN_PASSWORD: string = LoginConstants.password;
const USER_PASSWORD = 'Secret_password_2026!';
/** A cold app can take up to a minute before the login button renders. */
const COLD_APP_TIMEOUT = 60000;
const rand = generateRandmString(6).toLowerCase();
const openFolder = `Faelles-${rand}`;

async function apiToken(request: APIRequestContext, email: string, password: string): Promise<string> {
  const res = await request.post(`${BASE_URL}/api/auth/token`, {
    form: { username: email, password, grant_type: 'password' },
    timeout: API_TIMEOUT,
  });
  expect(res.ok(), `token for ${email}: HTTP ${res.status()}`).toBe(true);
  const token = (await res.json())?.model?.accessToken;
  expect(token, `access token for ${email}`).toBeTruthy();
  return token;
}

/** One admin API call; fails with the status and body when it is not 2xx. */
async function call(request: APIRequestContext, method: 'GET' | 'POST' | 'PUT', url: string, token: string,
  what: string, data?: unknown): Promise<any> {
  const res = await request.fetch(`${BASE_URL}${url}`, {
    method,
    data,
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    timeout: API_TIMEOUT,
  });
  const text = await res.text();
  expect(res.ok(), `${what}: HTTP ${res.status()} ${text}`).toBe(true);
  return text ? JSON.parse(text) : null;
}

/**
 * A security group with the baseline app claims and plugin access to backend-configuration, landing on the
 * Arkiv page, and a `user`-role account in it without a device user (so no worker is linked).
 */
async function createNonAdminUser(request: APIRequestContext, token: string): Promise<string> {
  const groupName = `arkiv-user-${rand}`;
  const email = `arkiv-${rand}@example.com`;

  await call(request, 'POST', '/api/security/groups', token, 'creating the security group', { userIds: [], name: groupName });
  const groups = await call(request, 'POST', '/api/security/groups/index', token, 'listing security groups',
    { sort: 'Id', nameFilter: groupName, pageIndex: 0, pageSize: 100, isSortDsc: false, offset: 0 });
  const groupId = (groups?.model?.entities ?? []).find((g: any) => g.groupName === groupName)?.id ?? 0;
  expect(groupId, `id of security group ${groupName}`).toBeGreaterThan(0);

  const coreClaims = ['eforms_read', 'cases_read', 'case_read'];
  const permissionTypes = (await call(request, 'GET', `/api/security/permissions/${groupId}`, token, 'reading group permissions'))
    ?.model?.permissionTypes ?? [];
  const permissions = permissionTypes.flatMap((pt: any) => (pt.permissions ?? [])
    .map((p: any) => ({ ...p, isEnabled: p.isEnabled || coreClaims.includes(p.claimName) })));
  await call(request, 'PUT', '/api/security/permissions', token, 'granting the baseline claims', { groupId, permissions });
  await call(request, 'PUT', '/api/security/groups/settings', token, 'setting the landing page',
    { id: groupId, redirectLink: '/plugins/backend-configuration-pn/files' });

  const plugins = await call(request, 'GET',
    '/api/plugins-management/installed?sort=id&isSortDsc=true&pageSize=1000&pageIndex=0&offset=0', token, 'listing plugins');
  const backend = (plugins?.model?.pluginsList ?? []).find((p: any) => p.pluginId === 'eform-backend-configuration-plugin');
  expect(backend, 'the backend-configuration plugin is installed').toBeTruthy();
  const permUrl = `/api/plugins-permissions/group-permissions/${backend.id}`;
  const current = await call(request, 'GET', permUrl, token, 'reading plugin permissions');
  const permissionIds: Record<string, number> = {};
  for (const groupPermissions of current?.model ?? []) {
    for (const perm of groupPermissions.permissions ?? []) {
      permissionIds[perm.claimName] = perm.permissionId;
    }
  }
  const wanted = ['backend_configuration_plugin_access', 'properties_get'];
  // A fresh group may list no permission ids yet, hence the `|| index + 1` fallback.
  await call(request, 'PUT', permUrl, token, 'granting plugin access', [{
    groupId,
    permissions: wanted.map((claimName, index) => ({
      isEnabled: true, claimName, permissionId: permissionIds[claimName] || index + 1, permissionName: claimName,
    })),
  }]);

  const created = await call(request, 'POST', '/api/admin/create-user', token, 'creating the user', {
    id: 0, firstName: 'Jane', lastName: `Doe ${rand}`, userName: email, email,
    password: USER_PASSWORD, passwordConfimation: USER_PASSWORD, role: 'user', groupId, isDeviceUser: false,
  });
  expect(created?.success, `create-user ${email}: ${created?.message ?? ''}`).toBe(true);
  return email;
}

async function loginAs(page: Page, email: string, password: string): Promise<void> {
  await page.goto(BASE_URL, { timeout: COLD_APP_TIMEOUT });
  await page.locator('#loginBtn').waitFor({ state: 'visible', timeout: COLD_APP_TIMEOUT });
  await page.locator('#username').fill(email, { timeout: UI_TIMEOUT });
  await page.locator('#password').fill(password, { timeout: UI_TIMEOUT });
  const token = waitForApiResponse(page, `POST /api/auth/token for ${email}`,
    r => new URL(r.url()).pathname === '/api/auth/token', API_TIMEOUT);
  await page.locator('#loginBtn').click({ timeout: UI_TIMEOUT });
  expect((await token).ok(), `login of ${email}`).toBe(true);
  await page.waitForURL(url => !url.pathname.startsWith('/auth'), { timeout: API_TIMEOUT });
}

test.describe.serial('Arkiv as a non-admin', () => {
  let page: Page;
  let email = '';

  test.beforeAll(async ({ browser }) => {
    // About a dozen admin API calls (API_TIMEOUT each).
    test.setTimeout(180000);
    page = await browser.newPage();
    const token = await apiToken(page.request, ADMIN_EMAIL, ADMIN_PASSWORD);
    email = await createNonAdminUser(page.request, token);
    const folder = await call(page.request, 'POST', FOLDERS_PATH, token, `creating folder ${openFolder}`,
      { name: openFolder, parentId: null });
    expect(folder?.success, `creating folder ${openFolder}: ${folder?.message ?? ''}`).toBe(true);
  });

  test.afterAll(async () => {
    await page?.close();
  });

  test('N1 a non-admin navigates the folders but gets no folder administration and no access column', async () => {
    // Cold login plus one page load.
    test.setTimeout(150000);
    await loginAs(page, email, USER_PASSWORD);
    const archive = new BackendConfigurationArchivePage(page);
    await archive.open();
    await expect(archive.folderRow(DEFAULT_FOLDER)).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(archive.folderRow(openFolder)).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(archive.dateCheckChip()).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(page.locator('#addNewFileBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(page.locator('#archiveNewFolderBtn')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(page.locator('[id^="archiveFolderMenuBtn-"]')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(page.locator('#archiveAccessHeader')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(page.locator('#archiveNavInbox')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await archive.selectFolder(openFolder);
    await expect(archive.crumb()).toContainText(openFolder, { timeout: UI_TIMEOUT });
  });
});
