import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import { TaskListPage } from '../task-list.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';
import { assigneeWorkerOptions } from '../calendar-assignee.helper';
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';

/**
 * Task list — the eForm `user` role and the #1140 read-only fix (#1302).
 *
 * Before #1302 the task-list route carried `canActivate: [IsAdminGuard]` and
 * this suite pinned that a non-admin could not reach it. Now:
 *  - the route is `AuthGuard` — every logged-in user opens the page;
 *  - the BATCH UI is admin-only: `#taskListBatchAction`, the selection counter
 *    `#taskListSelectionCount` and the grid's checkbox column are absent for a
 *    user (the batch endpoints stay admin-only server-side);
 *  - everything else stays: the edit modal (edit + save), inline rename
 *    (`task-list/rename`, now open to users), Manage tags, CSV export;
 *  - #1140, for everyone: a series that started in the past opens the edit
 *    modal EDITABLE, on its next upcoming occurrence (it used to open on the
 *    series start, which the modal locks as past).
 *
 * UG1: as `user` the page opens; batch dropdown, counter and checkboxes absent.
 * UG2: as `user` inline rename works (POST task-list/rename succeeds).
 * UG3: as `user` edit + save through the modal works; the recurring task shows
 *      the this / this-and-following / all dialog, as in the calendar.
 * AG1: as admin, a series re-anchored into the past (batch "change start date")
 *      opens the edit modal editable and Save reaches the scope dialog.
 *
 * `loginViaApi`/`loginAs`/`setupNonAdminUser` are the same minimal non-admin
 * setup the pre-#1302 version of this file used (copied from
 * `r/property-workers-nonadmin-no-logout.spec.ts`).
 */

const BASE_URL = 'http://localhost:4200';
const USER_PASSWORD = 'Secret_password_2026!';

// Danish label of the batch entry used by AG1 (da.ts: 'Change start date':
// 'Skift startdato') — the batch dropdown is the one place task-list specs
// match on translated text (ng-select options carry no per-option id).
const LABEL_CHANGE_START_DATE = 'Skift startdato';
const MONTHS_BACK = 2;

