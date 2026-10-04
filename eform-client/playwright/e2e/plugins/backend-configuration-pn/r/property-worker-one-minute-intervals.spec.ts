import { expect, Page, test } from '@playwright/test';
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
import { ActionMenuItem, openRowActionMenu } from '../row-action-menu';
import {
  API_TIMEOUT,
  holdApiGetRequests,
  ignoreUnhandledRejections,
  SLOW_API_TIMEOUT,
  UI_TIMEOUT,
  waitForApiResponse,
} from '../wait-helpers';

// Every worker registers in 1-minute intervals (eform-angular-timeplanning-plugin
// #1740), so the property-worker create/edit modal no longer offers a
// "Use 1-minute intervals" checkbox, nor the "Advanced settings" section that
// held only it.
//
// WHAT THIS PROTECTS: the server hardcodes UseOneMinuteIntervals = true on every
// AssignedSite row it creates (BackendConfigurationAssignmentWorkerServiceHelper,
// both CreateDeviceUser and the UpdateDeviceUser path that mints a row when time
// registration is switched on for the first time), and the flag can no longer be
// changed from the UI. A checkbox over a value nobody can choose is a lie, so the
// tests below assert it is gone on each way a user reaches the Timeregistration
// tab, and that the created row really is in one-minute mode. The last test pins
// what the edit dialog shows while its languages request is still in flight.

const TIME_REGISTRATION_TAB = 'Timeregistrering';

const rand = generateRandmString(4);

