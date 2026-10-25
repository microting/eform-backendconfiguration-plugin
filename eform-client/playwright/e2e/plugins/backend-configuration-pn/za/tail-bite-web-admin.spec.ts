import { expect, Page, test } from '@playwright/test';
import * as fs from 'fs';
import LoginConstants from '../../../Constants/LoginConstants';
import { generateRandmString } from '../../../helper-functions';
import { LoginPage } from '../../../Page objects/Login.page';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import { BackendConfigurationPropertyWorkersPage, PropertyWorker } from '../BackendConfigurationPropertyWorkers.page';
import { customerDatabase, runMariadbSql } from '../db-helpers';
import { API_TIMEOUT, ignoreUnhandledRejections, UI_TIMEOUT, waitForApiResponse } from '../wait-helpers';
import { escapeRegExp, TailBitePage } from './tail-bite.page';

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
const API = `${BASE_URL}/api/backend-configuration-pn/tail-bite`;
// The same budget LoginPage.login() gives the app to bootstrap and land.
const APP_LOAD_TIMEOUT = 120000;
// Second admin for the #1425 first-user check. Password generated at runtime ("Aa1" satisfies the Identity policy).
const secondAdminEmail = `second-admin-${rand}@example.org`;
const secondAdminPassword = `Aa1${generateRandmString(12)}`;

// Set by the outbreak seed test, read by the outbreak tests after it (serial suite, one worker).
let outbreakId = 0;
let seededPropertyId = 0;
const registrationIds: number[] = [0, 0];

async function apiToken(page: Page): Promise<string> {
  const res = await page.request.post(`${BASE_URL}/api/auth/token`, {
    form: { username: LoginConstants.username, password: LoginConstants.password, grant_type: 'password' },
  });
  return (await res.json())?.model?.accessToken ?? '';
}

async function apiGet<T>(page: Page, path: string): Promise<T> {
  const res = await page.request.get(`${API}/${path}`, { headers: { Authorization: `Bearer ${await apiToken(page)}` } });
  const body = await res.json();
  expect(body.success, `GET ${path} -> ${body.message}`).toBe(true);
  return body.model as T;
}

function int(value: unknown, what: string): number {
  expect(Number.isInteger(value), `${what} is an integer`).toBe(true);
  return value as number;
}

async function tailBitePropertyId(page: Page): Promise<number> {
  const properties = await apiGet<{ propertyId: number; name: string }[]>(page, 'properties');
  return int(properties.find((p) => p.name === property.name)?.propertyId, 'property id');
}

