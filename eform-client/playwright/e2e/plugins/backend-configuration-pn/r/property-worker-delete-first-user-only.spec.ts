import { expect, Locator, Page, Response, test } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import LoginConstants from '../../../Constants/LoginConstants';
import { generateRandmString } from '../../../helper-functions';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
  PropertyRowObject,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
  WorkerRowObject,
} from '../BackendConfigurationPropertyWorkers.page';
import { openRowActionMenu } from '../row-action-menu';
import {
  API_TIMEOUT,
  ignoreUnhandledRejections,
  SLOW_API_TIMEOUT,
  UI_TIMEOUT,
  waitForApiResponse,
} from '../wait-helpers';

// "Delete employee" in the property-worker row menu.
//
// WHAT THIS PROTECTS: only the first user (the lowest AspNetUsers Id) may delete
// a property worker, and only while also holding the device_users_delete claim
// that core's delete endpoint requires. Being an admin is not enough.
//
// HOW: a second ADMIN is created. Admins get every claim, device_users_delete
// included, but this one is not the first user, so its row menu must not offer
// "Delete employee". "Edit employee" (device_users_update, which it also holds)
// must be in the same menu. That proves the menu opened with the claims loaded;
// otherwise an absent delete item could just be a menu that never rendered. The
// last test deletes the worker as admin@admin.com, the first user in CI, and
// asserts the plugin accepts it, so the gate is not simply "nobody may delete".

const BASE_URL = 'http://localhost:4200';
// The same budget LoginPage.login() gives the app to bootstrap and land.
const APP_LOAD_TIMEOUT = 120000;

const rand = generateRandmString(8).toLowerCase();