const property: PropertyCreateUpdate = {
  name: `OneMin ${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

/** Created WITH time registration, so the server creates its AssignedSite up front. */
const timeRegWorker: PropertyWorker = {
  name: `Omi${rand}`,
  surname: 'Minute',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: `${generateRandmString(5)}@test.com`,
  timeRegistrationEnabled: true,
};
const timeRegWorkerFullName = `${timeRegWorker.name} ${timeRegWorker.surname}`;

/** Created WITHOUT time registration, so it has no AssignedSite row at all. */
const plainWorker: PropertyWorker = {
  name: `Opl${rand}`,
  surname: 'Plain',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: `${generateRandmString(5)}@test.com`,
};
const plainWorkerFullName = `${plainWorker.name} ${plainWorker.surname}`;

/** Opens the action menu of `workerFullName`'s device-user row, after asserting the row is unique. */
async function openWorkerRowMenu(page: Page, workerFullName: string): Promise<ActionMenuItem> {
  const row = page.locator('.mat-mdc-row').filter({ hasText: workerFullName });
  await expect(row, 'the worker to edit must be listed exactly once').toHaveCount(1, { timeout: UI_TIMEOUT });
  return openRowActionMenu(page, row, `Device-user row "${workerFullName}"`);
}

/** The open create/edit dialog. Every locator below hangs off this, never off the page. */
function dialog(page: Page) {
  return page.locator('mat-dialog-container');
}

/**
 * Selects the modal's Timeregistration tab. Its nested "General" sub-tab is the
 * sub-group's default, so no second click is needed. The label is unique across
 * every tab in the dialog on purpose: a `.first()` here could silently land on a
 * nested sub-tab instead.
 */
async function openTimeRegistrationTab(page: Page): Promise<void> {
  const tab = dialog(page).locator('.mat-mdc-tab').filter({ hasText: TIME_REGISTRATION_TAB });
  await tab.click({ timeout: UI_TIMEOUT });
  await expect(tab, 'Timeregistration tab must become the selected tab').toHaveAttribute(
    'aria-selected',
    'true',
    { timeout: UI_TIMEOUT }
  );
  // Post-condition of the tab switch: the General sub-tab's last section is on
  // screen. The 1-minute checkbox used to sit right below it, so the absence
  // checks that follow look at a rendered tab, not one still switching.
  await expect(
    dialog(page).locator('#isManager'),
    'the Timeregistration General sub-tab must render ("Is manager")'
  ).toBeVisible({ timeout: UI_TIMEOUT });
}

/**
 * The 1-minute choice is gone from the open dialog: no checkbox, no label, no
 * section. The CI user's UI is Danish, so the texts are the da.ts translations of
 * 'Use 1-minute intervals' and 'Advanced settings'.
 */
async function expectNoOneMinuteChoice(page: Page, context: string): Promise<void> {
  await expect(
    dialog(page).locator('#useOneMinuteIntervals'),
    `${context}: the "Use 1-minute intervals" checkbox must not render`
  ).toHaveCount(0, { timeout: UI_TIMEOUT });
  await expect(
    dialog(page).getByText('Brug 1-minutters intervaller'),
    `${context}: no "Use 1-minute intervals" label may remain`
  ).toHaveCount(0, { timeout: UI_TIMEOUT });
  await expect(
    dialog(page).getByText('Avancerede indstillinger'),
    `${context}: the "Advanced settings" section held only the checkbox and must be gone`
  ).toHaveCount(0, { timeout: UI_TIMEOUT });
}

/**
 * Closes the edit modal without saving, waiting for the button to go hidden rather
 * than sleeping, so the next step cannot race the dialog's close animation.
 */
async function cancelEditModal(workersPage: BackendConfigurationPropertyWorkersPage): Promise<void> {
  const cancelBtn = workersPage.cancelEditBtn();
  await cancelBtn.click({ timeout: UI_TIMEOUT });
  await cancelBtn.waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
}

test.describe.serial('Property-worker modal offers no 1-minute intervals choice', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    // login() already waits for #newEFormBtn, so the app is loaded when it returns.
    await new LoginPage(page).login();
  });

  test('the create modal offers no 1-minute choice', async ({ page }) => {
    // 5 min: login (up to 2 min on a cold app), one property create, and one
    // device-user create — the SDK provisioning call alone gets SLOW_API_TIMEOUT
    // (60s), followed by the assignment POST and the list refresh (30s each).
    // Every wait inside is individually bounded; this is only their sum.
    test.setTimeout(300000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();

    // Fills the General tab, turns the time-registration toggle on and ticks the
    // property — everything except pressing Create.
    await workersPage.openCreateModal(timeRegWorker);
    await openTimeRegistrationTab(page);
    await expectNoOneMinuteChoice(page, 'create modal');

    await workersPage.closeCreateModal();

    await expect(
      page.locator('.mat-mdc-row').filter({ hasText: timeRegWorkerFullName }),
      'the created worker must show up in the device-user table'
    ).toHaveCount(1, { timeout: UI_TIMEOUT });
  });

  test('the created worker is saved in one-minute mode and its edit modal offers no choice', async ({ page }) => {
    // 4 min: login, one edit-modal round trip (row action menu, the modal's
    // assigned-site GET, form-ready), then one save: update-device-user (SDK-backed,
    // SLOW_API_TIMEOUT), the assigned-site PUT and the list refresh.
    test.setTimeout(240000);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();

    // The modal's ngOnInit fetches the saved AssignedSite; register the wait
    // before the click that triggers it.
    const assignedSiteRequest = waitForApiResponse(
      page,
      'GET /api/time-planning-pn/settings/assigned-sites (edit modal loads the saved AssignedSite)',
      r =>
        new URL(r.url()).pathname.endsWith('/api/time-planning-pn/settings/assigned-sites') &&
        r.request().method() === 'GET',
      API_TIMEOUT
    );
    // openEditModalFor() can fail before we reach the await below.
    ignoreUnhandledRejections(assignedSiteRequest);

    await workersPage.openEditModalFor(timeRegWorkerFullName);

    // With the checkbox gone, the saved row is the only place the mode shows:
    // creating the worker must have saved it in one-minute mode.
    const assignedSiteResponse = await assignedSiteRequest;
    expect(assignedSiteResponse.status(), 'assigned-sites GET status').toBe(200);
    const assignedSiteBody = await assignedSiteResponse.json();
    expect(assignedSiteBody?.success, `assigned-sites GET (${assignedSiteBody?.message ?? ''})`).toBe(true);
    expect(assignedSiteBody?.model?.id, 'creating the worker must have saved an AssignedSite row').toBeTruthy();
    expect(
      assignedSiteBody?.model?.useOneMinuteIntervals,
      'the server hardcodes one-minute intervals on the row it creates'
    ).toBe(true);

    await openTimeRegistrationTab(page);
    await expectNoOneMinuteChoice(page, 'edit modal for a worker with a saved AssignedSite');

    // Saving still works without the control, and the device-user payload no
    // longer claims a 1-minute value the dialog does not show.
    const updateDeviceUserRequest = waitForApiResponse(
      page,
      'POST /api/backend-configuration-pn/properties/assignment/update-device-user (edit save)',
      r =>
        r.url().includes('/api/backend-configuration-pn/properties/assignment/update-device-user') &&
        r.request().method() === 'POST',
      SLOW_API_TIMEOUT
    );
    const assignedSitePut = waitForApiResponse(
      page,
      'PUT /api/time-planning-pn/settings/assigned-site (edit save)',
      r =>
        new URL(r.url()).pathname.endsWith('/api/time-planning-pn/settings/assigned-site') &&
        r.request().method() === 'PUT',
      SLOW_API_TIMEOUT
    );
    const listRefresh = waitForApiResponse(
      page,
      'POST /api/backend-configuration-pn/properties/assignment/index-device-user (list refresh after save)',
      r =>
        r.url().includes('/api/backend-configuration-pn/properties/assignment/index-device-user') &&
        r.request().method() === 'POST',
      SLOW_API_TIMEOUT
    );
    // Awaited one after another below; a later one can reject while an earlier one is pending.
    ignoreUnhandledRejections(updateDeviceUserRequest, assignedSitePut, listRefresh);

    const saveBtn = dialog(page).locator('#saveEditBtn');
    await expect(saveBtn, 'the edit form must be valid to save').toBeEnabled({ timeout: UI_TIMEOUT });
    await saveBtn.click({ timeout: UI_TIMEOUT });

    const updateResponse = await updateDeviceUserRequest;
    const updateResult = await updateResponse.json().catch(() => null);
    expect(updateResponse.status(), `update-device-user status (${JSON.stringify(updateResult)})`).toBe(200);
    expect(updateResult?.success, `update-device-user success (${updateResult?.message ?? ''})`).toBe(true);
    const updateBody = JSON.parse(updateResponse.request().postData() || '{}');
    expect(
      'useOneMinuteIntervals' in updateBody,
      'the update-device-user payload must not carry a 1-minute value'
    ).toBe(false);

    const putResponse = await assignedSitePut;
    const putResult = await putResponse.json().catch(() => null);
    expect(putResponse.status(), `assigned-site PUT status (${JSON.stringify(putResult)})`).toBe(200);
    expect(putResult?.success, `assigned-site PUT success (${putResult?.message ?? ''})`).toBe(true);

    // The save closes the dialog and refreshes the list; wait for both before the
    // next test touches the row.
    await listRefresh;
    await saveBtn.waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    await workersPage.newDeviceUserBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  });

  test('switching time registration on for an existing worker offers no 1-minute choice either', async ({ page }) => {
    // 5 min: login plus one more device-user create (SDK provisioning again),
    // then a single edit-modal round trip with no save.
    test.setTimeout(300000);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();

    // No time registration on create, so the server created no AssignedSite row:
    // turning the toggle on below is the "first time" case.
    await workersPage.create(plainWorker);
    await expect(
      page.locator('.mat-mdc-row').filter({ hasText: plainWorkerFullName }),
      'the worker without time registration must show up in the device-user table'
    ).toHaveCount(1, { timeout: UI_TIMEOUT });

    await workersPage.openEditModalFor(plainWorkerFullName);

    // Precondition, asserted rather than assumed: no Timeregistration tab yet.
    await expect(
      dialog(page).locator('.mat-mdc-tab').filter({ hasText: TIME_REGISTRATION_TAB }),
      'a worker without time registration must not have a Timeregistration tab'
    ).toHaveCount(0, { timeout: UI_TIMEOUT });

    const timeRegistrationToggle = dialog(page).locator('#timeRegistrationEnabledToggle');
    await timeRegistrationToggle.locator('button').click({ timeout: UI_TIMEOUT });
    await expect(
      timeRegistrationToggle.locator('button[role="switch"]'),
      'the time-registration toggle must report itself on'
    ).toHaveAttribute('aria-checked', 'true', { timeout: UI_TIMEOUT });

    await openTimeRegistrationTab(page);
    await expectNoOneMinuteChoice(page, 'edit modal switching time registration on for the first time');

    // Nothing to save — the assertion is about what the modal offers, and leaving
    // the worker untouched keeps the row usable for whatever runs next.
    await cancelEditModal(workersPage);
  });

  test('an existing worker opens in edit mode before the languages request returns', async ({ page }) => {
    // 2 min: login plus one edit-modal round trip. The languages GET is held only
    // for as long as the in-flight assertions take, each bounded by UI_TIMEOUT.
    test.setTimeout(120000);

    // WHAT THIS PROTECTS: `edit` used to be set inside the getEnabledLanguages()
    // callback, so until that GET returned an EDIT dialog rendered as a create
    // dialog — title "New employee" and #saveCreateBtn — and the save button's
    // `edit ? updateSingle() : createDeviceUser()` sent a fast click down the
    // CREATE path, minting a duplicate worker. It is now known synchronously from
    // the dialog data. Holding the languages GET makes that window deterministic.
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();

    const menuItem = await openWorkerRowMenu(page, timeRegWorkerFullName);

    // AppSettingsService.getLanguages() -> GET api/settings/languages, fired from
    // the dialog's ngOnInit. Installed right before the click that opens it.
    const languages = await holdApiGetRequests(
      page,
      'GET /api/settings/languages (edit dialog ngOnInit)',
      '/api/settings/languages',
      UI_TIMEOUT
    );
    try {
      await menuItem('editDeviceUserBtn').click({ timeout: UI_TIMEOUT });
      await languages.held;

      // Proof that this IS the in-flight window: formReady waits for languages.
      await expect(
        dialog(page).locator('form[data-form-ready]'),
        'with the languages request held the form must not report itself ready'
      ).toHaveAttribute('data-form-ready', 'false', { timeout: UI_TIMEOUT });

      await expect(
        dialog(page).locator('#saveEditBtn'),
        'an existing worker must get the edit Save button before languages load'
      ).toBeVisible({ timeout: UI_TIMEOUT });
      await expect(
        dialog(page).locator('#saveCreateBtn'),
        'the create button must never render for an existing worker — clicking it creates a duplicate'
      ).toHaveCount(0, { timeout: UI_TIMEOUT });
    } finally {
      await languages.release();
    }

    // Released: the dialog settles and is still in edit mode.
    await expect(dialog(page).locator('form[data-form-ready]'), 'the dialog must settle once languages load').toHaveAttribute(
      'data-form-ready',
      'true',
      { timeout: API_TIMEOUT }
    );
    await expect(dialog(page).locator('#saveEditBtn'), 'still in edit mode after languages load').toBeVisible({
      timeout: UI_TIMEOUT,
    });
    await expect(dialog(page).locator('#saveCreateBtn')).toHaveCount(0, { timeout: UI_TIMEOUT });

    await cancelEditModal(workersPage);
  });
});
