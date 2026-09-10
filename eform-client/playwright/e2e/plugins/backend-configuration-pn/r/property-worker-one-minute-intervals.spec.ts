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
import {
  API_TIMEOUT,
  ignoreUnhandledRejections,
  SLOW_API_TIMEOUT,
  UI_TIMEOUT,
  waitForApiResponse,
} from '../wait-helpers';

// "Use 1-minute intervals" (#useOneMinuteIntervals) in the property-worker
// create/edit modal — Advanced settings on the Timeregistration tab.
//
// WHAT THIS PROTECTS: the server hardcodes UseOneMinuteIntervals = true on
// every AssignedSite row it creates (BackendConfigurationAssignmentWorkerServiceHelper,
// both CreateDeviceUser and the UpdateDeviceUser path that mints a row when time
// registration is switched on for the first time). DeviceUserModel.UseOneMinuteIntervals
// is accepted for wire compatibility but read by no server path.
//
// WHY IT MATTERS: an editable checkbox over a hardcoded server value is a lie. An
// admin who left it unticked would be told the worker registers in 5-minute steps
// while the row was saved in one-minute mode, and the flag is ONE-WAY — the server
// never turns it back off — so the mistake is not correctable afterwards. The UI
// must therefore show the outcome (ticked) and refuse the choice (disabled)
// whenever the worker has no saved AssignedSite yet, which is exactly what
// PropertyWorkerCreateEditModalComponent.applyOneMinuteIntervalsRule() does by
// locking the control while `!selectedAssignedSite.id`.
//
// The three tests below are the three ways a user reaches that state.

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

/** The open create/edit dialog. Every locator below hangs off this, never off the page. */
function dialog(page: Page) {
  return page.locator('mat-dialog-container');
}

function oneMinuteIntervalsCheckbox(page: Page) {
  return dialog(page).locator('#useOneMinuteIntervals');
}

/** The native input mat-checkbox renders — the element that actually carries checked/disabled. */
function oneMinuteIntervalsInput(page: Page) {
  return oneMinuteIntervalsCheckbox(page).locator('input[type="checkbox"]');
}

/**
 * Selects the modal's Timeregistration tab. Its nested "General" sub-tab (which
 * holds Advanced settings) is the sub-group's default, so no second click is
 * needed. The label is unique across every tab in the dialog on purpose: a
 * `.first()` here could silently land on a nested sub-tab instead.
 */
async function openTimeRegistrationTab(page: Page): Promise<void> {
  const tab = dialog(page).locator('.mat-mdc-tab').filter({ hasText: TIME_REGISTRATION_TAB });
  await tab.click({ timeout: UI_TIMEOUT });
  await expect(tab, 'Timeregistration tab must become the selected tab').toHaveAttribute(
    'aria-selected',
    'true',
    { timeout: UI_TIMEOUT }
  );
  // Post-condition of the tab switch: the Advanced-settings checkbox is on screen.
  // It only renders for a non-resigned worker when the current user is admin, so
  // this also proves the admin gate is satisfied rather than assuming it.
  await expect(
    oneMinuteIntervalsCheckbox(page),
    'Advanced settings / "Use 1-minute intervals" must render for an admin'
  ).toBeVisible({ timeout: UI_TIMEOUT });
}

/**
 * Both halves of the invariant, asserted separately: ticked (the state the server
 * will save) AND disabled (no choice offered). Neither is inferred from the other —
 * a locked-but-unticked box and an editable-but-ticked box are different defects.
 */
async function expectCheckedAndLocked(page: Page, context: string): Promise<void> {
  const input = oneMinuteIntervalsInput(page);
  await expect(input, `${context}: "Use 1-minute intervals" must be checked`).toBeChecked({
    timeout: UI_TIMEOUT,
  });
  await expect(input, `${context}: "Use 1-minute intervals" must be disabled`).toBeDisabled({
    timeout: UI_TIMEOUT,
  });
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

test.describe.serial('Property-worker 1-minute intervals are locked on', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    // login() already waits for #newEFormBtn, so the app is loaded when it returns.
    await new LoginPage(page).login();
  });

  test('the create modal shows it checked and locked, and never sends a choice', async ({ page }) => {
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
    await expectCheckedAndLocked(page, 'create modal');

    // The disabled control is left out of `form.value` and DeviceUserModel declares
    // no useOneMinuteIntervals field, so the payload must not carry the key at all —
    // which also rules out the `false` the server would silently overrule.
    const createRequest = waitForApiResponse(
      page,
      'PUT /api/backend-configuration-pn/properties/assignment/create-device-user (create payload)',
      r =>
        r.url().includes('/api/backend-configuration-pn/properties/assignment/create-device-user') &&
        r.request().method() === 'PUT',
      SLOW_API_TIMEOUT
    );
    // Awaited after closeCreateModal() below, which can itself throw first.
    ignoreUnhandledRejections(createRequest);

    await workersPage.closeCreateModal();

    const createBody = JSON.parse((await createRequest).request().postData() || '{}');
    expect(
      createBody.useOneMinuteIntervals,
      'the locked checkbox must leave useOneMinuteIntervals out of the create payload entirely'
    ).toBeUndefined();

    await expect(
      page.locator('.mat-mdc-row').filter({ hasText: timeRegWorkerFullName }),
      'the created worker must show up in the device-user table'
    ).toHaveCount(1, { timeout: UI_TIMEOUT });
  });

  test('reopening the created worker shows one-minute mode saved and still locked', async ({ page }) => {
    // 3 min: login plus one edit-modal round trip (row action menu, the modal's
    // assigned-site GET, form-ready). No device-user provisioning here.
    test.setTimeout(180000);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();

    // The modal's ngOnInit fetches the saved AssignedSite; register the wait
    // before the click that triggers it.
    const assignedSiteRequest = waitForApiResponse(
      page,
      'GET /api/time-planning-pn/settings/assigned-sites (edit modal loads the saved AssignedSite)',
      r =>
        r.url().includes('/api/time-planning-pn/settings/assigned-sites') &&
        r.request().method() === 'GET',
      API_TIMEOUT
    );
    // openEditModalFor() can fail before we reach the await below.
    ignoreUnhandledRejections(assignedSiteRequest);

    await workersPage.openEditModalFor(timeRegWorkerFullName);

    // What the UI was handed: a real, saved row that is in one-minute mode. This
    // rules out the alternative reading of a locked checkbox — that no row exists
    // yet — so the lock below can only be the one-way rule on a saved `true`.
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
    await expectCheckedAndLocked(page, 'edit modal for a worker whose AssignedSite is saved in one-minute mode');

    await cancelEditModal(workersPage);
  });

  test('switching time registration on for an existing worker locks it too', async ({ page }) => {
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
    await expectCheckedAndLocked(page, 'edit modal switching time registration on for the first time');

    // Nothing to save — the assertion is about what the modal offers, and leaving
    // the worker untouched keeps the row usable for whatever runs next.
    await cancelEditModal(workersPage);
  });
});
