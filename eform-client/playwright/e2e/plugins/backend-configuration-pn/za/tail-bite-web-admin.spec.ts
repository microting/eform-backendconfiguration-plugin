import { expect, test } from '@playwright/test';
import * as fs from 'fs';
import LoginConstants from '../../../Constants/LoginConstants';
import { generateRandmString } from '../../../helper-functions';
import { LoginPage } from '../../../Page objects/Login.page';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import { BackendConfigurationPropertyWorkersPage, PropertyWorker } from '../BackendConfigurationPropertyWorkers.page';
import { customerDatabase, runMariadbSql } from '../db-helpers';
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';
import { TailBitePage } from './tail-bite.page';

/**
 * Tail biting (halebid) web admin, sub-project 3 of spec 2026-10-04-halebid-app-design.
 *
 * Every tail-bite write needs the caller to be a WORKER on the property (spec §7.1): the web user is mapped
 * to an SDK worker by email (GrpcSiteResolver). The admin account cannot be given a worker through the UI
 * (CreateDeviceUser refuses to link a worker to an admin login), so the seed creates a worker with an
 * invented address and then points that SDK worker's email at the admin login with one UPDATE. That is
 * a state no API produces, which is what db-helpers.ts exists for.
 *
 * Shard `za` does not load the shard-a dump, so the suite is serial and seeds what it needs. Outbreaks are
 * only opened by registrations from the app (gRPC); the outbreak tests insert one registration pair and
 * one outbreak directly, the same way.
 */
