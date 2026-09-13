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
import { openRowActionMenu } from '../row-action-menu';
import {
  API_TIMEOUT,
  ignoreUnhandledRejections,
  UI_TIMEOUT,
  waitForApiResponse,
} from '../wait-helpers';

// "Set password" / "Send reset password email" in the property-worker row menu.
//
// WHAT THIS PROTECTS: the server creates an EformUser for ANY worker carrying
// an email — not just time-registration/archive/web-access workers — so there is
// something to set a password on in every one of those cases. The menu items must
// therefore be offered for exactly that condition.
//
// WHY IT MATTERS: gated on only those three flags, an admin would have no way to
// give a plain worker a password even though the account exists. Gated too widely, the
// items would resolve to a null user and AccountService.AdminChangePassword would
// throw rather than report anything useful.

const rand = generateRandmString(4);

const property: PropertyCreateUpdate = {
  name: `Pwd ${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// Email, and none of time registration / archive / web access.
const emailOnlyWorker: PropertyWorker = {
  name: `Pwo${rand}`,
  surname: 'Emailonly',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: `${generateRandmString(5)}@test.com`,
};
const emailOnlyWorkerFullName = `${emailOnlyWorker.name} ${emailOnlyWorker.surname}`;

// A second property + worker, so the set-password test does not depend on the
// rows the visibility test above happens to leave behind.
const setPwdProperty: PropertyCreateUpdate = {
  name: `Pwd set ${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const setPwdWorker: PropertyWorker = {
  name: `Pws${rand}`,
  surname: 'Setpwd',
  language: 'Dansk',
  properties: [setPwdProperty.name],
  workerEmail: `${generateRandmString(5)}@test.com`,
};
const setPwdWorkerFullName = `${setPwdWorker.name} ${setPwdWorker.surname}`;

// Generated at runtime rather than a literal (GitGuardian flags any committed
// password-shaped string, even a fabricated test one) - the fixed "Aa1"
// prefix guarantees lower/upper/digit regardless of generateRandmString's own
// charset, satisfying the modal's own rules (>= 8 chars, and the strength
// meter's length/lower/upper/digit rules) as well as the server's Identity
// policy.
const NEW_PASSWORD = `Aa1${generateRandmString(12)}`;

test.describe('Property-worker password affordance follows the email', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    // login() already waits for #newEFormBtn, so the app is loaded when it returns.
    await new LoginPage(page).login();
  });

  test('a worker with only an email is offered Set password', async ({ page }) => {
    // 5 min: login (up to 2 min on a cold app), one property create, and one
    // device-user create whose SDK provisioning call is the slow part, then a
    // single menu open. Every wait inside is individually bounded; this is only
    // their sum.
    test.setTimeout(300000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(emailOnlyWorker);

    const row = page.locator('.mat-mdc-row').filter({ hasText: emailOnlyWorkerFullName });
    await expect(row, 'the created worker must show up in the device-user table')
      .toHaveCount(1, { timeout: UI_TIMEOUT });

    // mat-menu content is projected into a CDK overlay on <body>, so the helper
    // addresses items by the row's action-items-<i> index rather than scoping to
    // the row, which would find nothing at all.
    const menuItem = await openRowActionMenu(page, row, emailOnlyWorkerFullName);

    await expect(
      menuItem('setPasswordBtn'),
      'a worker with an email must be offered "Set password"'
    ).toBeVisible({ timeout: UI_TIMEOUT });

    await expect(
      menuItem('sendResetPasswordEmailBtn'),
      'a worker with an email must be offered "Send reset password email"'
    ).toBeVisible({ timeout: UI_TIMEOUT });
  });

  test('setting a password on an email-only worker succeeds', async ({ page }) => {
    // 5 min: login (up to 2 min on a cold app), one property create, one
    // device-user create whose SDK provisioning is the slow part, then one menu
    // open and one change-password round trip. Every wait inside is individually
    // bounded; this is only their sum.
    test.setTimeout(300000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(setPwdProperty);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(setPwdWorker);

    const row = page.locator('.mat-mdc-row').filter({ hasText: setPwdWorkerFullName });
    await expect(row, 'the created worker must show up in the device-user table')
      .toHaveCount(1, { timeout: UI_TIMEOUT });

    const menuItem = await openRowActionMenu(page, row, setPwdWorkerFullName);
    await menuItem('setPasswordBtn').click({ timeout: UI_TIMEOUT });

    // The modal is a CDK overlay dialog; scope every field to it so a stale
    // password input from another dialog can never be the one we fill.
    const dialog = page.locator('mat-dialog-container');
    await expect(dialog, 'the set-password dialog must open')
      .toBeVisible({ timeout: UI_TIMEOUT });
    await expect(
      dialog.getByText(setPwdWorker.workerEmail!),
      'the dialog must target the worker whose row we opened'
    ).toBeVisible({ timeout: UI_TIMEOUT });

    await dialog.locator('#newPassword').fill(NEW_PASSWORD);
    await dialog.locator('#newPasswordConfirmation').fill(NEW_PASSWORD);

    const saveBtn = dialog.locator('#userDeleteBtn');
    await expect(saveBtn, 'Save stays disabled until the form validates')
      .toBeEnabled({ timeout: UI_TIMEOUT });

    // Registered BEFORE the click: the component closes the dialog the moment it
    // fires the request, without waiting for the reply, so the dialog being gone
    // proves nothing about whether the password was set.
    const changePassword = waitForApiResponse(
      page,
      'POST api/account/change-password-admin',
      response =>
        response.url().includes('api/account/change-password-admin') &&
        response.request().method() === 'POST',
      API_TIMEOUT
    );
    // The wait is bounded, so it can reject before the click below is even
    // reached; an unhandled rejection would fail the whole run rather than this
    // assertion. The await further down still observes the failure.
    ignoreUnhandledRejections(changePassword);

    await saveBtn.click({ timeout: UI_TIMEOUT });

    const response = await changePassword;

    // WHAT THIS PROTECTS: the affordance is gated on the worker's EMAIL, which is
    // projected off the SDK Worker row and not off AspNetUsers. If no EformUser
    // was created for this worker, core's AccountService.AdminChangePassword calls
    // RemovePasswordAsync on the null result of GetByUsernameAsync — an
    // ArgumentNullException and an HTTP 500 rather than anything the UI reports.
    expect(
      response.status(),
      'setting a password must reach a real account, not a 500 from a missing user'
    ).toBe(200);
    expect(
      (await response.json()).success,
      'the server must report the password change as successful'
    ).toBe(true);
  });
});
