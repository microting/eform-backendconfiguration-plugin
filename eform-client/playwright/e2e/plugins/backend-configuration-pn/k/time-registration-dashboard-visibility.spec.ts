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
