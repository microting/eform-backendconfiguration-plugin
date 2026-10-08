import { expect, test } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';
import { UI_TIMEOUT } from '../wait-helpers';

// An e-mail copied from a mail client or phone can carry invisible characters,
// e.g. U+200E (left-to-right mark) in front of the address. The address doubles as
// the worker's login name, and the login was refused because of the mark, so the
// worker could never get a password. The dialog's e-mail field strips such
// characters as they are typed or pasted, and the saved worker keeps the visible
// address only.

const rand = generateRandmString(4);
const cleanEmail = `${generateRandmString(6).toLowerCase()}@example.com`;

const property: PropertyCreateUpdate = {
  name: `EmailMark ${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const worker: PropertyWorker = {
  name: `Eml${rand}`,
  surname: 'Mark',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: `\u200E${cleanEmail}\u200B`,
};
const workerFullName = `${worker.name} ${worker.surname}`;

test.describe.serial('Property-worker dialog: invisible characters in the e-mail', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
  });

  test('the e-mail field strips invisible characters and the worker is saved with the clean address', async ({
    page,
  }) => {
    // 5 min: login, one property and one device user created, then the list reloaded once.
    test.setTimeout(300000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();

    // openCreateModal fills the e-mail with the marks around it.
    await workersPage.openCreateModal(worker);
    await expect(workersPage.createEmailInput(), 'the field must hold the visible address only').toHaveValue(
      cleanEmail,
      { timeout: UI_TIMEOUT }
    );
    await workersPage.closeCreateModal();
    await expect(page.locator('.mat-mdc-row').filter({ hasText: workerFullName })).toHaveCount(1, {
      timeout: UI_TIMEOUT,
    });

    // What the server stored: the worker list it sends back after a reload. The
    // dialog cleans the field when it opens, so it cannot show a dirty stored value.
    const listResponse = page.waitForResponse(
      r =>
        r.url().includes('/api/backend-configuration-pn/properties/assignment/index-device-user') &&
        r.request().method() === 'POST',
      { timeout: UI_TIMEOUT }
    );
    await page.reload();
    const listBody = await (await listResponse).text();
    expect(listBody, 'the stored address must be listed').toContain(cleanEmail);
    // JSON may carry a mark raw or escaped; none may be stored before or after the address.
    const marks = ['\u200E', '\\u200e', '\\u200E', '\u200B', '\\u200b', '\\u200B'];
    for (const mark of marks) {
      expect(listBody, 'no invisible character may be stored around the address').not.toContain(
        `${mark}${cleanEmail}`
      );
      expect(listBody, 'no invisible character may be stored around the address').not.toContain(
        `${cleanEmail}${mark}`
      );
    }
  });
});
