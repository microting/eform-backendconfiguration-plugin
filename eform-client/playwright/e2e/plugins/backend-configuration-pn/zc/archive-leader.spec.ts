import { APIRequestContext, Browser, BrowserContext, expect, Page, Response, test } from '@playwright/test';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import { BackendConfigurationPropertyWorkersPage, PropertyWorker } from '../BackendConfigurationPropertyWorkers.page';
import {
  ARCHIVE_API, ArchiveCapabilities, BackendConfigurationArchivePage, CAPABILITIES_PATH, expectSuccess, FILES_PATH, FOLDERS_PATH,
} from '../BackendConfigurationArchive.page';
import {
  ADMIN_EMAIL, ADMIN_PASSWORD, apiToken, BASE_URL, call, callOk, createUserInNewGroup, loginAs,
} from '../archive-api-helpers';
import { tinyPdf } from '../inbox-hub-seed';
import { clickMenuItem, openRowActionMenu } from '../row-action-menu';
import {
  API_TIMEOUT, ignoreUnhandledRejections, leaveCurrentDocument, SLOW_API_TIMEOUT, UI_TIMEOUT, waitForApiResponse,
} from '../wait-helpers';

/**
 * Leder (shard zc), per worker (amendment D-L15-D-L19). An admin sets Leder on any worker in Medarbejdere: one with no
 * Web or Arkiv (nothing else changes), and sees the toggle on a worker whose login is an admin. The archive Leder (an
 * archive-only login, not an admin, and not in the folder's worker group) then manages Arkiv: the restricted folder,
 * the ⋮ folder menu, Indbakke, etiket rename/delete, and deleting a document. A normal archive user in the worker group
 * reads that document but cannot delete it or reach Indbakke. A non-admin in Medarbejdere sees the Leder column but
 * cannot set Leder, change worker groups or move a Leder to another address. The flag follows the worker's site: an
 * admin moving the login to another group keeps the Leder. A revoke takes effect on the Leder's next request.
 */
const rand = generateRandmString(5);
const lower = rand.toLowerCase();
const property: PropertyCreateUpdate = {
  name: `Leder ${rand}`, chrNumber: generateRandmString(5), address: 'Sydvej 3, 8000 Aarhus C', cvrNumber: '1111111',
};
const groupName = `Ledelse-${rand}`;
const restricted = `Ledelse-mappe-${rand}`;
const doc = `Referat-${rand}`;
const etiket = `Etiket-${rand}`;
// Generated at run time: GitGuardian flags any committed password-shaped literal. "Aa1" meets the Identity policy.
const PASSWORD = `Aa1${generateRandmString(12)}`;
const reader: PropertyWorker = {
  name: `Nora${rand}`, surname: 'Doe', language: 'Dansk', properties: [property.name!],
  workerEmail: `nora-${lower}@example.com`, tags: [groupName], archiveEnabled: true,
};
const leader: PropertyWorker = {
  name: `Lea${rand}`, surname: 'Doe', language: 'Dansk', properties: [property.name!],
  workerEmail: `lea-${lower}@example.com`, archiveEnabled: true,
};
// Neither Web nor Arkiv: D-L16 lets an admin make this worker a Leder anyway.
const plain: PropertyWorker = {
  name: `Pia${rand}`, surname: 'Doe', language: 'Dansk', properties: [property.name!],
  workerEmail: `pia-${lower}@example.com`,
};
// Its login gets the admin role in beforeAll: the toggle used to be hidden on such a worker.
const boss: PropertyWorker = {
  name: `Bo${rand}`, surname: 'Doe', language: 'Dansk', properties: [property.name!],
  workerEmail: `bo-${lower}@example.com`, archiveEnabled: true,
};
const staffEmail = `staff-${lower}@example.com`;
const readerName = `${reader.name} ${reader.surname}`;
const leaderName = `${leader.name} ${leader.surname}`;
const plainName = `${plain.name} ${plain.surname}`;
const bossName = `${boss.name} ${boss.surname}`;
const WORKERS_URL = `${BASE_URL}/plugins/backend-configuration-pn/property-workers`;
const UPDATE_DEVICE_USER = '/api/backend-configuration-pn/properties/assignment/update-device-user';
const isIndex = (r: Response) => r.url().includes('/properties/assignment/index-device-user') && r.request().method() === 'POST';
const rowOf = (rows: any[], email: string) => rows.find(r => r.workerEmail === email);

