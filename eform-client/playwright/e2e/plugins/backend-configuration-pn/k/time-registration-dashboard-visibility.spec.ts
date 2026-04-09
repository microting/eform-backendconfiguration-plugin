import { test, expect, Page } from '@playwright/test';
import { Navbar } from '../../../Page objects/Navbar.page';
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

async function getAuthToken(page: Page): Promise<string> {
  return page.evaluate(() => {
    const raw = localStorage.getItem('token');
    if (!raw) return '';
    const parsed = JSON.parse(raw);
    return parsed?.token?.accessToken || '';
  });
}

/**
 * Replicates what deploy_and_configure.py does: create security groups,
 * set redirect links, and set plugin permissions via the API.
 */
async function setupSecurityGroupsViaApi(page: Page): Promise<void> {
  const token = await getAuthToken(page);
  const headers = { 'Authorization': `Bearer ${token}`, 'Content-Type': 'application/json' };

  // Step 1: Create "Kun tid" security group via API
  let createRes = await page.request.post(`${BASE_URL}/api/security/groups`, {
    headers, data: { userIds: [], name: 'Kun tid' }
  });
  console.log(`Create 'Kun tid' group: status=${createRes.status()}`);

  // Step 2: Create "Kun arkiv" security group via API
  createRes = await page.request.post(`${BASE_URL}/api/security/groups`, {
    headers, data: { userIds: [], name: 'Kun arkiv' }
  });
  console.log(`Create 'Kun arkiv' group: status=${createRes.status()}`);

  // Step 3: Fetch groups to get their IDs
  const indexRes = await page.request.post(`${BASE_URL}/api/security/groups/index`, {
    headers, data: { sort: 'Id', nameFilter: '', pageIndex: 0, pageSize: 10000, isSortDsc: false, offset: 0 }
  });
  const indexJson = await indexRes.json();
  const groups = indexJson?.model?.entities || [];
  console.log(`Security groups found: ${groups.map((g: any) => `${g.groupName}(id=${g.id})`).join(', ')}`);

  let kunTidId = 0;
  let kunArkivId = 0;
  for (const g of groups) {
    if (g.groupName === 'Kun tid') kunTidId = g.id;
    if (g.groupName === 'Kun arkiv') kunArkivId = g.id;
  }

  // Step 4: Set redirect links (like deploy_and_configure.py does)
  if (kunTidId > 0) {
    const r = await page.request.put(`${BASE_URL}/api/security/groups/settings`, {
      headers, data: { id: kunTidId, redirectLink: '/plugins/time-planning-pn/planning' }
    });
    console.log(`Set 'Kun tid' redirect: status=${r.status()}`);
  }
  if (kunArkivId > 0) {
    const r = await page.request.put(`${BASE_URL}/api/security/groups/settings`, {
      headers, data: { id: kunArkivId, redirectLink: '/plugins/backend-configuration-pn/files' }
    });
    console.log(`Set 'Kun arkiv' redirect: status=${r.status()}`);
  }

  // Step 5: Set plugin permissions for both groups
  // Find installed plugins
  const pluginsRes = await page.request.get(
    `${BASE_URL}/api/plugins-management/installed?sort=id&isSortDsc=true&pageSize=1000&pageIndex=0&offset=0`,
    { headers }
  );
  const pluginsJson = await pluginsRes.json();
  const plugins = pluginsJson?.model?.pluginsList || [];

  for (const plugin of plugins) {
    if (plugin.pluginId === 'eform-angular-time-planning-plugin') {
      // Get current permissions to find correct permissionIds
      const permUrl = `${BASE_URL}/api/plugins-permissions/group-permissions/${plugin.id}`;
      const currentPermsRes = await page.request.get(permUrl, { headers });
      const currentPermsJson = await currentPermsRes.json();
      const currentPerms = currentPermsJson?.model || [];

      // Build permission map: claimName -> permissionId
      const permIdMap: Record<string, number> = {};
      for (const gp of currentPerms) {
        for (const perm of gp.permissions || []) {
          permIdMap[perm.claimName] = perm.permissionId;
        }
      }

      const timePlanningPerms = [
        { isEnabled: true, permissionName: 'Access Time Plannings Plugin', claimName: 'time_planning_plugin_access', permissionId: permIdMap['time_planning_plugin_access'] || 1 },
        { isEnabled: true, permissionName: 'Obtain flex', claimName: 'time_planning_flex_get', permissionId: permIdMap['time_planning_flex_get'] || 2 },
        { isEnabled: true, permissionName: 'Obtain working hours', claimName: 'time_planning_working_hours_get', permissionId: permIdMap['time_planning_working_hours_get'] || 3 },
      ];

      // Set for eForm users (group 1) and Kun tid group
      const payload: any[] = [{ permissions: timePlanningPerms, groupId: 1 }];
      if (kunTidId > 0) {
        payload.push({ permissions: timePlanningPerms, groupId: kunTidId });
      }

      const r = await page.request.put(permUrl, { headers, data: payload });
      console.log(`Set time-planning permissions: status=${r.status()}`);
    }

    if (plugin.pluginId === 'eform-backend-configuration-plugin') {
      const permUrl = `${BASE_URL}/api/plugins-permissions/group-permissions/${plugin.id}`;
      const currentPermsRes = await page.request.get(permUrl, { headers });
      const currentPermsJson = await currentPermsRes.json();
      const currentPerms = currentPermsJson?.model || [];

      const permIdMap: Record<string, number> = {};
      for (const gp of currentPerms) {
        for (const perm of gp.permissions || []) {
          permIdMap[perm.claimName] = perm.permissionId;
        }
      }

      const backendPerms = (claims: string[]) => [
        { isEnabled: claims.includes('backend_configuration_plugin_access'), claimName: 'backend_configuration_plugin_access', permissionId: permIdMap['backend_configuration_plugin_access'] || 1, permissionName: 'Access BackendConfiguration Plugin' },
        { isEnabled: claims.includes('properties_get'), claimName: 'properties_get', permissionId: permIdMap['properties_get'] || 3, permissionName: 'Get properties' },
        { isEnabled: claims.includes('time_registration_enable'), claimName: 'time_registration_enable', permissionId: permIdMap['time_registration_enable'] || 8, permissionName: 'Enable time registration' },
        { isEnabled: claims.includes('task_management_enable'), claimName: 'task_management_enable', permissionId: permIdMap['task_management_enable'] || 7, permissionName: 'Enable task management' },
        { isEnabled: claims.includes('document_management_enable'), claimName: 'document_management_enable', permissionId: permIdMap['document_management_enable'] || 6, permissionName: 'Enable document management' },
      ];

      const payload: any[] = [
        { permissions: backendPerms(['backend_configuration_plugin_access', 'properties_get', 'time_registration_enable', 'task_management_enable', 'document_management_enable']), groupId: 1 },
      ];
      if (kunTidId > 0) {
        payload.push({ permissions: backendPerms(['backend_configuration_plugin_access', 'properties_get', 'time_registration_enable', 'task_management_enable', 'document_management_enable']), groupId: kunTidId });
      }

      const r = await page.request.put(permUrl, { headers, data: payload });
      console.log(`Set backend-configuration permissions: status=${r.status()}`);
    }
  }
}