const property: PropertyCreateUpdate = {
  name: `tlu-${generateRandmString(5)}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const worker: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};

const rand = generateRandmString(6).toLowerCase();
// Two weekly tasks: one the USER edits (UG2/UG3), one the ADMIN re-anchors
// into the past (AG1), so the suites never depend on each other's edits.
let userTask = `tlu-user-${rand}`;
const pastTask = `tlu-past-${rand}`;

let seeded = false;
let userEmail = '';

async function loginViaApi(page: Page, email: string, password: string): Promise<string> {
  const res = await page.request.post(`${BASE_URL}/api/auth/token`, {
    form: { username: email, password: password, grant_type: 'password' },
    timeout: API_TIMEOUT,
  });
  const json = await res.json();
  return json?.model?.accessToken || '';
}

async function loginAs(page: Page, email: string, password: string): Promise<void> {
  const loginBtn = page.locator('#loginBtn');
  await loginBtn.waitFor({ state: 'visible', timeout: 60000 });
  await page.locator('#username').fill(email);
  await page.locator('#password').fill(password);
  const loginResponsePromise = page.waitForResponse(
    (r) => r.url().includes('/api/auth/token'),
    { timeout: 30000 },
  ).catch(() => null);
  await loginBtn.click();
  await loginResponsePromise;
  // Wait for the post-login redirect to leave /auth (group redirectLink).
  await page.waitForURL((url) => !url.pathname.startsWith('/auth'), { timeout: 20000 })
    .catch(() => console.log(`loginAs ${email}: still on ${page.url()}`));
  await page.waitForTimeout(1000);
}

/**
 * A security group with the reported customer's minimal claims (core device
 * users + eForm tags/read, backend-configuration + time-planning plugin
 * access) and a `user`-role web user in it. All via admin API calls.
 */
async function setupNonAdminUser(page: Page): Promise<string> {
  const token = await loginViaApi(page, 'admin@admin.com', 'secretpassword');
  const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
  const groupName = `tl-user-${rand}`;
  const email = `tluser-${rand}@test.com`;

  await page.request.post(`${BASE_URL}/api/security/groups`, {
    headers, data: { userIds: [], name: groupName }, timeout: API_TIMEOUT,
  });
  const indexRes = await page.request.post(`${BASE_URL}/api/security/groups/index`, {
    headers,
    data: { sort: 'Id', nameFilter: groupName, pageIndex: 0, pageSize: 100, isSortDsc: false, offset: 0 },
    timeout: API_TIMEOUT,
  });
  const groups = (await indexRes.json())?.model?.entities || [];
  const groupId = groups.find((g: any) => g.groupName === groupName)?.id || 0;
  expect(groupId).toBeGreaterThan(0);

  const coreClaims = [
    'device_users_read', 'device_users_create', 'device_users_update',
    'eforms_read_tags', 'eforms_update_tags',
    'eforms_read', 'cases_read', 'case_read',
  ];
  const permsRes = await page.request.get(`${BASE_URL}/api/security/permissions/${groupId}`,
    { headers, timeout: API_TIMEOUT });
  const permTypes = (await permsRes.json())?.model?.permissionTypes || [];
  const permissions: any[] = [];
  for (const pt of permTypes) {
    for (const p of pt.permissions || []) {
      permissions.push({ ...p, isEnabled: p.isEnabled || coreClaims.includes(p.claimName) });
    }
  }
  await page.request.put(`${BASE_URL}/api/security/permissions`, {
    headers, data: { groupId, permissions }, timeout: API_TIMEOUT,
  });

  await page.request.put(`${BASE_URL}/api/security/groups/settings`, {
    headers,
    data: { id: groupId, redirectLink: '/plugins/backend-configuration-pn/property-workers' },
    timeout: API_TIMEOUT,
  });

  const pluginsRes = await page.request.get(
    `${BASE_URL}/api/plugins-management/installed?sort=id&isSortDsc=true&pageSize=1000&pageIndex=0&offset=0`,
    { headers, timeout: API_TIMEOUT },
  );
  const plugins = (await pluginsRes.json())?.model?.pluginsList || [];
  const wantedPluginClaims: Record<string, string[]> = {
    'eform-backend-configuration-plugin': [
      'backend_configuration_plugin_access', 'properties_get',
      'time_registration_enable', 'task_management_enable', 'document_management_enable',
    ],
    'eform-angular-time-planning-plugin': [
      'time_planning_plugin_access', 'time_planning_flex_get', 'time_planning_working_hours_get',
    ],
  };
  for (const plugin of plugins) {
    const wanted = wantedPluginClaims[plugin.pluginId];
    if (!wanted) continue;
    const permUrl = `${BASE_URL}/api/plugins-permissions/group-permissions/${plugin.id}`;
    const currentPerms = (await (await page.request.get(permUrl, { headers, timeout: API_TIMEOUT })).json())?.model || [];
    const permIdMap: Record<string, number> = {};
    for (const gp of currentPerms) {
      for (const perm of gp.permissions || []) {
        permIdMap[perm.claimName] = perm.permissionId;
      }
    }
    const pluginPerms = wanted.map((claimName, i) => ({
      isEnabled: true,
      claimName,
      permissionId: permIdMap[claimName] || i + 1,
      permissionName: claimName,
    }));
    await page.request.put(permUrl, {
      headers, data: [{ permissions: pluginPerms, groupId }], timeout: API_TIMEOUT,
    });
  }

  const createUserRes = await page.request.post(`${BASE_URL}/api/admin/create-user`, {
    headers,
    data: {
      id: 0,
      firstName: `TlUser${rand}`,
      lastName: 'TaskList',
      userName: email,
      email,
      password: USER_PASSWORD,
      passwordConfimation: USER_PASSWORD,
      role: 'user',
      groupId,
      isDeviceUser: false,
    },
    timeout: API_TIMEOUT,
  });
  const createUserJson = await createUserRes.json().catch(() => null);
  console.log(`create-user ${email}: status=${createUserRes.status()} success=${createUserJson?.success}`);
  expect(createUserJson?.success).toBe(true);
  return email;
}

/**
 * A WEEKLY event on Monday of next week at 09:00 (same sequence as
 * `p/calendar-edit-scope.spec.ts` createWeeklyEvent).
 */
async function createWeeklyEvent(page: Page, calendarPage: CalendarUiEnhancementsPage, title: string, dayOffset: number) {
  await calendarPage.openCreateModalAtSlot(dayOffset, 9);
  await page.locator('#calendarEventTitle').fill(title);

  await page.locator('#calendarEventEform').click();
  await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await page.locator('.ng-dropdown-panel .ng-option').first().click();
  await page.waitForTimeout(300);

  await page.locator('#calendarEventPlanningTag').click();
  await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await page.locator('.ng-dropdown-panel .ng-option').first().click();
  await page.waitForTimeout(300);

  await page.locator('#calendarEventAssignee').click();
  await page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await assigneeWorkerOptions(page).first().click();
  await page.locator('#calendarEventTitle').click();
  await page.waitForTimeout(300);

  await calendarPage.setRepeatToWeekly();

  const createResp = page.waitForResponse(
    r => r.url().includes('/api/backend-configuration-pn/calendar/tasks')
      && !r.url().includes('/tasks/week')
      && r.request().method() === 'POST',
    { timeout: API_TIMEOUT },
  );
  await page.locator('#calendarEventSaveBtn').click();
  await createResp;
  await page.waitForTimeout(1500);
  await calendarPage.findEventBlock(title).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
}

/** The edit modal is open and NOT read-only (the modal hides its action row when read-only). */
async function expectEditableModal(page: Page) {
  await expect(page.locator('#calendarEventSaveBtn')).toBeVisible({ timeout: UI_TIMEOUT });
  await expect(page.locator('#calendarEventTitle')).toBeEnabled({ timeout: UI_TIMEOUT });
}

test.describe.serial('Task list — user role + past-started series (#1302)', () => {
  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage().catch((err: any) => {
      console.log(`afterAll cleanup failed (non-fatal): could not open a cleanup page: ${err?.message ?? err}`);
      return undefined;
    });
    if (!page) {
      return;
    }
    const cleanup = async () => {
      await page.goto(BASE_URL);
      await new LoginPage(page).login();

      const workersPage = new BackendConfigurationPropertyWorkersPage(page);
      await workersPage.goToPropertyWorkers();
      await page.waitForTimeout(1000);
      await workersPage.clearTable();

      const propertiesPage = new BackendConfigurationPropertiesPage(page);
      await propertiesPage.goToProperties();
      await page.waitForTimeout(1000);
      await propertiesPage.clearTable();
    };
    try {
      await Promise.race([
        cleanup(),
        new Promise(resolve => setTimeout(resolve, 60000)),
      ]);
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    }
    try { await page.close(); } catch {}
  });

  // -----------------------------------------------------------------------
  // Seed (as admin) — property, worker, two weekly tasks, a `user`-role user.
  // -----------------------------------------------------------------------
  test('seed: property + worker + two weekly tasks + user-role user (as admin)', async ({ page }) => {
    test.setTimeout(600000);
    await page.goto(BASE_URL);
    await new LoginPage(page).login();
    await page.waitForTimeout(1500);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.selectProperty(property.name);
    await page.waitForTimeout(1000);
    await createWeeklyEvent(page, calendarPage, userTask, 0);
    // openCreateModalAtSlot advances one more week, so this one lands on the
    // Tuesday two weeks out — a distinct, empty slot.
    await createWeeklyEvent(page, calendarPage, pastTask, 1);

    userEmail = await setupNonAdminUser(page);
    seeded = true;
  });

  // =======================================================================
  // UG1 — the page opens for a user; the batch UI does not exist.
  // =======================================================================
  test('UG1: user opens the task list; batch dropdown, counter and checkbox column are absent', async ({ page }) => {
    test.setTimeout(300000);
    expect(seeded).toBe(true);

    await page.goto(BASE_URL);
    await loginAs(page, userEmail, USER_PASSWORD);
    expect(page.url()).not.toContain('/auth');

    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await expect(page.locator('app-task-list-page')).toHaveCount(1, { timeout: UI_TIMEOUT });
    await taskListPage.search(rand);
    await expect(taskListPage.row(userTask)).toBeVisible({ timeout: UI_TIMEOUT });

    await expect(page.locator('#taskListBatchAction')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(page.locator('#taskListSelectionCount')).toHaveCount(0, { timeout: UI_TIMEOUT });
    // No checkbox column: neither the header "select all" nor per-row boxes.
    await expect(taskListPage.getGrid().locator('mat-checkbox')).toHaveCount(0, { timeout: UI_TIMEOUT });

    // Status quo for everything the spec did not mention.
    await expect(page.locator('#taskListManageTagsBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(page.locator('#taskListCsvExportBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(page.locator(`[id^="taskListEditModalBtn-"]`).first()).toBeAttached({ timeout: UI_TIMEOUT });
  });

  // =======================================================================
  // UG2 — inline rename works for a user (task-list/rename is now open).
  // =======================================================================
  test('UG2: user renames a task inline', async ({ page }) => {
    test.setTimeout(300000);
    expect(seeded).toBe(true);

    await page.goto(BASE_URL);
    await loginAs(page, userEmail, USER_PASSWORD);
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.search(rand);
    await expect(taskListPage.row(userTask)).toBeVisible({ timeout: UI_TIMEOUT });

    const renamed = `${userTask}-rn`;
    const renameResp = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/task-list/rename')
        && r.request().method() === 'POST',
      { timeout: API_TIMEOUT },
    );
    await taskListPage.startInlineRename(userTask);
    await taskListPage.setInlineRenameValue(renamed);
    await taskListPage.commitInlineRenameWithEnter();
    const resp = await renameResp;
    expect(resp.status(), 'task-list/rename must not 403 for the user role').toBe(200);
    const body = await resp.json();
    expect(body.success, `rename returned success=false: ${body.message}`).toBe(true);

    await expect(taskListPage.titleText(renamed)).toBeVisible({ timeout: UI_TIMEOUT });
    userTask = renamed;
  });

  // =======================================================================
  // UG3 — a user edits and saves through the modal; recurring → scope dialog.
  // =======================================================================
  test('UG3: user edits + saves a recurring task via the modal (scope dialog shown)', async ({ page }) => {
    test.setTimeout(300000);
    expect(seeded).toBe(true);

    await page.goto(BASE_URL);
    await loginAs(page, userEmail, USER_PASSWORD);
    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    // A single property filter loads its calendars, which the modal needs.
    await taskListPage.selectProperty(property.name);
    await taskListPage.search(rand);
    await expect(taskListPage.row(userTask)).toBeVisible({ timeout: UI_TIMEOUT });

    await taskListPage.openEditModal(userTask);
    await expectEditableModal(page);

    const edited = `${userTask}-ed`;
    await page.locator('#calendarEventTitle').fill(edited);

    const putResp = page.waitForResponse(
      r => r.url().endsWith('/api/backend-configuration-pn/calendar/tasks')
        && r.request().method() === 'PUT',
      { timeout: API_TIMEOUT },
    );
    const reload = page.waitForResponse(
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/index'),
      { timeout: API_TIMEOUT },
    ).catch(() => null);
    await page.locator('#calendarEventSaveBtn').click();

    // Same this / this and following / all dialog as the calendar.
    await expect(page.locator('#repeatScopeConfirmBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await page.locator('mat-radio-button[value="all"] input').check({ timeout: UI_TIMEOUT });
    await page.locator('#repeatScopeConfirmBtn').click();

    const body = await (await putResp).json();
    expect(body.success, `updateTask returned success=false: ${body.message}`).toBe(true);
    await expect(page.locator('#calendarEventTitle')).toBeHidden({ timeout: UI_TIMEOUT });
    await reload;

    await expect(taskListPage.titleText(edited)).toBeVisible({ timeout: UI_TIMEOUT });
    userTask = edited;
  });

  // =======================================================================
  // AG1 — #1140, as admin: a series whose start is in the past opens the
  // edit modal EDITABLE (it opened read-only on its past series start).
  // =======================================================================
  test('AG1: admin — a past-started series opens editable and Save reaches the scope dialog', async ({ page }) => {
    test.setTimeout(300000);
    expect(seeded).toBe(true);

    await page.goto(BASE_URL);
    await new LoginPage(page).login();
    await page.waitForTimeout(1500);

    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await taskListPage.search(rand);
    await expect(taskListPage.row(pastTask)).toBeVisible({ timeout: UI_TIMEOUT });

    // Re-anchor the series into the past with the batch action.
    await taskListPage.selectRow(pastTask);
    await taskListPage.pickBatchAction(new RegExp(LABEL_CHANGE_START_DATE));
    const expectedDate = await taskListPage.pickPastStartDate(MONTHS_BACK);
    await taskListPage.waitForStartDatePreviewResolved();
    await expect(taskListPage.batchModalSubmitButton()).toBeEnabled({ timeout: UI_TIMEOUT });
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();
    await expect(taskListPage.columnCell(pastTask, 'taskDate')).toHaveText(expectedDate, { timeout: UI_TIMEOUT });

    // The fix: editable, not read-only.
    await taskListPage.openEditModal(pastTask);
    await expectEditableModal(page);

    // And Save gets as far as the recurring-scope dialog (a read-only modal
    // has no Save; the old past-date gate returned before this dialog).
    await page.locator('#calendarEventSaveBtn').click();
    await expect(page.locator('#repeatScopeConfirmBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await page.locator('#repeatScopeCancelBtn').click();
    await expect(page.locator('#repeatScopeConfirmBtn')).toBeHidden({ timeout: UI_TIMEOUT });
  });
});
