/**
 * The Arkiv page with folders (layout W1): folder tree, document table and the opened document's large view, plus the
 * Indbakke review steps the archive specs share. Folder menu items live in the CDK overlay on <body>,
 * so they are addressed by `<prefix>-<folderId>` inside `.cdk-overlay-container` (CLAUDE.md rule 7).
 */
import { expect, Locator, Page, Response } from '@playwright/test';
import { API_TIMEOUT, ignoreUnhandledRejections, UI_TIMEOUT, waitForApiResponse } from './wait-helpers';
import { fillInDialog } from './dialog-helpers';

export const ARCHIVE_URL = 'http://localhost:4200/plugins/backend-configuration-pn/files';
export const ARCHIVE_API = '/api/backend-configuration-pn';
export const FOLDERS_PATH = `${ARCHIVE_API}/archive-folders`;
export const FILES_PATH = `${ARCHIVE_API}/files`;
/** Created by the base migration in every tenant; never deleted, moved or restricted (D12). */
export const DEFAULT_FOLDER = 'Arkiv';
/** InboxDocumentStatus, as the inbox list renders it in `data-status`. */
export const InboxStatus = { Ready: '2', Filed: '4' } as const;

/** 2xx with `success: true` (OperationResult answers 200 on failure too); returns the body. */
export async function expectSuccess(response: Response | Promise<Response>, what: string): Promise<any> {
  const resp = await response;
  if (!resp.ok()) {
    throw new Error(`${what}: HTTP ${resp.status()} ${await resp.text()}`);
  }
  const body = await resp.json();
  expect(body.success, `${what}: ${body.message ?? ''}`).toBe(true);
  return body;
}

export class BackendConfigurationArchivePage {
  constructor(private page: Page) {}

  tree(): Locator {
    return this.page.locator('#archiveFolderTree');
  }

  folderRow(name: string): Locator {
    return this.tree().locator(`.archive-folder-row[data-folder-name="${name}"]`);
  }

  table(): Locator {
    return this.page.locator('#archiveFilesTable');
  }

  /** The table row whose file name contains `name` (rows render "<name>.<extension>"). */
  fileRow(name: string): Locator {
    return this.table().locator('tr.archive-file-row').filter({ has: this.page.locator('.fileName', { hasText: name }) });
  }

  /** The opened document's large view (it covers the folder tree and the table while it is shown). */
  panel(): Locator {
    return this.page.locator('#archiveDocumentPanel');
  }

  /** The Mappe cell of a row: `#folderPath-<i>`, i being the row's place in the table. */
  async folderCell(row: Locator): Promise<Locator> {
    const index = await row.evaluate((tr: HTMLTableRowElement) => tr.sectionRowIndex, undefined, { timeout: UI_TIMEOUT });
    return this.table().locator(`#folderPath-${index}`);
  }

  /** Opens a row's document in the large view and waits for its data; returns the file id. */
  async openDocument(row: Locator, what: string): Promise<string> {
    const fileId = await row.getAttribute('data-file-id', { timeout: UI_TIMEOUT });
    expect(fileId, `file id of ${what}`).toMatch(/^\d+$/);
    const loaded = this.waitForCall(`the document data of ${what}`, 'GET', `${FILES_PATH}/${fileId}`);
    await row.locator('.fileName').click({ timeout: UI_TIMEOUT });
    await expectSuccess(loaded, `opening ${what}`);
    await expect(this.panel()).toBeVisible({ timeout: UI_TIMEOUT });
    return fileId!;
  }

  /** Clicks previous/next in the large view and waits for that document's data. */
  async stepDocument(button: 'archiveViewPrevBtn' | 'archiveViewNextBtn', fileId: string): Promise<void> {
    const loaded = this.waitForCall(`the document data of file ${fileId}`, 'GET', `${FILES_PATH}/${fileId}`);
    await this.panel().locator(`#${button}`).click({ timeout: UI_TIMEOUT });
    await expectSuccess(loaded, `stepping to file ${fileId}`);
  }

  dialog(): Locator {
    return this.page.locator('mat-dialog-container');
  }

  crumb(): Locator {
    return this.page.locator('#archiveCrumb');
  }

  crumbAccess(): Locator {
    return this.page.locator('#archiveCrumbAccess');
  }

  dateCheckChip(): Locator {
    return this.page.locator('#archiveDateCheckChip');
  }

  dateCheckCount(): Locator {
    return this.page.locator('#archiveDateCheckCount');
  }

  async clickInDialog(id: string): Promise<void> {
    await this.dialog().locator(`#${id}`).click({ timeout: UI_TIMEOUT });
  }