async function setPassword(request: APIRequestContext, adminToken: string, email: string): Promise<void> {
  await callOk(request, 'POST', '/api/account/change-password-admin', adminToken, `setting the password of ${email}`,
    { data: { newPassword: PASSWORD, confirmPassword: PASSWORD, email } });
}

/** Saves the login of `email` through the core Users endpoint with `changes` applied (a group or a role). */
async function updateLogin(request: APIRequestContext, adminToken: string, email: string,
  changes: Record<string, unknown>, what: string): Promise<void> {
  const users = await call(request, 'POST', '/api/admin/get-users', adminToken, 'listing users',
    { data: { sort: 'Id', isSortDsc: true, pageIndex: 0, pageSize: 10000, offset: 0 } });
  const userId: number = (users?.model?.entities ?? []).find((u: any) => u.email === email)?.id ?? 0;
  expect(userId, `login id of ${email}`).toBeGreaterThan(0);
  const current = await call(request, 'GET', `/api/admin/user/${userId}`, adminToken, `reading the login of ${email}`);
  await callOk(request, 'POST', '/api/admin/update-user', adminToken, what, {
    data: { ...current.model, ...changes, password: '', passwordConfimation: '' }, timeout: SLOW_API_TIMEOUT,
  });
}

/** Waits for a refused save: HTTP 200 (named with its body otherwise) and `success: false`. Returns the sent body. */
async function expectRefused(response: Promise<Response>, what: string): Promise<any> {
  const resp = await response;
  const text = await resp.text();
  expect(resp.status(), `${what}: HTTP ${resp.status()} ${text}`).toBe(200);
  const body = JSON.parse(text);
  expect(body?.success, `${what} is refused (${body?.message ?? ''})`).toBe(false);
  return JSON.parse(resp.request().postData() || '{}');
}