async function loginAs(page: Page, email: string, password: string): Promise<void> {
  const loginBtn = page.locator('#loginBtn');
  await loginBtn.waitFor({ state: 'visible', timeout: 60000 });
  await page.locator('#username').fill(email);
  await page.locator('#password').fill(password);
  // Listen for the login API response
  const loginResponsePromise = page.waitForResponse(
    r => r.url().includes('/api/auth/token') || r.url().includes('/api/account/login'),
    { timeout: 30000 }
  ).catch(() => null);
  await loginBtn.click();
  const loginResponse = await loginResponsePromise;
  if (loginResponse) {
    console.log(`Login ${email}: status=${loginResponse.status()} url=${loginResponse.url()}`);
  } else {
    console.log(`Login ${email}: no auth response captured`);
  }
  await page.waitForTimeout(2000);
  console.log(`Login ${email}: current URL=${page.url()}`);
}

async function loginAsAdmin(page: Page): Promise<void> {
  await loginAs(page, 'admin@admin.com', 'secretpassword');
  await page.locator('#newEFormBtn').waitFor({ state: 'visible', timeout: 120000 });
}

async function loginAsWorker(page: Page, email: string): Promise<void> {
  await loginAs(page, email, WORKER_PASSWORD);
  // Workers with "Kun tid" are redirected to the planning page which has #workingHoursSite
  await page.locator('#workingHoursSite').waitFor({ state: 'visible', timeout: 120000 });
  await page.waitForTimeout(2000);
}

