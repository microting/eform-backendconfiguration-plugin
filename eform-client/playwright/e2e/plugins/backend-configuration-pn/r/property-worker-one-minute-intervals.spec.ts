import { expect, Page, Response, test } from '@playwright/test';
import { execFile } from 'child_process';
import { promisify } from 'util';
import DatabaseConfigurationConstants from '../../../Constants/DatabaseConfigurationConstants';
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
// The first three tests below are the three ways a user reaches that state. The
// ones after them pin what the edit dialog shows while its own requests are
// still in flight, by holding those requests with page.route.

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
 *
 * `via: 'keyboard'` is for while a request is held: the app's LoaderInterceptor
 * puts a full-screen spinner overlay over everything while ANY request is
 * pending, so a pointer click would never reach the tab. Focusing the tab and
 * pressing Enter is the keyboard path mat-tab-group supports for every user.
 */
async function openTimeRegistrationTab(page: Page, via: 'pointer' | 'keyboard' = 'pointer'): Promise<void> {
  const tab = dialog(page).locator('.mat-mdc-tab').filter({ hasText: TIME_REGISTRATION_TAB });
  if (via === 'keyboard') {
    await tab.press('Enter', { timeout: UI_TIMEOUT });
  } else {
    await tab.click({ timeout: UI_TIMEOUT });
  }
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
 * Ticked also means NOT indeterminate: the native input keeps its `checked` flag
 * underneath a mixed display, so `toBeChecked()` alone would not notice a box
 * still showing "unknown".
 */
async function expectCheckedAndLocked(page: Page, context: string): Promise<void> {
  const input = oneMinuteIntervalsInput(page);
  await expect(input, `${context}: "Use 1-minute intervals" must be checked`).toBeChecked({
    timeout: UI_TIMEOUT,
  });
  await expect(input, `${context}: "Use 1-minute intervals" must not be indeterminate`).not.toBeChecked({
    indeterminate: true,
    timeout: UI_TIMEOUT,
  });
  await expect(input, `${context}: "Use 1-minute intervals" must be disabled`).toBeDisabled({
    timeout: UI_TIMEOUT,
  });
}

const ASSIGNED_SITES_PATH = '/api/time-planning-pn/settings/assigned-sites';

/** The edit dialog's saved-row GET — not the singular `assigned-site` PUT that saves it. */
function isAssignedSiteGet(r: Response): boolean {
  return new URL(r.url()).pathname.endsWith(ASSIGNED_SITES_PATH) && r.request().method() === 'GET';
}

const execFileAsync = promisify(execFile);

/**
 * Puts a saved AssignedSite back into 5-minute mode, straight in the CI database.
 *
 * No API can do this any more, which is the point of the PR: every create path
 * hardcodes UseOneMinuteIntervals = true, and TimePlanning's updateAssignedSite
 * ORs the incoming flag into the stored one (one-way). Yet every site set up
 * before one-minute intervals became the default still looks exactly like this in
 * production, and the edit dialog has to handle it.
 *
 * Depends on .github/workflows/dotnet-core-pr.yml, job pn-playwright-test: its
 * "Start MariaDB" step (`docker run --name mariadbtest ...`, line 137 at the time
 * of writing) starts the database on the same runner host this spec runs on, with
 * the root password it passes as MYSQL_ROOT_PASSWORD; the job's own "Change
 * rabbitmq hostname" step already runs `docker exec -i mariadbtest mariadb -u root
 * ...` the same way. The schema is the time-planning plugin's, under the customer
 * number the database-configuration step sets up. Tests run in CI only (CLAUDE.md).
 */
async function setSavedOneMinuteIntervalsToFalse(assignedSiteId: number): Promise<void> {
  if (!Number.isInteger(assignedSiteId) || assignedSiteId <= 0) {
    throw new Error(`Refusing to build SQL for AssignedSite id ${String(assignedSiteId)}`);
  }
  const database = `${DatabaseConfigurationConstants.customerNo}_eform-angular-time-planning-plugin`;
  const sql =
    `UPDATE AssignedSites SET UseOneMinuteIntervals = 0 ` +
    `WHERE Id = ${assignedSiteId} AND WorkflowState <> 'removed'; SELECT ROW_COUNT();`;
  let stdout: string;
  try {
    ({ stdout } = await execFileAsync(
      'docker',
      ['exec', 'mariadbtest', 'mariadb', '-u', 'root', '--password=secretpassword', '-N', '-B',
        `--database=${database}`, '-e', sql],
      { timeout: API_TIMEOUT }
    ));
  } catch (error) {
    throw new Error(
      `Could not put AssignedSite ${assignedSiteId} back into 5-minute mode via docker exec: ${String(error)}`
    );
  }
  expect(
    stdout.trim(),
    `exactly one active AssignedSite row (id ${assignedSiteId}) must have been switched to 5-minute mode`
  ).toBe('1');
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

  test('the create modal shows it checked and locked, and sends exactly that', async ({ page }) => {
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

    // The payload must carry exactly what the checkbox shows: true. It used to say
    // false while the box showed checked — the component mirrors form valueChanges
    // into the model, one such emission captured the control's default false before
    // the rule ticked and locked it, and a disabled control is absent from
    // form.value, so nothing overwrote the stale value. The server overrules it
    // today (create hardcodes true), but a payload contradicting the UI is the same
    // lie this spec exists to catch, and it would persist the day that changes.
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
      'the create payload must say what the checked, locked box shows: useOneMinuteIntervals = true'
    ).toBe(true);

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

  // Runs LAST: it leaves timeRegWorker's saved row in 5-minute mode.
  test('the edit dialog claims no 1-minute state until the saved row has loaded', async ({ page }) => {
    // 3 min: login, two edit-modal round trips (one to read the AssignedSite id,
    // one with its GET held) and one SQL statement. No device-user provisioning.
    test.setTimeout(180000);

    // WHAT THIS PROTECTS: the checkbox rule used to run from ngOnInit before the
    // getAssignedSite GET landed, saw no saved row yet, and forced the box checked
    // and locked — for a worker whose saved row is in 5-minute mode. formReady is
    // not gated on that GET, so the dialog showed "one-minute mode, locked" without
    // knowing. While the GET is in flight the box must be locked and INDETERMINATE —
    // unknown shown as unknown, neither mode claimed; once it lands the saved state
    // rules.
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();

    // --- Arrange: timeRegWorker's saved row, switched back to 5-minute mode ---
    const firstLoad = waitForApiResponse(
      page,
      'GET /api/time-planning-pn/settings/assigned-sites (reads the saved AssignedSite id)',
      isAssignedSiteGet,
      API_TIMEOUT
    );
    // openEditModalFor() can fail before we reach the await below.
    ignoreUnhandledRejections(firstLoad);
    await workersPage.openEditModalFor(timeRegWorkerFullName);
    const firstBody = await (await firstLoad).json();
    expect(firstBody?.success, `assigned-sites GET (${firstBody?.message ?? ''})`).toBe(true);
    const assignedSiteId = firstBody?.model?.id;
    expect(typeof assignedSiteId, 'the worker must have a saved AssignedSite row').toBe('number');
    await cancelEditModal(workersPage);

    await setSavedOneMinuteIntervalsToFalse(assignedSiteId);

    // --- Act: reopen with the saved-row GET held -----------------------------
    const menuItem = await openWorkerRowMenu(page, timeRegWorkerFullName);

    // TimePlanningPnSettingsService.getAssignedSite() -> GET
    // api/time-planning-pn/settings/assigned-sites?siteId=..., fired from the
    // dialog's ngOnInit for a worker with time registration on.
    const assignedSite = await holdApiGetRequests(
      page,
      'GET /api/time-planning-pn/settings/assigned-sites (edit dialog loads the saved AssignedSite)',
      ASSIGNED_SITES_PATH,
      UI_TIMEOUT
    );
    try {
      await menuItem('editDeviceUserBtn').click({ timeout: UI_TIMEOUT });
      await assignedSite.held;

      // The window is real: the dialog reports itself ready while the saved row
      // is still unknown, because formReady only waits for languages.
      await expect(
        dialog(page).locator('form[data-form-ready]'),
        'the dialog must report ready even with the saved-row GET held — this is the window a user can act in'
      ).toHaveAttribute('data-form-ready', 'true', { timeout: API_TIMEOUT });

      await openTimeRegistrationTab(page, 'keyboard');
      const input = oneMinuteIntervalsInput(page);
      await expect(input, 'while the saved row is loading the checkbox must be locked').toBeDisabled({
        timeout: UI_TIMEOUT,
      });
      // The assertion the old code fails: it forced the box checked here, and an
      // unforced plain box would read unchecked — 5-minute mode — just as wrongly.
      await expect(
        input,
        'while the saved row is loading the checkbox must show indeterminate — the dialog does not know the mode yet'
      ).toBeChecked({ indeterminate: true, timeout: UI_TIMEOUT });

      // Registered before the release; the held response cannot arrive earlier.
      const heldResponse = waitForApiResponse(
        page,
        'GET /api/time-planning-pn/settings/assigned-sites (the released saved-row GET)',
        isAssignedSiteGet,
        API_TIMEOUT
      );
      ignoreUnhandledRejections(heldResponse);
      await assignedSite.release();

      // What the dialog was handed: the real saved row, in 5-minute mode.
      const response = await heldResponse;
      expect(response.status(), 'assigned-sites GET status').toBe(200);
      const body = await response.json();
      expect(body?.success, `assigned-sites GET (${body?.message ?? ''})`).toBe(true);
      expect(body?.model?.id, 'the GET must return the row arranged above').toBe(assignedSiteId);
      expect(body?.model?.useOneMinuteIntervals, 'precondition: the saved row is in 5-minute mode').toBe(false);
    } finally {
      await assignedSite.release();
    }

    // --- Assert: the saved state now rules -----------------------------------
    const input = oneMinuteIntervalsInput(page);
    await expect(input, 'a saved 5-minute row is editable once loaded').toBeEnabled({ timeout: UI_TIMEOUT });
    await expect(input, 'once loaded the checkbox is no longer indeterminate').not.toBeChecked({
      indeterminate: true,
      timeout: UI_TIMEOUT,
    });
    await expect(input, 'a saved 5-minute row shows unchecked once loaded').not.toBeChecked({ timeout: UI_TIMEOUT });

    // Nothing to save — the assertions are about what the dialog shows.
    await cancelEditModal(workersPage);
  });
});