  /** Arms a bounded wait for one API call; await it after the action that triggers it. */
  waitForCall(description: string, method: string, path: string | RegExp): Promise<Response> {
    const wait = waitForApiResponse(this.page, `${method} ${description}`, r => {
      if (r.request().method() !== method) {
        return false;
      }
      const pathname = new URL(r.url()).pathname;
      return typeof path === 'string' ? pathname.endsWith(path) : path.test(pathname);
    }, API_TIMEOUT);
    ignoreUnhandledRejections(wait);
    return wait;
  }

  /**
   * The waits are armed after `goto` commits, not before: until then a call the OLD archive document had in flight
   * (the refresh after a folder delete) can satisfy them, and its discarded body makes `json()` fail with
   * "No resource with given identifier found" (zb Z8).
   */
  async open(): Promise<void> {
    await this.page.goto(ARCHIVE_URL, { waitUntil: 'commit', timeout: API_TIMEOUT });
    const folders = this.waitForCall('archive folder tree', 'GET', FOLDERS_PATH);
    const files = this.waitForCall('archive documents', 'POST', FILES_PATH);
    await expectSuccess(folders, 'loading the folder tree');
    await expectSuccess(files, 'loading the archive documents');
    await expect(this.tree()).toBeVisible({ timeout: UI_TIMEOUT });
  }

  async folderId(name: string): Promise<number> {
    const row = this.folderRow(name);
    await expect(row, `folder ${name} in the tree`).toHaveCount(1, { timeout: UI_TIMEOUT });
    const id = Number(await row.getAttribute('data-folder-id', { timeout: UI_TIMEOUT }));
    expect(id, `id of folder ${name}`).toBeGreaterThan(0);
    return id;
  }

  async selectFolder(name: string): Promise<void> {
    const files = this.waitForCall(`documents of folder ${name}`, 'POST', FILES_PATH);
    await this.folderRow(name).locator('.archive-folder-row__main').click({ timeout: UI_TIMEOUT });
    await expectSuccess(files, `listing folder ${name}`);
    await expect(this.folderRow(name)).toHaveClass(/archive-folder-row--selected/, { timeout: UI_TIMEOUT });
  }

  /** Opens a folder's ⋮ menu; returns a lookup for its items by id prefix. */
  async openFolderMenu(name: string): Promise<(idPrefix: string) => Locator> {
    const id = await this.folderId(name);
    await this.page.locator(`#archiveFolderMenuBtn-${id}`).click({ timeout: UI_TIMEOUT });
    await this.page.locator('.mat-mdc-menu-panel').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    return idPrefix => this.page.locator('.cdk-overlay-container').locator(`#${idPrefix}-${id}`);
  }

  async closeMenu(): Promise<void> {
    await this.page.keyboard.press('Escape');
    await expect(this.page.locator('.mat-mdc-menu-panel')).toHaveCount(0, { timeout: UI_TIMEOUT });
  }

  /** Fills the open folder-name dialog, saves, and waits for the write and the tree reload. */
  async saveFolderName(name: string, what: string, method: 'POST' | 'PUT'): Promise<void> {
    await expect(this.dialog()).toHaveCount(1, { timeout: UI_TIMEOUT });
    await fillInDialog(this.page, this.dialog().locator('#archiveFolderNameInput'), name);
    await expect(this.dialog().locator('#archiveFolderNameSaveBtn')).toBeEnabled({ timeout: UI_TIMEOUT });
    const saved = this.waitForCall(what, method, /\/archive-folders(\/\d+)?$/);
    const reloaded = this.waitForCall('folder tree after the change', 'GET', FOLDERS_PATH);
    await this.clickInDialog('archiveFolderNameSaveBtn');
    await expectSuccess(saved, what);
    await expectSuccess(reloaded, 'reloading the folder tree');
    await expect(this.dialog()).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(this.folderRow(name)).toHaveCount(1, { timeout: UI_TIMEOUT });
  }

  async createTopFolder(name: string): Promise<number> {
    await this.page.locator('#archiveNewFolderBtn').click({ timeout: UI_TIMEOUT });
    await this.saveFolderName(name, `creating folder ${name}`, 'POST');
    return this.folderId(name);
  }

  async createSubfolder(parent: string, name: string): Promise<number> {
    const item = await this.openFolderMenu(parent);
    await item('archiveFolderNewSubBtn').click({ timeout: UI_TIMEOUT });
    await this.saveFolderName(name, `creating subfolder ${name}`, 'POST');
    return this.folderId(name);
  }

  /** Picks the one option of an mtx-select whose text contains `text` (names in these specs are unique). */
  async chooseInSelect(selectId: string, text: string): Promise<void> {
    const select = this.page.locator(`#${selectId}`);
    await select.locator('.ng-arrow-wrapper').click({ timeout: UI_TIMEOUT });
    await select.locator('input').fill(text, { timeout: UI_TIMEOUT });
    const option = this.page.locator('ng-dropdown-panel .ng-option').filter({ hasText: text });
    await expect(option, `exactly one "${text}" in #${selectId}`).toHaveCount(1, { timeout: UI_TIMEOUT });
    await option.click({ timeout: UI_TIMEOUT });
  }

