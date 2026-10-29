import { expect, Page, test } from '@playwright/test';
import { generateRandmString } from '../../../helper-functions';
import { ARCHIVE_URL, BackendConfigurationArchivePage, DEFAULT_FOLDER, FOLDERS_PATH } from '../BackendConfigurationArchive.page';
import { ADMIN_EMAIL, ADMIN_PASSWORD, apiToken, callOk, createUserInNewGroup, loginAs } from '../archive-api-helpers';
import { UI_TIMEOUT } from '../wait-helpers';

/**
 * Arkiv as a plain user (D5): folders can be navigated, but there is no "Ny mappe", no ⋮ folder menu, no
 * Læseadgang column and no Indbakke tab. The user has no linked worker, so it is not disabled (D7) and sees
 * the open folders. Setup goes through the admin API, the check through the UI.
 * N2: Indbakke and Indstillinger send a plain user back to the archive (CanManageGuard), and etiketter can be
 * created but not renamed or deleted (D-L11).
 */
// Generated at run time: GitGuardian flags any committed password-shaped literal. "Aa1" meets the Identity policy.
const USER_PASSWORD = `Aa1${generateRandmString(12)}`;
const rand = generateRandmString(6).toLowerCase();
const openFolder = `Faelles-${rand}`;
const etiket = `Etiket-${rand}`;

test.describe.serial('Arkiv as a non-admin', () => {
  let page: Page;
  let email = '';

  test.beforeAll(async ({ browser }) => {
    // About fifteen admin API calls (API_TIMEOUT each).
    test.setTimeout(180000);
    page = await browser.newPage();
    const token = await apiToken(page.request, ADMIN_EMAIL, ADMIN_PASSWORD);
    email = `arkiv-${rand}@example.com`;
    await createUserInNewGroup(page.request, token, {
      groupName: `arkiv-user-${rand}`, email, password: USER_PASSWORD, firstName: 'Jane', lastName: `Doe ${rand}`,
      coreClaims: ['eforms_read', 'cases_read', 'case_read'],
      pluginClaims: { 'eform-backend-configuration-plugin': ['backend_configuration_plugin_access', 'properties_get'] },
      redirectLink: '/plugins/backend-configuration-pn/files',
    });
    await callOk(page.request, 'POST', FOLDERS_PATH, token, `creating folder ${openFolder}`,
      { data: { name: openFolder, parentId: null } });
    await callOk(page.request, 'POST', '/api/backend-configuration-pn/file-tags', token, `creating etiket ${etiket}`,
      { data: { name: etiket } });
  });

  test.afterAll(async () => {
    await page?.close();
  });

  test('N1 a non-admin navigates the folders but gets no folder administration and no access column', async () => {
    // Cold login plus one page load.
    test.setTimeout(150000);
    await loginAs(page, email, USER_PASSWORD);
    const archive = new BackendConfigurationArchivePage(page);
    const capabilities = await archive.open();
    expect(capabilities.canManage, 'a plain user does not manage the archive').toBe(false);
    await expect(archive.folderRow(DEFAULT_FOLDER)).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(archive.folderRow(openFolder)).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(archive.dateCheckChip()).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(page.locator('#addNewFileBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(page.locator('#archiveNewFolderBtn')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(page.locator('[id^="archiveFolderMenuBtn-"]')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(page.locator('#archiveAccessHeader')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(page.locator('#archiveNavInbox')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await archive.selectFolder(openFolder);
    await expect(archive.crumb()).toContainText(openFolder, { timeout: UI_TIMEOUT });
  });

  test('N2 Indbakke and Indstillinger send a plain user back to the archive; etiketter cannot be renamed or deleted', async () => {
    // Two guarded page loads and one dialog; no SDK call.
    test.setTimeout(60000);
    const archive = new BackendConfigurationArchivePage(page);
    for (const section of ['inbox', 'settings']) {
      const capabilities = await archive.open(`${ARCHIVE_URL}/${section}`);
      expect(capabilities.canManage, `canManage on files/${section}`).toBe(false);
      await page.waitForURL(url => url.pathname === '/plugins/backend-configuration-pn/files', { timeout: UI_TIMEOUT });
    }

    await archive.openTagsDialog();
    const row = archive.tagRow(etiket);
    await expect(row).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(archive.dialog().locator('#newTagBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(row.locator('#editTagBtn')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(row.locator('#deleteTagBtn')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await archive.closeTagsDialog();
  });
});