async function logout(page: Page): Promise<void> {
  const navbar = new Navbar(page);
  await navbar.logout();
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

async function countAvailableSites(page: Page): Promise<number> {
  const siteSelector = page.locator('#workingHoursSite');
  await siteSelector.waitFor({ state: 'visible', timeout: 30000 });
  await siteSelector.click();
  await page.waitForTimeout(500);
  const dropdownPanel = page.locator('ng-dropdown-panel');
  await dropdownPanel.waitFor({ state: 'visible', timeout: 10000 });
  const options = dropdownPanel.locator('.ng-option');
  const count = await options.count();
  await page.keyboard.press('Escape');
  await page.waitForTimeout(300);
  return count;
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

    // Replicate deploy_and_configure.py setup: security groups, redirect links, plugin permissions
    await setupSecurityGroupsViaApi(page);

    // Create a property
    await propertiesPage.goToProperties();
    const property: PropertyCreateUpdate = {
      name: propertyName,
      cvrNumber: '1111111',
      chrNumber: rand.substring(0, 6),
      address: 'Test Address 1',
    };
    await propertiesPage.createProperty(property);
    await page.waitForTimeout(1000);

    // Navigate to property workers
    await workersPage.goToPropertyWorkers();
    await page.waitForTimeout(1000);

    // Create tag
    await workersPage.createTag(tagName);
    await page.waitForTimeout(1000);

    // Create Worker A: Manager with tag and managing tag
    const workerA: PropertyWorker = {
      name: managerName,
      surname: managerSurname,
      workerEmail: managerEmail,
      language: 'Dansk',
      properties: [propertyName],
      timeRegistrationEnabled: true,
      enableMobileAccess: true,
      isManager: true,
      managingTags: [tagName],
      tags: [tagName],
    };
    await workersPage.create(workerA);
    await page.waitForTimeout(2000);

    // Create Worker B: Tagged worker (same tag as manager)
    const workerB: PropertyWorker = {
      name: taggedName,
      surname: taggedSurname,
      workerEmail: taggedWorkerEmail,
      language: 'Dansk',
      properties: [propertyName],
      timeRegistrationEnabled: true,
      enableMobileAccess: true,
      tags: [tagName],
    };
    await workersPage.create(workerB);
    await page.waitForTimeout(2000);

    // Create Worker C: Untagged worker
    const workerC: PropertyWorker = {
      name: untaggedName,
      surname: untaggedSurname,
      workerEmail: untaggedWorkerEmail,
      language: 'Dansk',
      properties: [propertyName],
      timeRegistrationEnabled: true,
      enableMobileAccess: true,
    };
    await workersPage.create(workerC);
    await page.waitForTimeout(2000);

    // Create Worker D: Manager without managing tags
    const workerD: PropertyWorker = {
      name: notagMgrName,
      surname: notagMgrSurname,
      workerEmail: notagMgrEmail,
      language: 'Dansk',
      properties: [propertyName],
      timeRegistrationEnabled: true,
      enableMobileAccess: true,
      isManager: true,
    };
    await workersPage.create(workerD);
    await page.waitForTimeout(2000);

    // ==================== SANITY CHECK: Admin can see planning dashboard ====================

    await navigateToPlannings(page);
    const adminSiteSelector = page.locator('#workingHoursSite');
    await adminSiteSelector.waitFor({ state: 'visible', timeout: 30000 });
    console.log('SANITY CHECK: time-planning plugin is active, #workingHoursSite visible');

    const adminSiteNames = await getAvailableSiteNames(page);
    console.log(`SANITY CHECK: Admin sees ${adminSiteNames.length} sites: ${JSON.stringify(adminSiteNames)}`);

    // Admin should see all 4 workers we created
    expect(adminSiteNames.length).toBeGreaterThanOrEqual(4);
    expect(adminSiteNames.some(n => n.includes(managerName) || n.includes(managerSurname))).toBe(true);
    expect(adminSiteNames.some(n => n.includes(taggedName) || n.includes(taggedSurname))).toBe(true);
    expect(adminSiteNames.some(n => n.includes(untaggedName) || n.includes(untaggedSurname))).toBe(true);
    expect(adminSiteNames.some(n => n.includes(notagMgrName) || n.includes(notagMgrSurname))).toBe(true);

    // ==================== PHASE 2: VERIFY AS MANAGER (Worker A) ====================

    await logout(page);
    await loginAsWorker(page, managerEmail);
    await navigateToPlannings(page);

    const managerSiteCount = await countAvailableSites(page);
    expect(managerSiteCount).toBe(2);

    const managerSiteNames = await getAvailableSiteNames(page);
    expect(managerSiteNames.some(n => n.includes(managerName) || n.includes(managerSurname))).toBe(true);
    expect(managerSiteNames.some(n => n.includes(taggedName) || n.includes(taggedSurname))).toBe(true);
    expect(managerSiteNames.some(n => n.includes(untaggedName))).toBe(false);
    expect(managerSiteNames.some(n => n.includes(notagMgrName))).toBe(false);

    // ==================== PHASE 3: VERIFY AS TAGGED WORKER (Worker B) ====================

    await logout(page);
    await loginAsWorker(page, taggedWorkerEmail);
    await navigateToPlannings(page);

    const taggedSiteCount = await countAvailableSites(page);
    expect(taggedSiteCount).toBe(1);

    const taggedSiteNames = await getAvailableSiteNames(page);
    expect(taggedSiteNames.some(n => n.includes(taggedName) || n.includes(taggedSurname))).toBe(true);

    // ==================== PHASE 4: VERIFY AS UNTAGGED WORKER (Worker C) ====================

    await logout(page);
    await loginAsWorker(page, untaggedWorkerEmail);
    await navigateToPlannings(page);

    const untaggedSiteCount = await countAvailableSites(page);
    expect(untaggedSiteCount).toBe(1);

    const untaggedSiteNames = await getAvailableSiteNames(page);
    expect(untaggedSiteNames.some(n => n.includes(untaggedName) || n.includes(untaggedSurname))).toBe(true);

    // ==================== PHASE 5: VERIFY AS MANAGER WITHOUT TAGS (Worker D) ====================

    await logout(page);
    await loginAsWorker(page, notagMgrEmail);
    await navigateToPlannings(page);

    const notagMgrSiteCount = await countAvailableSites(page);
    expect(notagMgrSiteCount).toBe(1);

    const notagMgrSiteNames = await getAvailableSiteNames(page);
    expect(notagMgrSiteNames.some(n => n.includes(notagMgrName) || n.includes(notagMgrSurname))).toBe(true);

    // ==================== PHASE 6: CLEANUP (as admin) ====================

    await logout(page);
    await loginAsAdmin(page);

    // Delete workers
    await workersPage.goToPropertyWorkers();
    await page.waitForTimeout(1000);
    await workersPage.clearTable();
    await page.waitForTimeout(1000);

    // Delete property
    await propertiesPage.goToProperties();
    await page.waitForTimeout(1000);
    await propertiesPage.clearTable();
    await page.waitForTimeout(1000);

    // Delete tag
    await workersPage.goToPropertyWorkers();
    await page.waitForTimeout(1000);
    await workersPage.deleteTag(tagName);
  });
});
