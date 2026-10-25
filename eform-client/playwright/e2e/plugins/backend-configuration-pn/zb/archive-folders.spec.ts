import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import { BackendConfigurationPropertyWorkersPage, PropertyWorker } from '../BackendConfigurationPropertyWorkers.page';
import {
  ARCHIVE_URL, BackendConfigurationArchivePage, DEFAULT_FOLDER, expectSuccess, FILES_PATH, FOLDERS_PATH,
} from '../BackendConfigurationArchive.page';
import { fetchCatalog, seedInboxDocument, tinyPdf } from '../inbox-hub-seed';
import { openRowActionMenu } from '../row-action-menu';
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';

/**
 * Arkiv with folders, layout W1 (shard zb), as an admin. The tenant starts with only the default folder
 * "Arkiv"; every other name is random so a rerun on the same database cannot collide.
 *
 * Z1 the default folder is selected, marked "standard" and cannot be moved, restricted or deleted (D12)
 * Z2 a folder and a subfolder are created; the subfolder cannot hold a third level (D2); it is renamed
 * Z3 an upload lands in the folder that was open (D1)
 * Z4 restricting the top folder locks it; its subfolder inherits the access (D6)
 * Z5 dragging a document into the restricted folder asks first (D11), then moves it
 * Z6 a kept date suggestion lands under "Dato skal tjekkes" and is approved from the panel (D8)
 * Z7 only empty folders can be deleted (D12)
 * Z8 the Mappe column shows every document's folder: the parent and the leaf
 * Z9 an opened document is a large view over the tree and the table; Escape and back return to the table
 * Z10 previous and next in the large view move between the documents of the list
 * Z11 the page does not scroll sideways, neither in the list nor in the large view (#1450)
 * Z12 an image can be uploaded, gets the image icon and wording, and the large view previews it
 * Z13 an account_disabled answer ends the session (D7) — last, it signs the page out
 */
const rand = generateRandmString(5);
const property: PropertyCreateUpdate = {
  name: `Arkiv ${rand}`,
  chrNumber: generateRandmString(5),
  address: 'Nordvej 12, 8000 Aarhus C',
  cvrNumber: '1111111',
};
const groupName = `Ledelse-${rand}`;
const worker: PropertyWorker = {
  name: `Jane${rand}`,
  surname: 'Doe',
  language: 'Dansk',
  properties: [property.name!],
  workerEmail: `jane-${rand.toLowerCase()}@example.com`,
  tags: [groupName],
};
const topFolder = `Drift-${rand}`;
const subFolder = `Fakturaer-${rand}`;
const renamedSub = `Regninger-${rand}`;
const emptyFolder = `Tom-${rand}`;
const docInSub = `Faktura-${rand}`;
const docInArkiv = `Kontrakt-${rand}`;
const imageDoc = `example-image-${rand}`;
/** A valid 1x1 PNG, so the browser decodes it (naturalWidth 1). */
const TINY_PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');