const BASE_URL = 'http://localhost:4200';
const rand = generateRandmString(6);
const property: PropertyCreateUpdate = {
  name: `Halebid ${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};
const manager: PropertyWorker = {
  name: `Jane${rand}`,
  surname: 'Doe',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: `jane.doe.${rand}@example.org`,
};
const managerName = `${manager.name} ${manager.surname}`;

test.describe.serial('Tail biting web admin', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto(BASE_URL);
    await new LoginPage(page).login();
  });

  test('seed: property, a worker on it, and the admin login mapped to that worker', async ({ page }) => {
    test.setTimeout(600000);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(manager);
    await expect(workersPage.workerRow(managerName)).toHaveCount(1);

    // generateRandmString is a GUID slice (hex and hyphens), so the address is safe inside the SQL string.
    expect(manager.workerEmail).toMatch(/^[a-z0-9.@-]+$/);
    const sdk = customerDatabase('SDK');
    await runMariadbSql(
      `UPDATE Workers SET Email = '${LoginConstants.username}' WHERE Email = '${manager.workerEmail}' AND WorkflowState <> 'removed'`,
      'map the admin login to the seeded worker', sdk);
    const count = await runMariadbSql(
      `SELECT COUNT(*) FROM Workers WHERE Email = '${LoginConstants.username}' AND WorkflowState <> 'removed'`,
      'count workers with the admin email', sdk);
    // More than one would make every tail-bite call refuse the caller as ambiguous (spec §7.1).
    expect(count.trim()).toBe('1');
  });

  test('managers: enable tail biting on the property and make the worker a manager', async ({ page }) => {
    test.setTimeout(300000);
    const tailBite = new TailBitePage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await page.locator('#tailBiteManagersBtn').click();
    await tailBite.pick('tailBiteManagersProperty', property.name);
    await expect(page.locator('#tailBiteNotEnabled')).toBeVisible({ timeout: UI_TIMEOUT });
    await tailBite.expectApi('POST', /^\/properties\/\d+\/enable$/, () => page.locator('#tailBiteEnableBtn').click());

    const row = page.locator('[id^="tailBiteManagerRow-"]').filter({ hasText: managerName });
    await expect(row).toHaveCount(1, { timeout: API_TIMEOUT });
    await tailBite.expectApi('PUT', /^\/property-workers\/\d+\/manager$/, () => row.locator('mat-slide-toggle button').click());
    await expect(row.locator('mat-slide-toggle button[role="switch"]')).toHaveAttribute('aria-checked', 'true', { timeout: UI_TIMEOUT });
    await tailBite.screenshot('managers-dialog');
    await page.locator('#tailBiteManagersCloseBtn').click();
  });

  test('locations: build the tree, a pen range and a pig count, rename, delete and print QR labels', async ({ page }) => {
    test.setTimeout(300000);
    const tailBite = new TailBitePage(page);
    await tailBite.goto('locations', property.name);
    // Enabling created the root, named after the property.
    await expect(tailBite.locationRow(property.name)).toHaveCount(1, { timeout: API_TIMEOUT });

    await tailBite.selectLocation(property.name);
    await page.locator('#tailBiteAddChildBtn').click();
    await tailBite.expectApi('POST', /^\/locations$/, () => tailBite.answerTextDialog('Stald A'));
    await tailBite.selectLocation('Stald A');
    await page.locator('#tailBiteAddChildBtn').click();
    await tailBite.expectApi('POST', /^\/locations$/, () => tailBite.answerTextDialog('Sektion 4'));

    await tailBite.selectLocation('Sektion 4');
    await page.locator('#tailBitePenPrefix').fill('Sti');
    await page.locator('#tailBitePenFrom').fill('301');
    await page.locator('#tailBitePenTo').fill('312');
    await expect(page.locator('#tailBitePenSummary')).toContainText('12');
    await tailBite.expectApi('POST', /^\/locations\/range$/, () => page.locator('#tailBiteCreatePensBtn').click());
    await expect(tailBite.locationRow('Sti 301')).toHaveCount(1, { timeout: API_TIMEOUT });
    await expect(tailBite.locationRow('Sti 312')).toHaveCount(1);

    await tailBite.selectLocation('Sektion 4');
    await page.locator('#tailBitePigCount').fill('360');
    await tailBite.expectApi('PUT', /^\/locations\/\d+\/occupancy$/, () => page.locator('#tailBiteSavePigsBtn').click());
    await expect(tailBite.locationRow('Sektion 4').locator('td.mat-column-pigs')).toContainText('360', { timeout: API_TIMEOUT });
    // Stald A has no count of its own: it shows the sum below it.
    await expect(tailBite.locationRow('Stald A').locator('td.mat-column-pigs')).toContainText('360');

    await tailBite.selectLocation('Sti 312');
    await page.locator('#tailBiteRenameBtn').click();
    await tailBite.expectApi('PUT', /^\/locations\/\d+$/, () => tailBite.answerTextDialog('Sti 312A'));
    await expect(tailBite.locationRow('Sti 312A')).toHaveCount(1, { timeout: API_TIMEOUT });
    await tailBite.selectLocation('Sti 312A');
    await page.locator('#tailBiteDeleteBtn').click();
    await tailBite.expectApi('DELETE', /^\/locations\/\d+$/, () => tailBite.confirm('tailBiteDeleteLocationConfirm'));
    await expect(tailBite.locationRow('Sti 312A')).toHaveCount(0, { timeout: API_TIMEOUT });

    for (const pen of ['Sti 301', 'Sti 302', 'Sti 303']) {
      await tailBite.locationRow(pen).locator('mat-checkbox input[type="checkbox"]').check();
    }
    await tailBite.screenshot('locations');
    const download = page.waitForEvent('download', { timeout: API_TIMEOUT });
    await page.locator('#tailBitePrintQrBtn').click();
    const pdf = await download;
    expect(pdf.suggestedFilename()).toMatch(/^halebid-qr-\d{4}-\d{2}-\d{2}\.pdf$/);
    const bytes = fs.readFileSync(await pdf.path());
    expect(bytes.subarray(0, 5).toString('latin1')).toBe('%PDF-');
  });

  test('rules: the default rule, a new rule with its dry run, and a second version', async ({ page }) => {
    test.setTimeout(300000);
    const tailBite = new TailBitePage(page);
    await tailBite.goto('rules', property.name);
    // Enabling seeded the default rule on the root.
    await expect(page.locator('[id^="tailBiteRuleItem-"]')).toHaveCount(1, { timeout: API_TIMEOUT });

    await page.locator('#tailBiteAddRuleBtn').click();
    await tailBite.pick('tailBiteRuleLocation', 'Stald A');
    await page.locator('#tailBiteRuleMinPigs').fill('3');
    // No registrations yet, so the 90-day dry run opens nothing - but it answers.
    await expect(page.locator('#tailBiteRulePreviewCount')).toContainText('0', { timeout: API_TIMEOUT });
    await tailBite.expectApi('POST', /^\/rules$/, () => page.locator('#tailBiteRuleSaveBtn').click());
    await expect(page.locator('[id^="tailBiteRuleItem-"]')).toHaveCount(2, { timeout: API_TIMEOUT });

    await page.locator('#tailBiteRuleMinPigs').fill('4');
    await tailBite.expectApi('PUT', /^\/rules\/\d+$/, () => page.locator('#tailBiteRuleSaveBtn').click());
    await expect(page.locator('#tailBiteRuleHistory [id^="tailBiteRuleVersion-"]')).toHaveCount(2, { timeout: API_TIMEOUT });
    await tailBite.screenshot('rules');
  });
});
