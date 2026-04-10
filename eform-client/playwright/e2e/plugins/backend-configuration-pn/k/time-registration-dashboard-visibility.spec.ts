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

const WORKER_PASSWORD = 'Replace_me_with_a_proper_password_2024!';
const BASE_URL = 'http://localhost:4200';

async function loginViaApi(page: Page, email: string, password: string): Promise<string> {
  const res = await page.request.post(`${BASE_URL}/api/auth/token`, {
    form: { username: email, password: password, grant_type: 'password' }
  });
  console.log(`loginViaApi ${email}: status=${res.status()}`);
  const json = await res.json();
  return json?.model?.accessToken || '';
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
  await page.waitForTimeout(2000);
  console.log(`Login ${email}: URL=${page.url()}`);
}

async function loginAsAdmin(page: Page): Promise<void> {
  await loginAs(page, 'admin@admin.com', 'secretpassword');
  await page.locator('#newEFormBtn').waitFor({ state: 'visible', timeout: 120000 });
}

async function loginAsWorker(page: Page, email: string): Promise<void> {
  await loginAs(page, email, WORKER_PASSWORD);
  await page.waitForURL('**/plugins/time-planning-pn/planning**', { timeout: 30000 }).catch(() => {
    console.log(`Worker ${email}: did not navigate to planning, URL=${page.url()}`);
  });
  console.log(`Worker ${email}: final URL=${page.url()}`);
  // Wait for the planning page to finish loading (date picker is always visible)
  await page.locator('mat-datepicker-toggle').first().waitFor({ state: 'visible', timeout: 120000 });
  await page.waitForTimeout(2000);
}

async function logout(page: Page): Promise<void> {
  await page.evaluate(() => {
    localStorage.removeItem('auth');
    localStorage.removeItem('token');
  });
  await page.goto(`${BASE_URL}/auth`);
  await page.locator('#loginBtn').waitFor({ state: 'visible', timeout: 60000 });
}

async function navigateToPlannings(page: Page): Promise<void> {
  const timePlanningMenu = page.locator('#time-planning-pn');
  if (!await timePlanningMenu.isVisible()) {
    await page.waitForTimeout(1000);
  }
  const planningBtn = page.locator('#time-planning-pn-planning');
  if (!await planningBtn.isVisible()) {
    await timePlanningMenu.click();
    await page.waitForTimeout(500);
  }
  await planningBtn.click();
  await page.waitForTimeout(2000);
}

async function getAvailableSiteNames(page: Page): Promise<string[]> {
  const siteSelector = page.locator('#workingHoursSite');
  await siteSelector.waitFor({ state: 'visible', timeout: 30000 });
  await siteSelector.click();
  await page.waitForTimeout(500);
  const dropdownPanel = page.locator('ng-dropdown-panel');
  await dropdownPanel.waitFor({ state: 'visible', timeout: 10000 });
  const names = await dropdownPanel.locator('.ng-option').allInnerTexts();
  await page.keyboard.press('Escape');
  await page.waitForTimeout(300);
  return names.map(n => n.trim());
}

test.describe('Time Registration Dashboard Visibility', () => {
  test('should show correct workers based on user role and tags', async ({ page }) => {
    test.setTimeout(600000);

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

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    // ==================== PHASE 1: SETUP (as admin) ====================

    await page.goto('http://localhost:4200');
    await loginAsAdmin(page);
    await setupSecurityGroupsViaApi(page);

    // Create a property
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty({
      name: propertyName, cvrNumber: '1111111',
      chrNumber: rand.substring(0, 6), address: 'Test Address 1',
    });
    await page.waitForTimeout(1000);

    // Navigate to property workers and create tag
    await workersPage.goToPropertyWorkers();
    await page.waitForTimeout(1000);
    await workersPage.createTag(tagName);
    await page.waitForTimeout(1000);

    // Worker A: Manager with tag and managing tag
    await workersPage.create({
      name: managerName, surname: managerSurname, workerEmail: managerEmail,
      language: 'Dansk', properties: [propertyName],
      timeRegistrationEnabled: true, enableMobileAccess: true,
      isManager: true, managingTags: [tagName], tags: [tagName],
    });
    await page.waitForTimeout(2000);

    // Worker B: Tagged worker (same tag as manager)
    await workersPage.create({
      name: taggedName, surname: taggedSurname, workerEmail: taggedWorkerEmail,
      language: 'Dansk', properties: [propertyName],
      timeRegistrationEnabled: true, enableMobileAccess: true, tags: [tagName],
    });
    await page.waitForTimeout(2000);

    // Worker C: Untagged worker
    await workersPage.create({
      name: untaggedName, surname: untaggedSurname, workerEmail: untaggedWorkerEmail,
      language: 'Dansk', properties: [propertyName],
      timeRegistrationEnabled: true, enableMobileAccess: true,
    });
    await page.waitForTimeout(2000);

    // Worker D: Manager without managing tags
    await workersPage.create({
      name: notagMgrName, surname: notagMgrSurname, workerEmail: notagMgrEmail,
      language: 'Dansk', properties: [propertyName],
      timeRegistrationEnabled: true, enableMobileAccess: true, isManager: true,
    });
    await page.waitForTimeout(2000);

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
    await loginAsWorker(page, managerEmail);
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
    await loginAsWorker(page, taggedWorkerEmail);
    expect(page.url()).toContain('/plugins/time-planning-pn/planning');

    // Non-manager should NOT see the dropdown (only 1 site returned, dropdown hidden)
    await expect(page.locator('#workingHoursSite')).not.toBeVisible();
    console.log('Tagged worker: dropdown correctly hidden (single site)');

    // ==================== PHASE 5: UNTAGGED WORKER sees only self, no dropdown ====================

    await logout(page);
    await loginAsWorker(page, untaggedWorkerEmail);
    expect(page.url()).toContain('/plugins/time-planning-pn/planning');

    await expect(page.locator('#workingHoursSite')).not.toBeVisible();
    console.log('Untagged worker: dropdown correctly hidden (single site)');

    // ==================== PHASE 6: MANAGER WITHOUT TAGS sees only self, no dropdown ====================

    await logout(page);
    await loginAsWorker(page, notagMgrEmail);
    expect(page.url()).toContain('/plugins/time-planning-pn/planning');

    await expect(page.locator('#workingHoursSite')).not.toBeVisible();
    console.log('Manager without tags: dropdown correctly hidden (single site)');

    // ==================== PHASE 7: CLEANUP (as admin) ====================

    await logout(page);
    await loginAsAdmin(page);

    await workersPage.goToPropertyWorkers();
    await page.waitForTimeout(1000);
    await workersPage.clearTable();
    await page.waitForTimeout(1000);

    await propertiesPage.goToProperties();
    await page.waitForTimeout(1000);
    await propertiesPage.clearTable();
    await page.waitForTimeout(1000);

    await workersPage.goToPropertyWorkers();
    await page.waitForTimeout(1000);
    await workersPage.deleteTag(tagName);
  });
});