  /**
   * Uploads one PDF from the Arkiv page. The upload page must preselect `folderLabel` (the folder that was
   * open, D1); the document must then show in that folder's list.
   */
  async uploadPdf(baseName: string, pdf: Buffer, propertyName: string, folderLabel: string): Promise<void> {
    await this.uploadFile(baseName, `${baseName}.pdf`, 'application/pdf', pdf, propertyName, folderLabel);
  }

  /** Uploads one image (jpg/png) the same way; `fileName` carries the extension. */
  async uploadImage(baseName: string, fileName: string, mimeType: 'image/png' | 'image/jpeg', image: Buffer,
                    propertyName: string, folderLabel: string): Promise<void> {
    await this.uploadFile(baseName, fileName, mimeType, image, propertyName, folderLabel);
  }

  private async uploadFile(baseName: string, fileName: string, mimeType: string, buffer: Buffer,
                           propertyName: string, folderLabel: string): Promise<void> {
    const folders = this.waitForCall('folders for the upload page', 'GET', FOLDERS_PATH);
    await this.page.locator('#addNewFileBtn').click({ timeout: UI_TIMEOUT });
    await expectSuccess(folders, 'loading the upload page folders');
    await expect(this.page.locator('#archiveUploadFolderSelect')).toContainText(folderLabel, { timeout: UI_TIMEOUT });
    await this.page.locator('#pdfInput').setInputFiles({ name: fileName, mimeType, buffer }, { timeout: UI_TIMEOUT });
    await this.chooseInSelect('propertyIdSelector', propertyName);
    await expect(this.page.locator('#uploadFilesBtn')).toBeEnabled({ timeout: UI_TIMEOUT });
    const created = this.waitForCall(`uploading ${baseName}`, 'POST', `${FILES_PATH}/create`);
    const listed = this.waitForCall('archive documents after the upload', 'POST', FILES_PATH);
    await this.page.locator('#uploadFilesBtn').click({ timeout: UI_TIMEOUT });
    await expectSuccess(created, `uploading ${baseName}`);
    await expectSuccess(listed, 'listing the archive after the upload');
    await expect(this.fileRow(baseName)).toHaveCount(1, { timeout: UI_TIMEOUT });
  }

  // ---- Indbakke ---------------------------------------------------------------------

  inboxRow(fileName: string): Locator {
    return this.page.locator('#inboxTable tr.inbox-row', { hasText: fileName });
  }

  /** Armed after commit, for the reason given on `open`. */
  async openInbox(): Promise<void> {
    await this.page.goto(`${ARCHIVE_URL}/inbox`, { waitUntil: 'commit', timeout: API_TIMEOUT });
    const listed = this.waitForCall('inbox list', 'GET', `${ARCHIVE_API}/inbox`);
    await expectSuccess(listed, 'loading the inbox list');
    await expect(this.page.locator('#inboxTable')).toBeVisible({ timeout: UI_TIMEOUT });
  }

  /** The row's inbox id, once exactly one row shows the file in the expected status. */
  async inboxRowId(fileName: string, status: string): Promise<string> {
    const row = this.inboxRow(fileName);
    await expect(row).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(row).toHaveAttribute('data-status', status, { timeout: UI_TIMEOUT });
    const id = await row.getAttribute('data-inbox-id', { timeout: UI_TIMEOUT });
    expect(id, `inbox id of the row for ${fileName}`).toMatch(/^\d+$/);
    return id!;
  }

  async openReview(id: string): Promise<void> {
    await this.page.locator(`#inboxReviewBtn-${id}`).click({ timeout: UI_TIMEOUT });
    await expect(this.dialog()).toHaveCount(1, { timeout: UI_TIMEOUT });
  }

  /** Opens the inbox and the review of the one Ready row for `fileName`; returns its inbox id. */
  async openReadyReview(fileName: string): Promise<string> {
    await this.openInbox();
    const id = await this.inboxRowId(fileName, InboxStatus.Ready);
    await this.openReview(id);
    return id;
  }

  /** Clicks "File in archive" in the open review and expects the filing to succeed; returns the file call. */
  async fileFromReview(id: string, what: string): Promise<Response> {
    const filed = this.waitForCall(what, 'POST', `/inbox/${id}/file`);
    await this.clickInDialog('inboxFileBtn');
    await expectSuccess(filed, what);
    await expect(this.dialog().locator('#inboxUndoBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    return filed;
  }

  async closeReview(): Promise<void> {
    await this.clickInDialog('inboxCloseBtn');
    await expect(this.dialog()).toHaveCount(0, { timeout: UI_TIMEOUT });
  }
}