test.describe.serial('Arkiv med mapper (W1)', () => {
  let page: Page;
  let archive: BackendConfigurationArchivePage;

  test.beforeAll(async ({ browser }) => {
    // Cold login (up to ~2 min), one property, one worker group and one SDK-provisioned worker.
    test.setTimeout(360000);
    page = await browser.newPage();
    await page.goto('http://localhost:4200', { timeout: API_TIMEOUT });
    await new LoginPage(page).login();
    const properties = new BackendConfigurationPropertiesPage(page);
    await properties.goToProperties();
    await properties.createProperty(property);
    // A medarbejdergruppe is "live" once a worker carries it; only live groups can restrict a folder.
    const workers = new BackendConfigurationPropertyWorkersPage(page);
    await workers.goToPropertyWorkers();
    await workers.createTag(groupName);
    await workers.create(worker);
    archive = new BackendConfigurationArchivePage(page);
  });

  test.afterAll(async () => {
    await page?.close();
  });

  test('Z1 the default folder is selected, marked "standard" and cannot be moved, restricted or deleted', async () => {
    // One page load and one menu.
    test.setTimeout(90000);
    await archive.open();
    const arkiv = archive.folderRow(DEFAULT_FOLDER);
    await expect(arkiv).toHaveClass(/archive-folder-row--selected/, { timeout: UI_TIMEOUT });
    await expect(arkiv.locator('.archive-folder-row__std')).toBeVisible({ timeout: UI_TIMEOUT });
    const item = await archive.openFolderMenu(DEFAULT_FOLDER);
    await expect(item('archiveFolderRenameBtn')).toBeEnabled({ timeout: UI_TIMEOUT });
    await expect(item('archiveFolderNewSubBtn')).toBeEnabled({ timeout: UI_TIMEOUT });
    for (const prefix of ['archiveFolderMoveBtn', 'archiveFolderAccessBtn', 'archiveFolderDeleteBtn']) {
      await expect(item(prefix), `${prefix} on the default folder`).toBeDisabled({ timeout: UI_TIMEOUT });
    }
    await archive.closeMenu();
  });

  test('Z2 a folder and a subfolder are created; the subfolder cannot hold another level; it is renamed', async () => {
    // Three folder writes, each followed by a tree reload (API_TIMEOUT each).
    test.setTimeout(120000);
    await archive.createTopFolder(topFolder);
    await archive.createSubfolder(topFolder, subFolder);
    const item = await archive.openFolderMenu(subFolder);
    await expect(item('archiveFolderNewSubBtn')).toBeDisabled({ timeout: UI_TIMEOUT });
    await expect(item('archiveFolderNewSubBtn')).toContainText(/Højst 2 niveauer|At most 2 levels/, { timeout: UI_TIMEOUT });
    await item('archiveFolderRenameBtn').click({ timeout: UI_TIMEOUT });
    await archive.saveFolderName(renamedSub, `renaming ${subFolder}`, 'PUT');
    await expect(archive.folderRow(subFolder)).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('Z3 an upload lands in the folder that was open', async () => {
    // Two uploads: each a page load, the folder list, the upload and the list reload (API_TIMEOUT each).
    test.setTimeout(150000);
    await archive.selectFolder(renamedSub);
    await expect(archive.crumb()).toContainText(topFolder, { timeout: UI_TIMEOUT });
    await archive.uploadPdf(docInSub, tinyPdf(docInSub), property.name!, `${topFolder} › ${renamedSub}`);
    await expect(archive.folderRow(renamedSub).locator('.archive-folder-row__count')).toHaveText('1', { timeout: UI_TIMEOUT });
    await archive.selectFolder(DEFAULT_FOLDER);
    await archive.uploadPdf(docInArkiv, tinyPdf(docInArkiv), property.name!, DEFAULT_FOLDER);
  });

  test('Z4 restricting the top folder locks it and its subfolder inherits the access', async () => {
    // One access write, one tree reload, one folder list (API_TIMEOUT each).
    test.setTimeout(90000);
    const topId = await archive.folderId(topFolder);
    const item = await archive.openFolderMenu(topFolder);
    await item('archiveFolderAccessBtn').click({ timeout: UI_TIMEOUT });
    await expect(archive.dialog()).toHaveCount(1, { timeout: UI_TIMEOUT });
    await archive.chooseInSelect('archiveAccessTagSelect', groupName);
    const saved = archive.waitForCall('folder access', 'PUT', `${FOLDERS_PATH}/${topId}/access`);
    const reloaded = archive.waitForCall('folder tree after the access change', 'GET', FOLDERS_PATH);
    await archive.clickInDialog('archiveAccessSaveBtn');
    await expectSuccess(saved, 'restricting the folder');
    await expectSuccess(reloaded, 'reloading the folder tree');
    await expect(archive.folderRow(topFolder).locator('.archive-lock')).toBeVisible({ timeout: UI_TIMEOUT });
    await archive.selectFolder(renamedSub);
    await expect(archive.crumbAccess()).toContainText(groupName, { timeout: UI_TIMEOUT });
    await expect(archive.crumbAccess()).toContainText(topFolder, { timeout: UI_TIMEOUT });
  });

  test('Z5 dragging a document into the restricted folder asks first (D11), then moves it', async () => {
    // Two folder lists and the move (API_TIMEOUT each).
    test.setTimeout(90000);
    await archive.selectFolder(DEFAULT_FOLDER);
    await archive.fileRow(docInArkiv).dragTo(archive.folderRow(topFolder), { timeout: UI_TIMEOUT });
    await expect(archive.dialog().locator('#archiveMoveImpact')).toContainText(groupName, { timeout: UI_TIMEOUT });
    const moved = archive.waitForCall('moving the document', 'PUT', `${FILES_PATH}/folder`);
    await archive.clickInDialog('archiveMoveConfirmBtn');
    const body = await expectSuccess(moved, 'moving the document');
    expect(body.model, 'one document moved, and its access changed').toEqual({ moved: 1, accessChanged: 1 });
    await expect(archive.fileRow(docInArkiv)).toHaveCount(0, { timeout: UI_TIMEOUT });
    await archive.selectFolder(topFolder);
    await expect(archive.fileRow(docInArkiv)).toHaveCount(1, { timeout: UI_TIMEOUT });
  });

  test('Z6 a kept date suggestion lands under "Dato skal tjekkes" and is approved from the panel', async () => {
    // A catalog call, two signed seeding calls, the inbox load and filing, the archive load, the date-check
    // list and the date save (API_TIMEOUT each).
    test.setTimeout(180000);
    const catalog = await fetchCatalog(page.request);
    const catalogProperty = catalog.properties.find(p => p.name === property.name);
    const catalogFolder = catalog.folders.find(f => f.name === renamedSub);
    expect(catalogProperty, `the catalog lists ${property.name}`).toBeTruthy();
    expect(catalogFolder, `the catalog lists ${renamedSub}`).toBeTruthy();
    const docName = `Regning ${rand}`;
    await seedInboxDocument(page.request, {
      fromAddress: 'faktura@example.com',
      fileName: `${docName}.pdf`,
      suggestions: [
        { kind: 'property', targetId: catalogProperty!.id },
        { kind: 'folder', targetId: catalogFolder!.id },
        { kind: 'documentDate', targetDate: '2026-10-03' },
      ],
    });

    // A row stuck in Preparing fails here with its status, not later in the dialog.
    const inboxId = await archive.openReadyReview(docName);
    await expect(archive.dialog().locator('#inboxFolderSelect')).toContainText(renamedSub, { timeout: UI_TIMEOUT });
    await expect(archive.dialog().locator('#inboxDateValue')).toContainText('03.10.2026', { timeout: UI_TIMEOUT });
    const filed = await archive.fileFromReview(inboxId, 'filing the document');
    expect(filed.request().postDataJSON(), 'filed into the suggested folder with the date not approved').toEqual(
      expect.objectContaining({ folderId: catalogFolder!.id, documentDate: '2026-10-03', documentDateApproved: false }));
    await archive.closeReview();

    await archive.open();
    const count = archive.dateCheckCount();
    await expect(count, 'the filed document waits for a date check').not.toHaveText('0', { timeout: UI_TIMEOUT });
    const countBefore = Number(await count.innerText({ timeout: UI_TIMEOUT }));
    const checkList = archive.waitForCall('the date-check list', 'POST', FILES_PATH);
    await archive.dateCheckChip().click({ timeout: UI_TIMEOUT });
    await expectSuccess(checkList, 'listing the documents whose date needs checking');
    await expect(archive.dateCheckChip()).toHaveAttribute('aria-pressed', 'true', { timeout: UI_TIMEOUT });

    const row = archive.fileRow(docName);
    await expect(row.locator('.archive-date--suggested')).toBeVisible({ timeout: UI_TIMEOUT });
    const fileId = await row.getAttribute('data-file-id', { timeout: UI_TIMEOUT });
    await row.locator('.fileName').click({ timeout: UI_TIMEOUT });
    await expect(archive.panel().locator('#archivePanelDateApproveBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await archive.panel().locator('#archivePanelDateApproveBtn').click({ timeout: UI_TIMEOUT });
    const dated = archive.waitForCall('approving the date', 'PUT', `${FILES_PATH}/${fileId}/date`);
    await archive.panel().locator('#archivePanelSaveBtn').click({ timeout: UI_TIMEOUT });
    const datedResponse = await dated;
    await expectSuccess(datedResponse, 'approving the date');
    expect(datedResponse.request().postDataJSON(), 'approving sends the suggested date').toEqual({ date: '2026-10-03' });
    // Approved, so it leaves the date-check list and the panel closes with it.
    await expect(archive.fileRow(docName)).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(archive.panel()).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(count).toHaveText(String(countBefore - 1), { timeout: UI_TIMEOUT });
  });

  test('Z7 only empty folders can be deleted', async () => {
    // One page load, one folder create and one delete, each with a tree reload (API_TIMEOUT each).
    test.setTimeout(120000);
    await archive.open();
    const item = await archive.openFolderMenu(topFolder);
    await expect(item('archiveFolderDeleteBtn')).toBeDisabled({ timeout: UI_TIMEOUT });
    await expect(item('archiveFolderDeleteBtn')).toContainText(/Kun tomme mapper kan slettes|Only empty folders can be deleted/, { timeout: UI_TIMEOUT });
    await archive.closeMenu();

    const emptyId = await archive.createTopFolder(emptyFolder);
    const emptyItem = await archive.openFolderMenu(emptyFolder);
    await emptyItem('archiveFolderDeleteBtn').click({ timeout: UI_TIMEOUT });
    const deleted = archive.waitForCall('deleting the folder', 'DELETE', `${FOLDERS_PATH}/${emptyId}`);
    await archive.clickInDialog('archiveFolderDeleteConfirmBtn');
    await expectSuccess(deleted, 'deleting the empty folder');
    await expect(archive.folderRow(emptyFolder)).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('Z8 the Mappe column shows every document\'s folder: the parent and the leaf', async () => {
    // One page load and two folder lists.
    test.setTimeout(90000);
    await archive.open();
    await expect(archive.table().locator('#archiveFolderHeader')).toHaveText(/Mappe|Folder/, { timeout: UI_TIMEOUT });

    // A subfolder: "<parent> ›" and the subfolder; the tooltip holds the whole path.
    await archive.selectFolder(renamedSub);
    const inSub = await archive.folderCell(archive.fileRow(docInSub));
    await expect(inSub.locator('.archive-folder-path__parent')).toHaveText(`${topFolder} ›`, { timeout: UI_TIMEOUT });
    await expect(inSub.locator('.archive-folder-path__leaf')).toHaveText(renamedSub, { timeout: UI_TIMEOUT });

    // A top-level folder (Z5 moved the document here): only the folder, no parent.
    await archive.selectFolder(topFolder);
    const inTop = await archive.folderCell(archive.fileRow(docInArkiv));
    await expect(inTop.locator('.archive-folder-path__leaf')).toHaveText(topFolder, { timeout: UI_TIMEOUT });
    await expect(inTop.locator('.archive-folder-path__parent')).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('Z9 an opened document is a large view over the tree and the table; Escape and back return', async () => {
    // One page load, one folder list and two document opens (API_TIMEOUT each).
    test.setTimeout(120000);
    await archive.open();
    await archive.selectFolder(topFolder);
    const row = archive.fileRow(docInArkiv);

    await archive.openDocument(row, docInArkiv);
    await expect(archive.panel()).toHaveClass(/archive-view/, { timeout: UI_TIMEOUT });
    await expect(archive.panel().locator('#archivePanelTitle')).toHaveText(docInArkiv, { timeout: UI_TIMEOUT });
    await expect(archive.panel().locator('.archive-view__path')).toHaveText(topFolder, { timeout: UI_TIMEOUT });
    await expect(archive.panel().locator('#archivePanelCloseBtn')).toBeFocused({ timeout: UI_TIMEOUT });
    await expect(archive.table()).toBeHidden({ timeout: UI_TIMEOUT });
    await expect(archive.tree()).toBeHidden({ timeout: UI_TIMEOUT });

    await page.keyboard.press('Escape');
    await expect(archive.panel()).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(archive.table()).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(archive.tree()).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(row, 'the row that opened the view has focus again').toBeFocused({ timeout: UI_TIMEOUT });

    await archive.openDocument(row, docInArkiv);
    await expect(archive.table()).toBeHidden({ timeout: UI_TIMEOUT });
    await archive.panel().locator('#archivePanelCloseBtn').click({ timeout: UI_TIMEOUT });
    await expect(archive.panel()).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(archive.table()).toBeVisible({ timeout: UI_TIMEOUT });
  });

  test('Z10 previous and next in the large view move between the documents of the list', async () => {
    // One page load, one folder list and four document opens (API_TIMEOUT each).
    test.setTimeout(180000);
    await archive.open();
    // Z3 uploaded one document here and Z6 filed another.
    await archive.selectFolder(renamedSub);
    const rows = archive.table().locator('tr.archive-file-row');
    await expect(rows, `the two documents in ${renamedSub}`).toHaveCount(2, { timeout: UI_TIMEOUT });
    const secondId = await rows.nth(1).getAttribute('data-file-id', { timeout: UI_TIMEOUT });
    const firstId = await archive.openDocument(rows.nth(0), `the first document in ${renamedSub}`);
    expect(secondId, 'file id of the second row').toMatch(/^\d+$/);

    const view = archive.panel();
    const position = view.locator('#archivePanelPosition');
    await expect(position).toHaveText(/^1 (af|of) 2$/, { timeout: UI_TIMEOUT });
    await expect(view.locator('#archiveViewPrevBtn')).toBeDisabled({ timeout: UI_TIMEOUT });
    await expect(view.locator('#archiveViewNextBtn')).toBeEnabled({ timeout: UI_TIMEOUT });

    await archive.stepDocument('archiveViewNextBtn', secondId!);
    await expect(position).toHaveText(/^2 (af|of) 2$/, { timeout: UI_TIMEOUT });
    await expect(view.locator('#archiveViewNextBtn')).toBeDisabled({ timeout: UI_TIMEOUT });
    await expect(view.locator('#archiveViewPrevBtn')).toBeEnabled({ timeout: UI_TIMEOUT });
    await expect(rows.nth(1)).toHaveClass(/archive-file-row--current/, { timeout: UI_TIMEOUT });

    // Next disabled itself at the last document, so focus fell to <body>: Escape must still close the view, and
    // focus goes to the row of the document shown last, not to the row that opened the view.
    await page.keyboard.press('Escape');
    await expect(view).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(archive.table()).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(rows.nth(1), 'the row of the document shown last has focus').toBeFocused({ timeout: UI_TIMEOUT });

    await archive.openDocument(rows.nth(1), `the second document in ${renamedSub}`);
    await archive.stepDocument('archiveViewPrevBtn', firstId);
    await expect(position).toHaveText(/^1 (af|of) 2$/, { timeout: UI_TIMEOUT });
    await expect(view.locator('#archiveViewPrevBtn')).toBeDisabled({ timeout: UI_TIMEOUT });
    await expect(rows.nth(0)).toHaveClass(/archive-file-row--current/, { timeout: UI_TIMEOUT });

    await view.locator('#archivePanelCloseBtn').click({ timeout: UI_TIMEOUT });
    await expect(view).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(rows.nth(0), 'the row of the document shown last has focus').toBeFocused({ timeout: UI_TIMEOUT });
  });

  test('Z11 the page does not scroll sideways, neither in the list nor in the large view', async () => {
    // One page load, one folder list and one document open.
    test.setTimeout(90000);
    // Narrower than the folder tree plus every table column need.
    await page.setViewportSize({ width: 1000, height: 800 });
    try {
      await archive.open();
      await archive.selectFolder(topFolder);
      const widths = () => page.evaluate(() => {
        const card = document.querySelector('.content-card')!;
        const table = document.querySelector('app-files-table')!;
        return {
          cardOverflow: card.scrollWidth - card.clientWidth,
          cardScrollLeft: card.scrollLeft,
          tableOverflow: table.scrollWidth - table.clientWidth,
        };
      });
      const sideways = async () => {
        const { cardOverflow, cardScrollLeft } = await widths();
        return { cardOverflow, cardScrollLeft };
      };
      // The table is wider than its column, and that overflow stays inside the table's own frame.
      await expect.poll(async () => (await widths()).tableOverflow, {
        message: 'the table is wider than its frame', timeout: UI_TIMEOUT,
      }).toBeGreaterThan(0);
      await expect.poll(sideways, { message: 'the list does not scroll sideways', timeout: UI_TIMEOUT })
        .toEqual({ cardOverflow: 0, cardScrollLeft: 0 });
      await archive.openDocument(archive.fileRow(docInArkiv), docInArkiv);
      await expect(archive.table()).toBeHidden({ timeout: UI_TIMEOUT });
      await expect.poll(sideways, { message: 'the large view does not scroll sideways', timeout: UI_TIMEOUT })
        .toEqual({ cardOverflow: 0, cardScrollLeft: 0 });
      await archive.panel().locator('#archivePanelCloseBtn').click({ timeout: UI_TIMEOUT });
      await expect(archive.panel()).toHaveCount(0, { timeout: UI_TIMEOUT });
    } finally {
      // playwright.config.ts use.viewport
      await page.setViewportSize({ width: 1920, height: 1080 });
    }
  });

  test('Z12 an image can be uploaded, gets the image icon and wording, and the large view previews it', async () => {
    // One page load, one upload with its list reload, the large view's two calls (API_TIMEOUT each).
    test.setTimeout(150000);
    await archive.open();
    await archive.selectFolder(DEFAULT_FOLDER);
    await archive.uploadImage(imageDoc, `${imageDoc}.png`, 'image/png', TINY_PNG, property.name!, DEFAULT_FOLDER);

    const row = archive.fileRow(imageDoc);
    await expect(row.locator('.archive-doc-name mat-icon')).toHaveText('image', { timeout: UI_TIMEOUT });
    await expect(row.locator('.fileName')).toHaveText(`${imageDoc}.png`, { timeout: UI_TIMEOUT });

    const item = await openRowActionMenu(page, row, `row of ${imageDoc}`);
    await expect(item('viewPdfBtn')).toContainText(/Vis billede|View image/, { timeout: UI_TIMEOUT });
    await expect(item('deleteFileBtn')).toContainText(/Slet billede|Delete image/, { timeout: UI_TIMEOUT });
    await archive.closeMenu();

    const fetched = archive.waitForCall('the image bytes', 'GET', /\/files\/get-file\/\d+$/);
    await row.locator('.fileName').click({ timeout: UI_TIMEOUT });
    const response = await fetched;
    expect(response.ok(), `GET get-file answered ${response.status()}`).toBe(true);
    expect(response.headers()['content-type'], 'content type of the served image').toContain('image/png');
    const preview = archive.panel().locator('#archivePanelImagePreview');
    await expect(preview).toBeVisible({ timeout: UI_TIMEOUT });
    await expect.poll(() => preview.evaluate((img: HTMLImageElement) => img.naturalWidth), {
      message: 'the preview image is decoded', timeout: UI_TIMEOUT,
    }).toBeGreaterThan(0);
    await expect(archive.panel().locator('pdf-viewer')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await archive.panel().locator('#archivePanelCloseBtn').click({ timeout: UI_TIMEOUT });
    await expect(archive.panel()).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('Z13 an account_disabled answer ends the session', async () => {
    // One page load whose folder call is answered 403 below (the host retries it once after a token
    // refresh), then the redirect to the login page.
    test.setTimeout(90000);
    const folders = `**${FOLDERS_PATH}**`;
    await page.route(folders, route => route.fulfill({
      status: 403,
      contentType: 'application/json',
      body: JSON.stringify({ error: 'account_disabled' }),
    }));
    try {
      await page.goto(ARCHIVE_URL, { timeout: API_TIMEOUT });
      await expect(page.locator('.toast-error .toast-message')).toContainText(/Din konto er deaktiveret|Your account is disabled/, { timeout: API_TIMEOUT });
      await expect(page).toHaveURL(/\/auth/, { timeout: API_TIMEOUT });
    } finally {
      await page.unroute(folders);
    }
  });
});
