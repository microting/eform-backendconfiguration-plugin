import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationPropertiesPage } from '../BackendConfigurationProperties.page';
import { BackendConfigurationAdhocPage } from '../BackendConfigurationAdhoc.page';

/**
 * Adhoc overblik — table + toolbar filter suite (M5/T1, flow 1).
 *
 * Shard z runs without the shard-a DB dump (`420_SDK.sql`/
 * `420_eform-backend-configuration-plugin.sql`), so - like every other
 * non-`a` shard - it starts from a fresh, empty database and must seed its
 * own data via the UI: two properties, one task per property (created via
 * the "Ny opgave" drawer), then filter/search/column-picker assertions
 * against that seeded pair.
 *
 * Runs as `admin@admin.com` throughout (`LoginPage.login()` default), but
 * NOT because the route requires it: since spec
 * `2026-08-24-adhoc-tasks-unrestricted-access-design.md` the dashboard's
 * `adhoc-tasks` route is plain `canActivate: [AuthGuard]`
 * (`backend-configuration-pn.routing.ts`) - the old `PermissionGuard` +
 * `data.requiredPermission: 'adhoc_enable'` gate is gone, and
 * `AdhocController` grants every web caller full access, so any user of
 * this plugin reaches the page and sees every task customer-wide. The only
 * requirement left is the parent route's
 * `backend_configuration_plugin_access`, which the admin session carries.
 * The non-admin half of that behaviour is covered by
 * `z/adhoc-nonadmin-access.spec.ts`; this suite stays admin-only simply
 * because it seeds its own data.
 */
const BASE_URL = 'http://localhost:4200';
const rand = generateRandmString(8).toLowerCase();

const propA = {
  name: `adhoc-tf-a-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};
const propB = {
  name: `adhoc-tf-b-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '2222222',
};
const taskATitle = `Task-A-${rand}`;
const taskBTitle = `Task-B-${rand}`;

async function login(page: import('@playwright/test').Page): Promise<void> {
  await page.goto(BASE_URL);
  await new LoginPage(page).login();
}

test.describe.serial('Adhoc overblik — table + toolbar filters', () => {
  test('seed: two properties + one task per property', async ({ page }) => {
    test.setTimeout(180000);
    await login(page);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(propA);
    await propertiesPage.createProperty(propB);

    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();

    await adhocPage.openNewTask();
    await adhocPage.selectDrawerProperty(propA.name);
    await adhocPage.drawerTitleInput().fill(taskATitle);
    await adhocPage.saveDrawer(true);
    await expect(adhocPage.row(taskATitle)).toBeVisible({ timeout: 15000 });

    await adhocPage.openNewTask();
    await adhocPage.selectDrawerProperty(propB.name);
    await adhocPage.drawerTitleInput().fill(taskBTitle);
    await adhocPage.saveDrawer(true);
    await expect(adhocPage.row(taskBTitle)).toBeVisible({ timeout: 15000 });

    // Both seeded tasks visible on the default (Åben/open) status filter.
    await expect(adhocPage.row(taskATitle)).toBeVisible();
    await expect(adhocPage.row(taskBTitle)).toBeVisible();
  });

  test('search narrows the row set to the matching task', async ({ page }) => {
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();

    await adhocPage.search(taskATitle);
    await expect(adhocPage.row(taskATitle)).toBeVisible();
    await expect(adhocPage.row(taskBTitle)).toHaveCount(0);

    await adhocPage.search(taskBTitle);
    await expect(adhocPage.row(taskBTitle)).toBeVisible();
    await expect(adhocPage.row(taskATitle)).toHaveCount(0);

    await adhocPage.search('');
    await expect(adhocPage.row(taskATitle)).toBeVisible();
    await expect(adhocPage.row(taskBTitle)).toBeVisible();
  });

  test('property filter narrows the row set to the selected property', async ({ page }) => {
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();

    await adhocPage.selectPropertyFilter(propA.name);
    await expect(adhocPage.row(taskATitle)).toBeVisible();
    await expect(adhocPage.row(taskBTitle)).toHaveCount(0);

    await adhocPage.selectPropertyFilter(propB.name);
    await expect(adhocPage.row(taskBTitle)).toBeVisible();
    await expect(adhocPage.row(taskATitle)).toHaveCount(0);
  });

  test('status filter changes the row set (Åben vs Løste)', async ({ page }) => {
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();

    // Neither seeded task is completed yet, so the "Løste" (Solved) status
    // tab must be empty while both remain visible on "Åben" (Open, default).
    await adhocPage.selectStatusFilter('Løste');
    await expect(adhocPage.row(taskATitle)).toHaveCount(0);
    await expect(adhocPage.row(taskBTitle)).toHaveCount(0);

    await adhocPage.selectStatusFilter('Åben');
    await expect(adhocPage.row(taskATitle)).toBeVisible();
    await expect(adhocPage.row(taskBTitle)).toBeVisible();
  });

  test('column picker toggles a column header off', async ({ page }) => {
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();

    // The picker is only enabled on the "Åben" status (adhoc-table.component.html
    // `[disabled]="status !== 'open'"`) - default filter state satisfies this.
    await expect(adhocPage.columnHeader('propertyName')).toBeVisible();

    await adhocPage.openColumnPicker();
    await adhocPage.toggleColumnByLabel('Ejendom');
    await adhocPage.closeColumnPicker();

    await expect(adhocPage.columnHeader('propertyName')).toHaveCount(0);
  });
});