test.describe.serial('Tail biting web admin', () => {
  // Browser errors are printed when a test fails, so a broken page shows its cause in the CI log.
  let browserErrors: string[] = [];

  test.beforeEach(async ({ page }) => {
    browserErrors = [];
    page.on('pageerror', (error) => browserErrors.push(`pageerror: ${error.message}`));
    page.on('console', (message) => {
      if (message.type() === 'error') {
        browserErrors.push(`console: ${message.text()}`);
      }
    });
    await page.goto(BASE_URL);
    await new LoginPage(page).login();
  });

  // Runs even when the second-admin test fails early; failures are logged, never thrown.
  test.afterAll(async ({ browser }) => {
    test.setTimeout(180000);
    const page = await browser.newPage();
    try {
      await page.goto(BASE_URL, { timeout: APP_LOAD_TIMEOUT });
      await new LoginPage(page).login();
      const headers = { Authorization: `Bearer ${await apiToken(page)}`, 'Content-Type': 'application/json' };
      const users = await page.request.post(`${BASE_URL}/api/admin/get-users`, {
        headers, data: { sort: 'Id', isSortDsc: false, pageIndex: 0, pageSize: 1000, offset: 0 }, timeout: API_TIMEOUT,
      });
      const body = await users.json().catch(() => null);
      // A failed lookup must not read as "the user is absent".
      if (!users.ok() || !body?.success) {
        throw new Error(`get-users failed (HTTP ${users.status()})`);
      }
      const created = (body.model?.entities ?? []).find((u: any) => u.email === secondAdminEmail);
      if (created) {
        const deleted = await page.request.get(`${BASE_URL}/api/admin/delete-user/${created.id}`, { headers, timeout: API_TIMEOUT });
        // The API can answer HTTP 200 with success:false.
        if (!deleted.ok() || !(await deleted.json().catch(() => null))?.success) {
          throw new Error(`delete-user failed (HTTP ${deleted.status()})`);
        }
      }
    } catch (err: any) {
      console.log(`[tail-bite-web-admin] afterAll cleanup of ${secondAdminEmail} failed (non-fatal): ${err?.message ?? err}`);
    } finally {
      await page.close().catch(() => undefined);
    }
  });

  test.afterEach(async ({}, testInfo) => {
    if (testInfo.status !== testInfo.expectedStatus && browserErrors.length > 0) {
      console.log(`Browser errors in "${testInfo.title}":\n${browserErrors.join('\n')}`);
      await testInfo.attach('browser-errors', { body: browserErrors.join('\n'), contentType: 'text/plain' });
    }
  });

  test('seed: property, a worker on it, and the admin login mapped to that worker', async ({ page }) => {
    // Budget: one property create and one SDK-backed worker create (each wait bounded by API_TIMEOUT /
    // SLOW_API_TIMEOUT in the page objects) plus two SQL statements. CI ran the whole za suite in under a minute
    // (seed ~15 s), so 3 minutes covers a slow SDK round-trip with room to spare and still fails fast on a hang.
    test.setTimeout(180000);
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

  // #1477: tail biting is switched on with the Halebid toggle in the property edit dialog, not in the managers dialog.
  test('managers: switch tail biting on in the property edit dialog and make the worker a manager', async ({ page }) => {
    test.setTimeout(300000);
    const tailBite = new TailBitePage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await page.locator('#tailBiteManagersBtn').click();
    await tailBite.pick('tailBiteManagersProperty', property.name);
    await expect(page.locator('#tailBiteNotEnabled')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(page.locator('#tailBiteEnableBtn')).toHaveCount(0);
    await page.locator('#tailBiteManagersCloseBtn').click();

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    const propertyRow = propertiesPage.getRowObjectByName(property.name);
    await propertyRow.setTailBite(true);
    await propertyRow.openEditModal();
    await expect(propertiesPage.propertyEditTailBiteEnableToggle()).toHaveAttribute('aria-checked', 'true', { timeout: UI_TIMEOUT });
    await propertiesPage.propertyEditSaveCancelBtn().click();
    await propertiesPage.propertyEditSaveCancelBtn().waitFor({ state: 'hidden', timeout: UI_TIMEOUT });

    await workersPage.goToPropertyWorkers();
    await page.locator('#tailBiteManagersBtn').click();
    await tailBite.pick('tailBiteManagersProperty', property.name);
    await expect(page.locator('#tailBiteNotEnabled')).toHaveCount(0, { timeout: UI_TIMEOUT });
    const row = page.locator('[id^="tailBiteManagerRow-"]').filter({ hasText: managerName });
    await expect(row).toHaveCount(1, { timeout: API_TIMEOUT });
    await tailBite.expectApi('PUT', /^\/property-workers\/\d+\/manager$/, () => row.locator('mat-slide-toggle button').click());
    await expect(row.locator('mat-slide-toggle button[role="switch"]')).toHaveAttribute('aria-checked', 'true', { timeout: UI_TIMEOUT });
    await tailBite.screenshot('managers-dialog');
    await page.locator('#tailBiteManagersCloseBtn').click();
  });

  // #1425: the managers dialog is first-user only for now. A second admin holds DeviceUsers.Update but is not
  // the first user, so the button must be absent while the property workers page itself is loaded.
  test('managers: an admin who is not the first user is not offered Tail-bite managers', async ({ page, browser }) => {
    test.setTimeout(300000);
    const headers = { Authorization: `Bearer ${await apiToken(page)}`, 'Content-Type': 'application/json' };
    const createRes = await page.request.post(`${BASE_URL}/api/admin/create-user`, {
      headers,
      data: {
        id: 0, firstName: `Second${rand}`, lastName: 'NotFirst', userName: secondAdminEmail, email: secondAdminEmail,
        password: secondAdminPassword, passwordConfimation: secondAdminPassword, role: 'admin', groupId: 0, isDeviceUser: false,
      },
      timeout: API_TIMEOUT,
    });
    expect((await createRes.json().catch(() => null))?.success, 'creating the second admin must succeed').toBe(true);

    const context = await browser.newContext();
    try {
      const secondPage = await context.newPage();
      await secondPage.goto(BASE_URL, { timeout: APP_LOAD_TIMEOUT });
      await secondPage.locator('#loginBtn').waitFor({ state: 'visible', timeout: APP_LOAD_TIMEOUT });
      await secondPage.locator('#username').fill(secondAdminEmail);
      await secondPage.locator('#password').fill(secondAdminPassword);
      await secondPage.locator('#loginBtn').click();
      await secondPage.locator('#newEFormBtn').waitFor({ state: 'visible', timeout: APP_LOAD_TIMEOUT });

      await new BackendConfigurationPropertyWorkersPage(secondPage).goToPropertyWorkers();
      // Gated by the same Update claim as the managers button, so its presence proves the claims are loaded.
      await expect(secondPage.locator('#bulkWorkerTagsBtn')).toBeVisible({ timeout: UI_TIMEOUT });
      await expect(secondPage.locator('#tailBiteManagersBtn')).toHaveCount(0, { timeout: UI_TIMEOUT });
    } finally {
      await context.close();
    }
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

  test('action types: add, rename and delete', async ({ page }) => {
    test.setTimeout(300000);
    const tailBite = new TailBitePage(page);
    await tailBite.goto('action-types', property.name);
    const rows = page.locator('[id^="tailBiteActionTypeRow-"]');
    // Enabling seeded the default list (TailBiteDefaults.ActionTypes).
    await expect(rows).toHaveCount(7, { timeout: API_TIMEOUT });

    await page.locator('#tailBiteNewActionTypeName').fill('Ekstra rodemateriale');
    await tailBite.expectApi('POST', /^\/properties\/\d+\/action-types$/, () => page.locator('#tailBiteAddActionTypeBtn').click());
    await expect(rows).toHaveCount(8, { timeout: API_TIMEOUT });

    const added = rows.filter({ hasText: 'Ekstra rodemateriale' });
    await added.locator('[id^="tailBiteActionTypeRename-"]').click();
    await tailBite.expectApi('PUT', /^\/action-types\/\d+$/, () => tailBite.answerTextDialog('Ekstra halm og reb'));
    const renamed = rows.filter({ hasText: 'Ekstra halm og reb' });
    await expect(renamed).toHaveCount(1, { timeout: API_TIMEOUT });

    await renamed.locator('[id^="tailBiteActionTypeDelete-"]').click();
    await tailBite.expectApi('DELETE', /^\/action-types\/\d+$/, () => tailBite.confirm('tailBiteActionTypeDeleteConfirm'));
    await expect(rows).toHaveCount(7, { timeout: API_TIMEOUT });
  });

  test('outbreaks: a seeded outbreak is listed as needing an assessment', async ({ page }) => {
    test.setTimeout(300000);
    const propertyId = await tailBitePropertyId(page);
    seededPropertyId = propertyId;
    const tree = await apiGet<{ locations: { id: number; name: string; removed: boolean }[] }>(page, `properties/${propertyId}/tree`);
    const idOf = (name: string) => int(tree.locations.find((l) => l.name === name && !l.removed)?.id, `location ${name}`);
    const rules = await apiGet<{ id: number; locationId: number; version: number }[]>(page, `properties/${propertyId}/rules`);
    const stableRule = rules.find((r) => r.locationId === idOf('Stald A'));
    // The outbreak page's manager-checked list (the admin login is a manager since the managers test).
    const workers = await apiGet<{ siteId: number; name: string }[]>(page, `properties/${propertyId}/assignable-workers`);
    const siteId = int(workers.find((w) => w.name === managerName)?.siteId, 'manager site id');
    const [ruleId, ruleVersion, stald, pen301, pen302] =
      [int(stableRule?.id, 'rule id'), int(stableRule?.version, 'rule version'), idOf('Stald A'), idOf('Sti 301'), idOf('Sti 302')];

    // Two registrations in Stald A, linked to one open outbreak summed at Stald A (the rule from the rules test).
    const base = "@now, @now, 'created', 0, 0, 1";
    const out = await runMariadbSql(`
      SET @now = UTC_TIMESTAMP(6);
      INSERT INTO TailBiteRegistrations (PropertyId, SiteId, RegisteredAt, ReceivedAt, EffectiveAt, ClientUuid, CreatedAt, UpdatedAt, WorkflowState, CreatedByUserId, UpdatedByUserId, Version)
        VALUES (${propertyId}, ${siteId}, @now - INTERVAL 2 DAY, @now - INTERVAL 2 DAY, @now - INTERVAL 2 DAY, UUID(), ${base});
      SET @r1 = LAST_INSERT_ID();
      INSERT INTO TailBiteRegistrationLocations (RegistrationId, LocationId, MinorCount, SevereCount, CountUnknown, CreatedAt, UpdatedAt, WorkflowState, CreatedByUserId, UpdatedByUserId, Version)
        VALUES (@r1, ${pen301}, 2, 0, 0, ${base});
      SET @l1 = LAST_INSERT_ID();
      INSERT INTO TailBiteRegistrations (PropertyId, SiteId, RegisteredAt, ReceivedAt, EffectiveAt, ClientUuid, CreatedAt, UpdatedAt, WorkflowState, CreatedByUserId, UpdatedByUserId, Version)
        VALUES (${propertyId}, ${siteId}, @now - INTERVAL 1 HOUR, @now - INTERVAL 1 HOUR, @now - INTERVAL 1 HOUR, UUID(), ${base});
      SET @r2 = LAST_INSERT_ID();
      INSERT INTO TailBiteRegistrationLocations (RegistrationId, LocationId, MinorCount, SevereCount, CountUnknown, CreatedAt, UpdatedAt, WorkflowState, CreatedByUserId, UpdatedByUserId, Version)
        VALUES (@r2, ${pen302}, 2, 1, 0, ${base});
      SET @l2 = LAST_INSERT_ID();
      INSERT INTO TailBiteOutbreaks (PropertyId, LocationId, RuleId, RuleVersion, OpenedAt, OpenedByRegistrationId, CreatedAt, UpdatedAt, WorkflowState, CreatedByUserId, UpdatedByUserId, Version)
        VALUES (${propertyId}, ${stald}, ${ruleId}, ${ruleVersion}, @now - INTERVAL 1 HOUR, @r2, ${base});
      SET @o = LAST_INSERT_ID();
      INSERT INTO TailBiteOutbreakLinks (OutbreakId, RegistrationLocationId, CreatedAt, UpdatedAt, WorkflowState, CreatedByUserId, UpdatedByUserId, Version)
        VALUES (@o, @l1, ${base}), (@o, @l2, ${base});
      SELECT @o, @r1, @r2;`, 'seed two registrations and an open outbreak', customerDatabase('eform-backend-configuration-plugin'));
    [outbreakId, registrationIds[0], registrationIds[1]] = out.trim().split('\t').map((v) => int(Number(v), 'seeded id'));

    const tailBite = new TailBitePage(page);
    await tailBite.goto('outbreaks', property.name);
    await expect(page.locator(`#tailBiteOutbreakRow-${outbreakId}`)).toContainText('Stald A', { timeout: API_TIMEOUT });
    await expect(page.locator(`#tailBiteOutbreakStatus-${outbreakId}`)).toHaveClass(/badge-error/);
    await tailBite.screenshot('outbreaks');
  });

  test('outbreak: the registrations behind it, and cancelling one with a reason', async ({ page }) => {
    test.setTimeout(300000);
    const tailBite = new TailBitePage(page);
    await tailBite.goto('outbreaks', property.name);
    await page.locator(`#tailBiteOutbreakLink-${outbreakId}`).click();
    await expect(page.locator('#tailBiteOutbreakTitle')).toHaveText('Stald A', { timeout: API_TIMEOUT });
    await expect(page.locator('#tailBiteRegistrationsTable tr[id^="tailBiteRegRow-"]')).toHaveCount(2);
    await expect(page.locator('#tailBiteOutbreakSummaryLine')).toContainText(/5 (bitten pigs|bidte grise)/);
    await expect(page.locator('#tailBiteRegistrationsTable')).toContainText(managerName);

    await page.locator(`#tailBiteCancelReg-${registrationIds[0]}`).click();
    await tailBite.expectApi('PUT', /^\/registrations\/\d+\/cancel$/, () => tailBite.answerTextDialog('Registreret på forkert sti'));
    await expect(page.locator(`#tailBiteRegCancelled-${registrationIds[0]}`)).toBeVisible({ timeout: API_TIMEOUT });
    // The cancelled registration (2 minor) no longer counts: 3 bitten pigs remain.
    await expect(page.locator('#tailBiteOutbreakSummaryLine')).toContainText(/3 (bitten pigs|bidte grise)/, { timeout: API_TIMEOUT });
  });

  test('outbreak: the risk assessment needs all six answers and an action for every yes', async ({ page }) => {
    test.setTimeout(300000);
    const tailBite = new TailBitePage(page);
    // A link that names another property id is corrected to the outbreak's own property.
    await page.goto(`${BASE_URL}/plugins/backend-configuration-pn/tail-bite/${seededPropertyId + 1000}/outbreaks/${outbreakId}`);
    await expect(page.locator('#tailBiteOutbreakTitle')).toHaveText('Stald A', { timeout: API_TIMEOUT });
    await expect(page).toHaveURL(new RegExp(`/tail-bite/${seededPropertyId}/outbreaks/${outbreakId}$`));

    // Before the assessment the close button is disabled, and says why.
    await expect(page.locator('#tailBiteCloseBtn')).toBeDisabled();
    await expect(page.locator('#tailBiteCloseReason')).toBeVisible();

    // Feed (factor 1) yes, the other five no.
    await page.locator('#tailBiteFactor-1-yes').click();
    for (const factor of [0, 2, 3, 4, 5]) {
      await page.locator(`#tailBiteFactor-${factor}-no`).click();
    }
    let assessmentSent = false;
    page.on('request', (r) => { if (r.method() === 'PUT' && r.url().includes('/assessment')) assessmentSent = true; });
    await page.locator('#tailBiteSaveAssessmentBtn').click();
    await expect(page.locator('#tailBiteAssessmentError-1')).toBeVisible({ timeout: UI_TIMEOUT });
    expect(assessmentSent).toBe(false);

    await page.locator('#tailBiteNewAction-1-0-description').fill('Kontrollér foderautomaten');
    await tailBite.pick('tailBiteNewAction-1-0-responsible', managerName);
    await page.locator('#tailBiteNewAction-1-0-dateToggle button').click();
    await page.locator('.mat-calendar-body-today').click();
    await tailBite.expectApi('PUT', /^\/outbreaks\/\d+\/assessment$/, () => page.locator('#tailBiteSaveAssessmentBtn').click());
    await expect(page.locator('#tailBiteOutbreakDetailStatus')).toHaveClass(/badge-warning/, { timeout: API_TIMEOUT });
    await expect(page.locator('#tailBiteCloseBtn')).toBeDisabled();
    await tailBite.screenshot('outbreak-detail');
  });

  test('outbreak: marking the follow-up done allows closing; the closed outbreak leaves the open list', async ({ page }) => {
    test.setTimeout(300000);
    const tailBite = new TailBitePage(page);
    await page.goto(`${BASE_URL}/plugins/backend-configuration-pn/tail-bite/${seededPropertyId}/outbreaks/${outbreakId}`);
    const actionRow = page.locator('#tailBiteFollowUpsTable tr[id^="tailBiteActionRow-"]');
    await expect(actionRow).toHaveCount(1, { timeout: API_TIMEOUT });

    await tailBite.expectApi('PUT', /^\/actions\/\d+\/done$/, () => actionRow.locator('[id^="tailBiteActionDone-"]').click());
    await expect(actionRow.locator('[id^="tailBiteActionState-"]')).toHaveClass(/badge-success/, { timeout: API_TIMEOUT });
    await expect(page.locator('#tailBiteCloseBtn')).toBeEnabled();
    await page.locator('#tailBiteCloseBtn').click();
    await tailBite.expectApi('PUT', /^\/outbreaks\/\d+\/close$/, () => tailBite.confirm('tailBiteCloseConfirm'));
    await expect(page.locator('#tailBiteOutbreakDetailStatus')).toHaveClass(/badge-success/, { timeout: API_TIMEOUT });

    await page.locator('#tailBiteBackToOutbreaks').click();
    await expect(page.locator(`#tailBiteOutbreakRow-${outbreakId}`)).toHaveCount(0, { timeout: API_TIMEOUT });
    await page.locator('#tailBiteShowClosed button').click();
    await expect(page.locator(`#tailBiteOutbreakStatus-${outbreakId}`)).toHaveClass(/badge-success/, { timeout: API_TIMEOUT });
  });

  // #1477: switching Halebid off hides the property (its data is kept); switching it on again shows it as it was.
  test('toggle: switched off the property leaves the tail-bite page, switched on it is back with its data', async ({ page }) => {
    test.setTimeout(300000);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.getRowObjectByName(property.name).setTailBite(false);

    const mine = waitForApiResponse(page, 'GET tail-bite my-properties',
      (r) => r.request().method() === 'GET' && new URL(r.url()).pathname.endsWith('/tail-bite/my-properties'), API_TIMEOUT);
    ignoreUnhandledRejections(mine);
    await page.goto(`${BASE_URL}/plugins/backend-configuration-pn/tail-bite`);
    const body = await (await mine).json();
    expect(body.success, `GET my-properties -> ${body.message}`).toBe(true);
    const names: string[] = (body.model ?? []).map((p: { name: string }) => p.name);
    expect(names).not.toContain(property.name);
    if (names.length === 0) {
      await expect(page.locator('#tailBiteNoProperty')).toBeVisible({ timeout: UI_TIMEOUT });
    } else {
      await page.locator('#tailBitePropertySelect').click({ timeout: UI_TIMEOUT });
      const panel = page.locator('.ng-dropdown-panel');
      await panel.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      await expect(panel.locator('.ng-option').filter({ hasText: new RegExp(`^\\s*${escapeRegExp(property.name)}\\s*$`) }))
        .toHaveCount(0);
      await page.keyboard.press('Escape');
    }
    const tailBite = new TailBitePage(page);
    await tailBite.screenshot('switched-off');

    await propertiesPage.goToProperties();
    await propertiesPage.getRowObjectByName(property.name).setTailBite(true);
    await tailBite.goto('locations', property.name);
    await expect(tailBite.locationRow(property.name)).toHaveCount(1, { timeout: API_TIMEOUT });
    await expect(tailBite.locationRow('Sti 301')).toHaveCount(1);
    await tailBite.goto('rules', property.name);
    await expect(page.locator('[id^="tailBiteRuleItem-"]')).toHaveCount(2, { timeout: API_TIMEOUT });
  });
});
