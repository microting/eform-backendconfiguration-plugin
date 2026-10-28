/**
 * Admin-API setup and sign-in shared by the archive role specs (zb/archive-nonadmin, zc/archive-leader).
 * Every call carries an explicit timeout and fails with its status and body (plugin CLAUDE.md, "Playwright tests
 * fail fast").
 */
import { APIRequestContext, expect, Page } from '@playwright/test';
import DatabaseConfigurationConstants from '../../Constants/DatabaseConfigurationConstants';
import LoginConstants from '../../Constants/LoginConstants';
import { API_TIMEOUT, ignoreUnhandledRejections, UI_TIMEOUT, waitForApiResponse } from './wait-helpers';

export const BASE_URL = 'http://localhost:4200';
/** The CI admin. */
export const ADMIN_EMAIL: string = DatabaseConfigurationConstants.email;
export const ADMIN_PASSWORD: string = LoginConstants.password;
/** A cold app can take up to a minute before the login button (or the landing page) renders. */
const COLD_APP_TIMEOUT = 60000;

export async function apiToken(request: APIRequestContext, email: string, password: string): Promise<string> {
  const res = await request.post(`${BASE_URL}/api/auth/token`, {
    form: { username: email, password, grant_type: 'password' },
    timeout: API_TIMEOUT,
  });
  expect(res.ok(), `token for ${email}: HTTP ${res.status()}`).toBe(true);
  const token = (await res.json())?.model?.accessToken;
  expect(token, `access token for ${email}`).toBeTruthy();
  return token;
}

export interface CallOptions {
  data?: unknown;
  /** SDK-backed calls pass SLOW_API_TIMEOUT. */
  timeout?: number;
  /** The exact HTTP status expected instead of any 2xx (e.g. 403 for a refused [ArchiveManager] route). */
  status?: number;
}

/**
 * One API call as `token`. Fails with the status and body when the status is not 2xx (or not `options.status`).
 * Returns the parsed body of a 2xx answer, or null for an empty body or an expected non-2xx status.
 */
export async function call(request: APIRequestContext, method: 'GET' | 'POST' | 'PUT' | 'DELETE', url: string,
  token: string, what: string, options: CallOptions = {}): Promise<any> {
  const res = await request.fetch(`${BASE_URL}${url}`, {
    method,
    data: options.data,
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    timeout: options.timeout ?? API_TIMEOUT,
  });
  const text = await res.text();
  if (options.status !== undefined) {
    expect(res.status(), `${what}: HTTP ${res.status()} ${text}`).toBe(options.status);
    return null;
  }
  expect(res.ok(), `${what}: HTTP ${res.status()} ${text}`).toBe(true);
  return text ? JSON.parse(text) : null;
}

/** {@link call} for an OperationResult endpoint: also fails when `success` is not true (it answers 200 on failure too). */
export async function callOk(request: APIRequestContext, method: 'GET' | 'POST' | 'PUT' | 'DELETE', url: string,
  token: string, what: string, options: Omit<CallOptions, 'status'> = {}): Promise<any> {
  const body = await call(request, method, url, token, what, options);
  expect(body?.success, `${what}: ${body?.message ?? ''}`).toBe(true);
  return body;
}

/**
 * Signs in through the login page and waits for the app to leave it. With `landing`, also waits for that element of
 * the landing page (the app has stored the token and rendered the menu).
 */
export async function loginAs(page: Page, email: string, password: string, landing?: string): Promise<void> {
  await page.goto(BASE_URL, { timeout: COLD_APP_TIMEOUT });
  await page.locator('#loginBtn').waitFor({ state: 'visible', timeout: COLD_APP_TIMEOUT });
  await page.locator('#username').fill(email, { timeout: UI_TIMEOUT });
  await page.locator('#password').fill(password, { timeout: UI_TIMEOUT });
  const token = waitForApiResponse(page, `POST /api/auth/token for ${email}`,
    r => new URL(r.url()).pathname === '/api/auth/token', API_TIMEOUT);
  // Awaited after the click, which can throw first.
  ignoreUnhandledRejections(token);
  await page.locator('#loginBtn').click({ timeout: UI_TIMEOUT });
  expect((await token).ok(), `login of ${email}`).toBe(true);
  await page.waitForURL(url => !url.pathname.startsWith('/auth'), { timeout: API_TIMEOUT });
  if (landing) {
    await expect(page.locator(landing), `the landing page of ${email}`).toBeVisible({ timeout: COLD_APP_TIMEOUT });
  }
}

