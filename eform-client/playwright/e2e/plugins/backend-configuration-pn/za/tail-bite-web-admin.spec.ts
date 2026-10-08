import { expect, test } from '@playwright/test';
import LoginConstants from '../../../Constants/LoginConstants';
import { generateRandmString } from '../../../helper-functions';
import { LoginPage } from '../../../Page objects/Login.page';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import { BackendConfigurationPropertyWorkersPage, PropertyWorker } from '../BackendConfigurationPropertyWorkers.page';
import { customerDatabase, runMariadbSql } from '../db-helpers';

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
});