const property: PropertyCreateUpdate = {
  name: `Fu ${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// The create form requires an email (#1242).
const worker: PropertyWorker = {
  name: `Fu${rand}`,
  surname: 'Nodelete',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: `fu-worker-${rand}@test.com`,
};
const workerFullName = `${worker.name} ${worker.surname}`;

const secondAdminEmail = `second-admin-${rand}@test.com`;
// Generated at runtime rather than a literal (GitGuardian flags any committed
// password-shaped string). The "Aa1" prefix guarantees lower/upper/digit for the
// server's Identity policy regardless of generateRandmString's charset.
const secondAdminPassword = `Aa1${generateRandmString(12)}`;

/** The seeded worker's row, scoped to the worker grid's own host element. */
function workerRow(page: Page): Locator {
  return page.locator('app-property-worker-table .mat-mdc-row').filter({ hasText: workerFullName });
}

/**
 * Starts a bounded wait for a response, to be awaited after the action that
 * triggers it. That action can throw first, so the pending wait gets a no-op
 * rejection handler; the later `await` still observes a timeout.
 */
function watchApiResponse(
  page: Page,
  description: string,
  predicate: (response: Response) => boolean,
  timeout: number
): Promise<Response> {
  const response = waitForApiResponse(page, description, predicate, timeout);
  ignoreUnhandledRejections(response);
  return response;
}

/**
 * Whether `response` is a `method` call to `path`, matched on a path-segment
 * boundary so a route that merely shares a suffix cannot match. The query
 * string is ignored.
 */
function isCall(response: Response, method: string, path: string): boolean {
  const pathname = new URL(response.url()).pathname;
  const relativePath = path.replace(/^\//, '');
  return (
    response.request().method() === method &&
    (pathname === path || pathname.endsWith(`/${relativePath}`))
  );
}

/** The part of the device-user index this spec reads. */
type DeviceUserIndexResponse = { model?: Array<{ siteName?: string }> };

/** API headers for the first user, whose credentials core's LoginConstants hold. */
async function firstUserApiHeaders(page: Page): Promise<Record<string, string>> {
  const res = await page.request.post(`${BASE_URL}/api/auth/token`, {
    form: { username: LoginConstants.username, password: LoginConstants.password, grant_type: 'password' },
    timeout: API_TIMEOUT,
  });
  const token = (await res.json())?.model?.accessToken;
  expect(token, 'the first user must obtain an API token').toBeTruthy();
  return { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
}

/** Logs in through the form as an admin, who lands on the eForms list. */
async function loginThroughForm(page: Page, email: string, password: string): Promise<void> {
  await page.goto(BASE_URL, { timeout: APP_LOAD_TIMEOUT });
  const loginBtn = page.locator('#loginBtn');
  await loginBtn.waitFor({ state: 'visible', timeout: APP_LOAD_TIMEOUT });
  await page.locator('#username').fill(email);
  await page.locator('#password').fill(password);
  const tokenResponse = watchApiResponse(
    page,
    `POST /api/auth/token (login as ${email})`,
    r => isCall(r, 'POST', '/api/auth/token'),
    API_TIMEOUT
  );
  await loginBtn.click({ timeout: UI_TIMEOUT });
  const body = await (await tokenResponse).json().catch(() => null);
  expect(body?.model?.accessToken, `${email} must be able to log in`).toBeTruthy();
  await page.locator('#newEFormBtn').waitFor({ state: 'visible', timeout: APP_LOAD_TIMEOUT });
}

/**
 * Opens the property-workers page from the sidebar and waits for the grid's
 * data, not just the page chrome. The page builds its rows from a forkJoin of
 * three calls (property-workers-page.component.ts `updateTable`): the
 * properties dictionary GET, the device-user index POST and the worker
 * assignments GET. Returns the parsed device-user index, so a caller can tell
 * from the data whether a worker exists; a row count does not wait.
 */
async function openPropertyWorkers(page: Page): Promise<DeviceUserIndexResponse> {
  const dictionaryCall = watchApiResponse(
    page,
    'GET /api/backend-configuration-pn/properties/dictionary (properties dictionary)',
    r => isCall(r, 'GET', '/api/backend-configuration-pn/properties/dictionary'),
    API_TIMEOUT
  );
  const deviceUsersCall = watchApiResponse(
    page,
    'POST /api/backend-configuration-pn/properties/assignment/index-device-user (device user list)',
    r => isCall(r, 'POST', '/api/backend-configuration-pn/properties/assignment/index-device-user'),
    API_TIMEOUT
  );
  const assignmentsCall = watchApiResponse(
    page,
    'GET /api/backend-configuration-pn/properties/assignment (worker property assignments)',
    r => isCall(r, 'GET', '/api/backend-configuration-pn/properties/assignment'),
    API_TIMEOUT
  );

  const workersMenuItem = page.locator('#backend-configuration-pn-property-workers');
  if (!(await workersMenuItem.isVisible())) {
    await page.locator('#backend-configuration-pn').click({ timeout: UI_TIMEOUT });
  }
  await workersMenuItem.click({ timeout: UI_TIMEOUT });
  const [, deviceUsersResponse] = await Promise.all([dictionaryCall, deviceUsersCall, assignmentsCall]);
  await page.locator('#newDeviceUserBtn').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  return deviceUsersResponse.json();
}

test.describe.serial('Only the first user may delete a property worker', () => {
  /**
   * Removes whatever the tests did not: the worker (if the last test never
   * deleted it), the property and the second admin. It runs as the first user,
   * the only account allowed to delete a worker. Failures are logged, never
   * thrown, so cleanup cannot itself fail the run.
   */
  test.afterAll(async ({ browser }) => {
    // Playwright gives a hook the config-level 120s, below this budget: an admin
    // login, one SDK-backed worker delete, a property delete and two API calls.
    const CLEANUP_BUDGET_MS = 150000;
    test.setTimeout(CLEANUP_BUDGET_MS + 30000);

    const problems: string[] = [];
    const page = await browser.newPage().catch((err: any) => {
      problems.push(`browser.newPage() failed: ${err?.message ?? err}`);
      return undefined;
    });
    const cleanup = async (cleanupPage: Page) => {
      await cleanupPage.goto(BASE_URL, { timeout: APP_LOAD_TIMEOUT });
      await new LoginPage(cleanupPage).login();

      // Decided from the server's list, not a row count: the rows render only
      // once all three grid calls have landed.
      const workersPage = new BackendConfigurationPropertyWorkersPage(cleanupPage);
      const deviceUsers = await openPropertyWorkers(cleanupPage);
      if ((deviceUsers?.model ?? []).some(u => u.siteName === workerFullName)) {
        await workerRow(cleanupPage).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
        await new WorkerRowObject(cleanupPage, workersPage, 1, workerFullName).delete();
      }

      // Decided from one large page of the index: the page's own list is paged
      // (10 rows, by Id), so a new property may not be on its first page.
      const headers = await firstUserApiHeaders(cleanupPage);
      const propertiesRes = await cleanupPage.request.post(`${BASE_URL}/api/backend-configuration-pn/properties/index`, {
        headers,
        data: { nameFilter: '', sort: 'Id', isSortDsc: false, pageIndex: 0, pageSize: 1000, offset: 0 },
        timeout: API_TIMEOUT,
      });
      const properties: any[] = (await propertiesRes.json())?.model?.entities || [];
      if (properties.some(p => p.name === property.name)) {
        // Filter the page by name so the row is on screen, then delete it
        // through its row menu like every other spec does.
        const propertiesPage = new BackendConfigurationPropertiesPage(cleanupPage);
        const unfilteredIndex = watchApiResponse(
          cleanupPage,
          'POST /api/backend-configuration-pn/properties/index (property list)',
          r => isCall(r, 'POST', '/api/backend-configuration-pn/properties/index'),
          API_TIMEOUT
        );
        await propertiesPage.goToProperties();
        await unfilteredIndex;
        const filteredIndex = watchApiResponse(
          cleanupPage,
          `POST /api/backend-configuration-pn/properties/index (filtered by "${property.name}")`,
          r =>
            isCall(r, 'POST', '/api/backend-configuration-pn/properties/index') &&
            r.request().postDataJSON()?.nameFilter === property.name,
          API_TIMEOUT
        );
        await cleanupPage.locator('#nameInput').fill(property.name, { timeout: UI_TIMEOUT });
        await filteredIndex;
        await cleanupPage
          .locator('app-properties-table .mat-mdc-row')
          .filter({ hasText: property.name })
          .waitFor({ state: 'visible', timeout: UI_TIMEOUT });
        await new PropertyRowObject(cleanupPage, propertiesPage, undefined, property.name).delete();
      }

      const usersRes = await cleanupPage.request.post(`${BASE_URL}/api/admin/get-users`, {
        headers,
        data: { sort: 'Id', isSortDsc: false, pageIndex: 0, pageSize: 1000, offset: 0 },
        timeout: API_TIMEOUT,
      });
      const users: any[] = (await usersRes.json())?.model?.entities || [];
      const secondAdmin = users.find(u => u.email === secondAdminEmail);
      if (secondAdmin) {
        const deleteRes = await cleanupPage.request.get(`${BASE_URL}/api/admin/delete-user/${secondAdmin.id}`, {
          headers,
          timeout: API_TIMEOUT,
        });
        if (!(await deleteRes.json())?.success) {
          problems.push(`deleting ${secondAdminEmail} was refused`);
        }
      }
    };

    let timer: ReturnType<typeof setTimeout> | undefined;
    try {
      if (page) {
        const outcome = await Promise.race([
          cleanup(page).then(() => 'done' as const),
          new Promise<'timeout'>(resolve => {
            timer = setTimeout(() => resolve('timeout'), CLEANUP_BUDGET_MS);
          }),
        ]);
        if (outcome === 'timeout') {
          problems.push(`timed out after ${CLEANUP_BUDGET_MS}ms`);
        }
      }
    } catch (err: any) {
      problems.push(err?.message ?? String(err));
    } finally {
      if (timer) {
        clearTimeout(timer);
      }
      if (problems.length > 0) {
        console.log(
          '[property-worker-delete-first-user-only] afterAll cleanup INCOMPLETE (non-fatal) — may have left ' +
            `worker "${workerFullName}" (${worker.workerEmail}), property "${property.name}" and user ${secondAdminEmail}: ` +
            problems.join(' | ')
        );
      }
      if (page) {
        try { await page.close(); } catch {}
      }
    }
  });

  test('seed: property, worker and a second admin (as the first user)', async ({ page }) => {
    // 5 min: login (up to 2 min on a cold app), one property create, one
    // device-user create whose SDK provisioning is the slow part, and two API
    // calls. Every wait inside is individually bounded; this is only their sum.
    test.setTimeout(300000);

    await page.goto(BASE_URL, { timeout: APP_LOAD_TIMEOUT });
    await new LoginPage(page).login();

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await openPropertyWorkers(page);
    await workersPage.create(worker);
    await expect(
      workerRow(page),
      'the created worker must show up in the device-user table'
    ).toHaveCount(1, { timeout: UI_TIMEOUT });

    const createUserRes = await page.request.post(`${BASE_URL}/api/admin/create-user`, {
      headers: await firstUserApiHeaders(page),
      data: {
        id: 0,
        firstName: `SecondAdmin${rand}`,
        lastName: 'NotFirst',
        userName: secondAdminEmail,
        email: secondAdminEmail,
        password: secondAdminPassword,
        passwordConfimation: secondAdminPassword,
        role: 'admin',
        groupId: 0,
        isDeviceUser: false,
      },
      timeout: API_TIMEOUT,
    });
    expect(
      (await createUserRes.json().catch(() => null))?.success,
      `creating the second admin ${secondAdminEmail} must succeed`
    ).toBe(true);
  });

  test('a second admin, who holds the delete claim but is not the first user, is not offered Delete employee', async ({
    page,
  }) => {
    // 3 min: one login (up to 2 min for the app to land), the grid load and one
    // menu open, each individually bounded.
    test.setTimeout(180000);

    await loginThroughForm(page, secondAdminEmail, secondAdminPassword);
    await openPropertyWorkers(page);

    const row = workerRow(page);
    await expect(row, 'the seeded worker must be listed for the second admin').toHaveCount(1, {
      timeout: UI_TIMEOUT,
    });

    // mat-menu content is projected into a CDK overlay on <body>; the helper
    // addresses items by the row's action-items-<i> index inside that overlay.
    const menuItem = await openRowActionMenu(page, row, workerFullName);
    await expect(
      menuItem('editDeviceUserBtn'),
      'the menu must be open with the admin claims loaded ("Edit employee" is visible)'
    ).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(
      menuItem('deleteDeviceUserBtn'),
      'an admin who is not the first user must not be offered "Delete employee"'
    ).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('the first user is offered Delete employee, and the plugin accepts the delete', async ({ page }) => {
    // 4 min: login (up to 2 min), the grid load, then core's and the plugin's
    // SDK-backed deletes, each individually bounded.
    test.setTimeout(240000);

    await page.goto(BASE_URL, { timeout: APP_LOAD_TIMEOUT });
    await new LoginPage(page).login();
    await openPropertyWorkers(page);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    const row = workerRow(page);
    await expect(row, 'the seeded worker must still be listed').toHaveCount(1, { timeout: UI_TIMEOUT });

    const menuItem = await openRowActionMenu(page, row, workerFullName);
    await menuItem('deleteDeviceUserBtn').click({ timeout: UI_TIMEOUT });
    await workersPage.cancelDeleteBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });

    // The modal deletes through core first, then calls the plugin; this is the
    // plugin call that refuses everyone but the first user. Registered before the
    // click, so its budget also covers core's delete ahead of it.
    const pluginDelete = watchApiResponse(
      page,
      'DELETE /api/backend-configuration-pn/properties/assignment (plugin worker delete)',
      r => isCall(r, 'DELETE', '/api/backend-configuration-pn/properties/assignment'),
      SLOW_API_TIMEOUT
    );
    await workersPage.saveDeleteBtn().click({ timeout: UI_TIMEOUT });

    const response = await pluginDelete;
    const body = await response.json().catch(() => null);
    expect(body?.success, `the plugin must accept the first user's delete (message: ${body?.message})`).toBe(true);

    await workersPage.cancelDeleteBtn().waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    await expect(row, 'the deleted worker must leave the table').toHaveCount(0, { timeout: API_TIMEOUT });
  });
});