export interface GroupUser {
  groupName: string;
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  /** Core claim names to enable on the new group. */
  coreClaims: string[];
  /** pluginId → plugin claim names to enable on the new group. */
  pluginClaims: Record<string, string[]>;
  redirectLink: string;
}

/**
 * A new security group with exactly these claims and landing page, and a `user`-role account in it (no worker).
 * Returns the group's id (looked up by name, never assumed: Galera offsets).
 */
export async function createUserInNewGroup(request: APIRequestContext, token: string, user: GroupUser): Promise<number> {
  await call(request, 'POST', '/api/security/groups', token, 'creating the security group',
    { data: { userIds: [], name: user.groupName } });
  const groups = await call(request, 'POST', '/api/security/groups/index', token, 'listing security groups',
    { data: { sort: 'Id', nameFilter: user.groupName, pageIndex: 0, pageSize: 100, isSortDsc: false, offset: 0 } });
  const groupId: number = (groups?.model?.entities ?? []).find((g: any) => g.groupName === user.groupName)?.id ?? 0;
  expect(groupId, `id of security group ${user.groupName}`).toBeGreaterThan(0);

  const permissionTypes = (await call(request, 'GET', `/api/security/permissions/${groupId}`, token, 'reading group permissions'))
    ?.model?.permissionTypes ?? [];
  const permissions = permissionTypes.flatMap((pt: any) => (pt.permissions ?? [])
    .map((p: any) => ({ ...p, isEnabled: p.isEnabled || user.coreClaims.includes(p.claimName) })));
  await call(request, 'PUT', '/api/security/permissions', token, 'granting the core claims', { data: { groupId, permissions } });
  await call(request, 'PUT', '/api/security/groups/settings', token, 'setting the landing page',
    { data: { id: groupId, redirectLink: user.redirectLink } });

  const plugins = await call(request, 'GET',
    '/api/plugins-management/installed?sort=id&isSortDsc=true&pageSize=1000&pageIndex=0&offset=0', token, 'listing plugins');
  for (const [pluginId, wanted] of Object.entries(user.pluginClaims)) {
    const plugin = (plugins?.model?.pluginsList ?? []).find((p: any) => p.pluginId === pluginId);
    expect(plugin, `plugin ${pluginId} is installed`).toBeTruthy();
    // Every permission the plugin defines, independent of any group (PluginPermissionsManager.GetPluginPermissions).
    const defined = await call(request, 'GET', `/api/plugins-permissions/${plugin.id}`, token, `listing ${pluginId} permissions`);
    const permissionIds = new Map<string, number>(
      (defined?.model ?? []).map((perm: any) => [perm.claimName, perm.permissionId] as [string, number]));
    const granted = wanted.map(claimName => {
      const permissionId = permissionIds.get(claimName);
      expect(permissionId, `permission id of ${pluginId} claim ${claimName} (defined: ${[...permissionIds.keys()].join(', ')})`)
        .toBeGreaterThan(0);
      return { isEnabled: true, claimName, permissionId, permissionName: claimName };
    });
    await call(request, 'PUT', `/api/plugins-permissions/group-permissions/${plugin.id}`, token, `granting ${pluginId} claims`,
      { data: [{ groupId, permissions: granted }] });
  }

  await callOk(request, 'POST', '/api/admin/create-user', token, `creating the user ${user.email}`, {
    data: {
      id: 0, firstName: user.firstName, lastName: user.lastName, userName: user.email, email: user.email,
      password: user.password, passwordConfimation: user.password, role: 'user', groupId: groupId, isDeviceUser: false,
    },
  });
  return groupId;
}