test.describe.serial('Leder in Arkiv and Medarbejdere', () => {
  const contexts: BrowserContext[] = [];
  let admin: Page;
  let workers: BackendConfigurationPropertyWorkersPage;
  let adminToken = '';
  let staffGroupId = 0;
  let leaderToken = '';

  async function newPage(browser: Browser): Promise<Page> {
    const context = await browser.newContext();
    contexts.push(context);
    return context.newPage();
  }

  /** Signs in (Kun arkiv lands on the archive) and opens Arkiv; returns the page and the capabilities. */
  async function openArchiveAs(browser: Browser, email: string):
    Promise<{ page: Page; archive: BackendConfigurationArchivePage; capabilities: ArchiveCapabilities }> {
    const page = await newPage(browser);
    await loginAs(page, email, PASSWORD);
    const archive = new BackendConfigurationArchivePage(page);
    const capabilities = await archive.open();
    return { page, archive, capabilities };
  }

  /**
   * Loads Medarbejdere on `page`; returns the index-device-user rows. A page load, not a menu click: the menu does
   * not reload a list that is already shown.
   */
  async function listWorkers(page: Page, who: string): Promise<any[]> {
    await leaveCurrentDocument(page);
    const listed = waitForApiResponse(page, `POST index-device-user (Medarbejdere as ${who})`, isIndex, API_TIMEOUT);
    ignoreUnhandledRejections(listed);
    await page.goto(WORKERS_URL, { timeout: API_TIMEOUT });
    return (await expectSuccess(listed, `listing Medarbejdere as ${who}`)).model;
  }

  test.beforeAll(async ({ browser }) => {
    // Cold admin login, a property, a group, four SDK workers (two more than before the per-worker change, ~30 s
    // each), a folder, an upload, a core user update and ~20 API calls.
    test.setTimeout(540000);
    admin = await newPage(browser);
    await loginAs(admin, ADMIN_EMAIL, ADMIN_PASSWORD, { landing: '#newEFormBtn' });
    adminToken = await apiToken(admin.request, ADMIN_EMAIL, ADMIN_PASSWORD);

    const properties = new BackendConfigurationPropertiesPage(admin);
    await properties.goToProperties();
    await properties.createProperty(property);
    workers = new BackendConfigurationPropertyWorkersPage(admin);
    await workers.goToPropertyWorkers();
    await workers.createTag(groupName);
    await workers.create(reader);
    await workers.create(leader);
    await workers.create(plain);
    await workers.create(boss);
    await setPassword(admin.request, adminToken, reader.workerEmail!);
    await setPassword(admin.request, adminToken, leader.workerEmail!);
    await updateLogin(admin.request, adminToken, boss.workerEmail!, { role: 'admin' }, 'giving the boss login the admin role');

    // Restricted to the group only the reader carries: the Leder reads it as a manager, not as a member.
    const folder = await callOk(admin.request, 'POST', FOLDERS_PATH, adminToken, `creating folder ${restricted}`,
      { data: { name: restricted, parentId: null } });
    const groups = await call(admin.request, 'GET', `${ARCHIVE_API}/worker-tags`, adminToken, 'listing worker groups');
    const tagId = (groups?.model ?? []).find((t: any) => t.name === groupName)?.id ?? 0;
    expect(tagId, `id of worker group ${groupName}`).toBeGreaterThan(0);
    await callOk(admin.request, 'PUT', `${FOLDERS_PATH}/${folder.model.id}/access`, adminToken, `restricting ${restricted}`,
      { data: { tagIds: [tagId] } });
    await callOk(admin.request, 'POST', `${ARCHIVE_API}/file-tags`, adminToken, `creating etiket ${etiket}`,
      { data: { name: etiket } });

    const archive = new BackendConfigurationArchivePage(admin);
    await archive.open();
    await archive.selectFolder(restricted);
    await archive.uploadPdf(doc, tinyPdf(doc), property.name!, restricted);

    // A web user who may see, create and edit workers but is neither admin nor Leder (L4). Time-planning access
    // too: the worker dialog reads time-planning settings, and a 403 there would end the session. L5 moves the
    // Leder's login into this group.
    staffGroupId = await createUserInNewGroup(admin.request, adminToken, {
      groupName: `medarbejdere-${lower}`, email: staffEmail, password: PASSWORD, firstName: 'Jane', lastName: `Staff ${rand}`,
      coreClaims: ['device_users_read', 'device_users_create', 'device_users_update', 'eforms_read', 'cases_read', 'case_read'],
      pluginClaims: {
        'eform-backend-configuration-plugin': ['backend_configuration_plugin_access', 'properties_get'],
        'eform-angular-time-planning-plugin': ['time_planning_plugin_access', 'time_planning_flex_get', 'time_planning_working_hours_get'],
      },
      redirectLink: '/plugins/backend-configuration-pn/property-workers',
    });
  });

  test.afterAll(async () => {
    for (const context of contexts) {
      await context.close();
    }
  });

  test('L1 an admin sets Leder on any worker: the toggle is always enabled, nothing else changes, the column shows Ja', async () => {
    // Two list page loads, three edit dialogs, two SDK-backed saves and their list refreshes.
    test.setTimeout(240000);
    const rows = await listWorkers(admin, 'the admin');
    expect(rowOf(rows, plain.workerEmail!), 'the no-access worker as the admin sees it').toMatchObject({
      isLeader: false, webAccessEnabled: false, archiveEnabled: false, canSetLeder: true, canEditWorkerTags: true,
    });
    expect(rowOf(rows, boss.workerEmail!), 'the admin-login worker').toMatchObject({ isLeader: false, isAdminLogin: true });
    await workers.expectLeaderCell(plainName, false);
    await workers.expectLeaderCell(readerName, false);

    // An admin login gets the toggle too (amendment §5).
    const dialog = admin.locator('mat-dialog-container');
    await workers.openEditModalFor(bossName);
    await expect(workers.leaderSwitch()).toBeEnabled({ timeout: UI_TIMEOUT });
    await dialog.locator('#cancelEditBtn').click({ timeout: UI_TIMEOUT });
    await expect(dialog).toHaveCount(0, { timeout: UI_TIMEOUT });

    // Neither Web nor Arkiv: the toggle is enabled all the same (D-L16).
    await workers.openEditModalFor(plainName);
    await expect(workers.archiveSwitch()).toHaveAttribute('aria-checked', 'false', { timeout: UI_TIMEOUT });
    await expect(workers.leaderSwitch()).toBeEnabled({ timeout: UI_TIMEOUT });
    await dialog.locator('#cancelEditBtn').click({ timeout: UI_TIMEOUT });
    await expect(dialog).toHaveCount(0, { timeout: UI_TIMEOUT });

    await workers.setLeader(plainName, true);
    await workers.expectLeaderCell(plainName, true);
    await workers.expectLeaderCell(readerName, false);
    const after = await listWorkers(admin, 'the admin');
    expect(rowOf(after, plain.workerEmail!), 'Leder stored, no access switched on').toMatchObject({
      isLeader: true, webAccessEnabled: false, archiveEnabled: false,
    });

    // The archive Leder of L2-L6.
    await workers.setLeader(leaderName, true);
    await workers.expectLeaderCell(leaderName, true);
  });

  test('L2 a normal archive user reads the restricted document but cannot delete it or reach Indbakke', async ({ browser }) => {
    // Cold login, two page loads and one API call.
    test.setTimeout(180000);
    const { page, archive, capabilities } = await openArchiveAs(browser, reader.workerEmail!);
    expect(capabilities).toEqual({ canManage: false, canDelete: false, canUseInbox: false, isFirstUser: false });
    await expect(page.locator('#archiveNewFolderBtn')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(page.locator('[id^="archiveFolderMenuBtn-"]')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(page.locator('#archiveNavInbox')).toHaveCount(0, { timeout: UI_TIMEOUT });

    await archive.selectFolder(restricted);
    const row = archive.fileRow(doc);
    await expect(row).toHaveCount(1, { timeout: UI_TIMEOUT });
    const item = await openRowActionMenu(page, row, `document ${doc}`);
    await expect(item('viewPdfBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(item('deleteFileBtn')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await archive.closeMenu();

    // The server says the same, whatever the UI shows (D-L4).
    const fileId = await row.getAttribute('data-file-id', { timeout: UI_TIMEOUT });
    const token = await apiToken(page.request, reader.workerEmail!, PASSWORD);
    await call(page.request, 'DELETE', `${FILES_PATH}/${fileId}`, token, 'DELETE files/{id} as a normal user', { status: 403 });
  });

  test('L3 the Leder manages Arkiv and deletes the document', async ({ browser }) => {
    // Cold login, three page loads, the etiketter dialog and one delete.
    test.setTimeout(180000);
    const { page, archive, capabilities } = await openArchiveAs(browser, leader.workerEmail!);
    expect(capabilities).toEqual({ canManage: true, canDelete: true, canUseInbox: true, isFirstUser: false });
    const folderId = await archive.folderId(restricted);
    await expect(page.locator('#archiveNewFolderBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(page.locator(`#archiveFolderMenuBtn-${folderId}`)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(page.locator('#archiveNavInbox')).toBeVisible({ timeout: UI_TIMEOUT });

    // Etiketter: rename and delete are offered to a Leder (D-L11).
    await archive.openTagsDialog();
    const tagRow = archive.tagRow(etiket);
    await expect(tagRow).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(tagRow.locator('#editTagBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(tagRow.locator('#deleteTagBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await archive.closeTagsDialog();

    // Indbakke opens: CanManageGuard lets the Leder in and GET inbox answers.
    await archive.openInbox();
    await expect(page.locator('#archiveNavSettings')).toBeVisible({ timeout: UI_TIMEOUT });

    await archive.open();
    await archive.selectFolder(restricted);
    const row = archive.fileRow(doc);
    await expect(row).toHaveCount(1, { timeout: UI_TIMEOUT });
    const fileId = await row.getAttribute('data-file-id', { timeout: UI_TIMEOUT });
    const item = await openRowActionMenu(page, row, `document ${doc}`);
    await clickMenuItem(page, item('deleteFileBtn'), `Slet on the row of ${doc}`);
    await archive.expectOneOpenDialog(`the delete dialog of ${doc}`);
    const deleted = archive.waitForCall(`deleting ${doc}`, 'DELETE', `${FILES_PATH}/${fileId}`);
    const listed = archive.waitForCall('archive documents after the delete', 'POST', FILES_PATH);
    await archive.clickInDialog('deleteFileBtn');
    await expectSuccess(deleted, `deleting ${doc}`);
    await expectSuccess(listed, 'listing the archive after the delete');
    await expect(row).toHaveCount(0, { timeout: UI_TIMEOUT });

    leaderToken = await apiToken(page.request, leader.workerEmail!, PASSWORD);
  });

  test('L4 a non-admin sees the Leder column but cannot set Leder, worker groups or a Leder\'s address', async ({ browser }) => {
    // Cold login onto the list, an edit and a create dialog, one refused SDK-backed create and one refused API save.
    test.setTimeout(240000);
    const page = await newPage(browser);
    // The group lands on Medarbejdere. Its list call is taken from that landing, not from a reload: on a reload
    // straight after sign-in the route guards cancelled the initial navigation and left a blank page (CI, zc L4).
    const landed = await loginAs(page, staffEmail, PASSWORD, {
      landing: '#newDeviceUserBtn',
      landingCall: { what: 'POST index-device-user (Medarbejdere as a non-admin)', predicate: isIndex },
    });
    const rows = (await expectSuccess(landed!, 'listing Medarbejdere as a non-admin')).model;
    expect(rowOf(rows, leader.workerEmail!), 'the Leder row as a non-admin sees it').toMatchObject({
      isLeader: true, canSetLeder: false, canEditWorkerTags: false,
    });
    expect(rowOf(rows, plain.workerEmail!), 'the no-access Leder').toMatchObject({ isLeader: true });
    expect(rowOf(rows, reader.workerEmail!), 'the reader').toMatchObject({ isLeader: false });

    const staffWorkers = new BackendConfigurationPropertyWorkersPage(page);
    await staffWorkers.expectLeaderCell(leaderName, true);
    await staffWorkers.expectLeaderCell(readerName, false);
    await expect(page.locator('#bulkWorkerTagsBtn')).toHaveCount(0, { timeout: UI_TIMEOUT });

    await staffWorkers.openEditModalFor(leaderName);
    const dialog = page.locator('mat-dialog-container');
    await expect(dialog.locator('#leaderToggle')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(dialog.locator('#tagSelector')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(dialog.locator('#workerEmail'), 'the Leder\'s login is read-only for a non-admin').not.toBeEditable({ timeout: UI_TIMEOUT });
    await expect(dialog.locator('#leaderLoginHint')).toBeVisible({ timeout: UI_TIMEOUT });
    await dialog.locator('#cancelEditBtn').click({ timeout: UI_TIMEOUT });
    await expect(dialog).toHaveCount(0, { timeout: UI_TIMEOUT });

    // H2 on the server: the read-only field is only the UI. The row as listed, with another address and no isLeader.
    const staffToken = await apiToken(page.request, staffEmail, PASSWORD);
    const moved = { ...rowOf(rows, leader.workerEmail!), workerEmail: `moved-${lower}@example.com`, isLeader: null };
    const refused = await call(page.request, 'POST', UPDATE_DEVICE_USER, staffToken,
      'update-device-user moving a Leder to another address as a non-admin', { data: moved, timeout: SLOW_API_TIMEOUT });
    expect(refused?.success, `a non-admin cannot move a Leder to another address (${refused?.message ?? ''})`).toBe(false);
    expect(refused?.message).toMatch(/Kun en administrator kan ændre en leders login|Only an administrator can change a leader's login/);

    // A refused create keeps its dialog open. The Leder's address already belongs to a worker, so the API refuses
    // before any write.
    await staffWorkers.openCreateModal({ name: `Nina${rand}`, surname: 'Doe', language: 'Dansk', workerEmail: leader.workerEmail });
    const create = waitForApiResponse(page, 'PUT create-device-user (refused create)',
      r => r.url().includes('/properties/assignment/create-device-user') && r.request().method() === 'PUT', SLOW_API_TIMEOUT);
    ignoreUnhandledRejections(create);
    await expect(staffWorkers.saveCreateBtn()).toBeEnabled({ timeout: UI_TIMEOUT });
    await staffWorkers.saveCreateBtn().click({ timeout: UI_TIMEOUT });
    await expectRefused(create, 'create-device-user with the Leder\'s address');
    await expect(page.locator('#toast-container .toast-error',
      { hasText: /E-mailadressen er allerede i brug|The email address is already in use/ }))
      .toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(staffWorkers.saveCreateBtn()).toBeVisible({ timeout: UI_TIMEOUT });
    await staffWorkers.cancelCreateBtn().click({ timeout: UI_TIMEOUT });
    await expect(dialog).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('L5 the Leder stays a Leder after an admin moves the login to another group on the core Users endpoint', async () => {
    // Three core admin calls (update-user is SDK-backed), one capabilities call and a list page load.
    test.setTimeout(120000);
    await updateLogin(admin.request, adminToken, leader.workerEmail!, { groupId: staffGroupId },
      'moving the Leder login to another group');

    // The flag belongs to the worker's site, which the core Users page cannot touch.
    const capabilities = await callOk(admin.request, 'GET', CAPABILITIES_PATH, leaderToken, 'capabilities after the group change');
    expect(capabilities.model?.canManage, 'canManage after the group change').toBe(true);
    const rows = await listWorkers(admin, 'the admin');
    expect(rowOf(rows, leader.workerEmail!), 'the Leder row after the group change').toMatchObject({
      isLeader: true, loginIsManaged: false,
    });
    await workers.expectLeaderCell(leaderName, true);
  });

  test('L6 a revoke takes effect on the Leder\'s next request, with the same token', async () => {
    // A list page load, one edit dialog, one SDK-backed save and two API calls.
    test.setTimeout(120000);
    expect(leaderToken, 'the Leder signed in in L3').not.toBe('');
    await listWorkers(admin, 'the admin');
    // The login is now in an owner group (unmanaged); the toggle is enabled anyway.
    await workers.setLeader(leaderName, false);
    await workers.expectLeaderCell(leaderName, false);

    // No new token: the API checks the flag on every request (D-L6).
    await call(admin.request, 'GET', `${ARCHIVE_API}/inbox`, leaderToken, 'GET inbox with the former Leder\'s token', { status: 403 });
    const capabilities = await callOk(admin.request, 'GET', CAPABILITIES_PATH, leaderToken, 'capabilities of the former Leder');
    expect(capabilities.model?.canManage, 'canManage after the revoke').toBe(false);
  });
});
