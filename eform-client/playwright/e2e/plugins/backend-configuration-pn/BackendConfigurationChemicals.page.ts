import { Locator, Page, Response, expect } from '@playwright/test';
import { LoginPage } from '../../Page objects/Login.page';
import { generateRandmString } from '../../helper-functions';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from './BackendConfigurationProperties.page';
import { ActionMenuItem, openRowActionMenu } from './row-action-menu';
import { API_TIMEOUT, SLOW_API_TIMEOUT, UI_TIMEOUT, waitForApiResponse } from './wait-helpers';

const BASE_URL = 'http://localhost:4200';
const CHEMICALS_API = '/api/backend-configuration-pn/chemicals';

/** The Kemi screens (route path under `/plugins/backend-configuration-pn/chemicals/`) and the root element each renders. */
const SCREEN_ROOTS = {
  inventory: '#chemicalInventoryPage',
  locations: '#chemicalLocationsPage',
  permissions: '#chemicalPermissionsPage',
  settings: '#chemicalSettingsPage',
  history: '#chemicalHistoryPage',
} as const;
export type ChemicalScreen = keyof typeof SCREEN_ROOTS;
/** Menu children of `#backend-configuration-pn-chemicals`, in menu order. */
export const CHEMICAL_SCREENS_IN_MENU_ORDER: ChemicalScreen[] = ['inventory', 'locations', 'permissions', 'settings', 'history'];

/** A fresh property for one Kemi spec, named `<prefix>-<rand>` so the spec can pick it by name (E-10). */
export const kemiProperty = (prefix: string, rand: string): PropertyCreateUpdate => ({
  name: `${prefix}-${rand}`, chrNumber: generateRandmString(5), address: generateRandmString(5), cvrNumber: '1111111',
});

/** Anchored, whitespace-tolerant regexp so an option must match the whole label (`Farm` never picks `Farm 2`). */
const exactText = (text: string): RegExp =>
  new RegExp(`^\\s*${text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*$`);

/**
 * Page object for the Kemi web screens (flutter-chemistry spec §11),
 * `/plugins/backend-configuration-pn/chemicals/<screen>`. DOM reference:
 * `modules/chemicals/` in this repo.
 *
 *   - Nav: the plugin dropdown `#backend-configuration-pn` holds the Kemi group
 *     `#backend-configuration-pn-chemicals`, which holds one child per shipped
 *     screen, e.g. `#backend-configuration-pn-chemicals-permissions`. On a fresh
 *     install (CI) the host's plugin loader seeds the menu from
 *     EformBackendConfigurationPlugin; PluginMenuSyncService syncs it on existing tenants.
 *   - Shell: title `#chemicalsTitle`, property picker `#chemicalsPropertySelect`
 *     (mtx-select). E1 has no tab bar: each screen is a menu child.
 *   - W4: `#chemicalPermissionsPage`, rows `#chemicalPermissionRow-<workerId>`,
 *     checkboxes `#chemicalPermission-<flag>-<workerId>`, save bar
 *     `#chemicalPermissionsSaveBar` with `#chemicalPermissionsUndoBtn` /
 *     `#chemicalPermissionsSaveBtn`, discard confirm
 *     `#chemicalPermissionsDiscardConfirm` / `#chemicalPermissionsDiscardConfirmCancel`.
 *
 *   - W5: `#chemicalSettingsPage`, stock toggle `#chemicalSettingsStockToggle`, recipients
 *     `#chemicalSettingsRecipient-<i>` / `#chemicalSettingsRecipientInput` / `#chemicalSettingsRecipientError`,
 *     `#chemicalSettingsSaveBtn`, thresholds `#chemicalSettingsThresholds`, preview `#chemicalDigestPreview`
 *     (`#chemicalDigestPreviewNothing`, `#chemicalDigestSection-<passed|upcoming|status|new>`), menu child
 *     `#backend-configuration-pn-chemicals-settings`.
 *   - W1: `#chemicalInventoryPage` (mtx-grid rows `.mat-mdc-row`, `chemical-row--overdue|warning`), chip
 *     `#chemicalsRequiresActionChip`, banner `#chemicalsActionBanner`, export menu `#chemicalsExportBtn` >
 *     `#chemicalsExportAuthorityList|Inventory|Removed`, `#chemicalsRegisterPlacementBtn` (dialog
 *     `#chemicalRegisterSearchInput`, `#chemicalRegisterResult-<chemicalId>`, `#chemicalRegisterLocationSelect`,
 *     `#chemicalRegisterNote`, `#chemicalRegisterSaveBtn`), row menu `chemicalRowDetails|Move|Remove-<i>`.
 *   - W2: side panel `#chemicalDrawer` (opened by the row menu "details", i.e. W1 openDetails), move dialog
 *     `#chemicalMoveTargetSelect` / `#chemicalMoveSaveBtn`, remove dialog `#chemicalRemoveHazardWarning` /
 *     `#chemicalRemoveReasonDisposed` / `#chemicalRemoveSaveBtn`.
 *   - W3: `#chemicalLocationsPage`, rows `#chemicalLocationRow-<id>` in `#chemicalLocationsList` (archived:
 *     `chemicals-location-row--archived`), drag handle `#chemicalLocationDrag-<id>`, `#chemicalLocationUp|Down-<id>`,
 *     `#chemicalLocationArchive-<id>` + confirm `#chemicalLocationArchiveConfirm`, `#chemicalLocationsShowArchived`.
 *   - W6: `#chemicalHistoryPage`, live region `#chemicalHistoryStatus` (text only while loading), rows
 *     `#chemicalHistoryTable tr.chemical-history-row[data-action]`, `#chemicalHistoryActionFilter`,
 *     `#chemicalHistoryExportBtn`.
 *
 * No sleeps: every wait is a bounded wait on a concrete condition.
 */
export class BackendConfigurationChemicalsPage {
  /** Uncaught page errors and console errors, quoted when a Kemi screen does not show what the server sent. */
  private readonly browserErrors: string[] = [];

  constructor(public page: Page) {
    page.on('pageerror', (error) => this.browserErrors.push(`pageerror: ${error.message}`));
    page.on('console', (message) => {
      if (message.type() === 'error') {
        this.browserErrors.push(`console: ${message.text()}`);
      }
    });
  }

  /** Logs in from the app root and opens a Kemi screen for the property (the shared start of every Kemi spec). */
  static async openLoggedIn(page: Page, screen: ChemicalScreen, propertyName: string): Promise<BackendConfigurationChemicalsPage> {
    const kemi = await BackendConfigurationChemicalsPage.login(page);
    await kemi.openScreenFor(screen, propertyName);
    return kemi;
  }

  /** Logs in from the app root. */
  static async login(page: Page): Promise<BackendConfigurationChemicalsPage> {
    await page.goto(BASE_URL, { timeout: API_TIMEOUT });
    await new LoginPage(page).login();
    return new BackendConfigurationChemicalsPage(page);
  }

  /** Creates the property through the Properties screen, then its Kemi locations through W3 (logged in). */
  async createPropertyWithLocations(property: PropertyCreateUpdate, locationNames: string[]): Promise<void> {
    const properties = new BackendConfigurationPropertiesPage(this.page);
    await properties.goToProperties();
    await properties.createProperty(property);
    await this.openScreenFor('locations', property.name);
    for (const name of locationNames) {
      await this.createLocation(name);
    }
  }

  /** The page URL and what the browser reported, for failure messages. */
  browserReport(): string {
    return `page ${this.page.url()}; browser errors [${this.browserErrors.join(' | ')}]`;
  }

  /**
   * Waits for a Kemi API answer (`path` is matched after `/api/backend-configuration-pn/chemicals/`), asserts
   * success and returns the JSON. Call it before the click that sends the request.
   */
  async waitForChemicalsCall(method: string, path: RegExp, match: (response: Response) => boolean = () => true): Promise<any> {
    const description = `${method} chemicals/${path.source}`;
    const response = await waitForApiResponse(this.page, description, (r) => {
      const pathname = new URL(r.url()).pathname;
      return r.request().method() === method && pathname.startsWith(`${CHEMICALS_API}/`)
        && path.test(pathname.slice(CHEMICALS_API.length + 1)) && match(r);
    }, API_TIMEOUT);
    const json = await response.json();
    expect(json.success, description).toBe(true);
    return json;
  }

  pluginMenuButton(): Locator {
    return this.page.locator('#backend-configuration-pn');
  }

  chemicalsGroupButton(): Locator {
    return this.page.locator('#backend-configuration-pn-chemicals');
  }

  permissionsMenuButton(): Locator {
    return this.page.locator('#backend-configuration-pn-chemicals-permissions');
  }

  settingsMenuButton(): Locator {
    return this.page.locator('#backend-configuration-pn-chemicals-settings');
  }

  propertySelect(): Locator {
    return this.page.locator('#chemicalsPropertySelect');
  }

  permissionsPage(): Locator {
    return this.page.locator('#chemicalPermissionsPage');
  }

  /** Expands the plugin dropdown and the Kemi group, each only if the next level is hidden, until `child` shows. */
  private async expandChemicalsGroup(child: Locator): Promise<void> {
    if (!(await child.isVisible())) {
      if (!(await this.chemicalsGroupButton().isVisible())) {
        await this.pluginMenuButton().click({ timeout: UI_TIMEOUT });
      }
      await this.chemicalsGroupButton().click({ timeout: UI_TIMEOUT });
    }
    await child.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  }

  /** Opens Kemi > Rettigheder through the sidebar. */
  async goToPermissionsViaMenu(): Promise<void> {
    await this.expandChemicalsGroup(this.permissionsMenuButton());
    await this.permissionsMenuButton().click({ timeout: UI_TIMEOUT });
    await this.permissionsPage().waitFor({ state: 'visible', timeout: API_TIMEOUT });
  }

  /** Opens the bare Kemi root by URL (`query` e.g. `?propertyId=3`); it redirects to a screen, so nothing is awaited. */
  async gotoRoot(query = ''): Promise<void> {
    await this.page.goto(`${BASE_URL}/plugins/backend-configuration-pn/chemicals${query}`, { timeout: API_TIMEOUT });
  }

  /** Opens a Kemi screen by URL (`query` e.g. `?propertyId=3`); waits for the screen root. */
  async gotoScreen(screen: ChemicalScreen, query = ''): Promise<void> {
    await this.page.goto(`${BASE_URL}/plugins/backend-configuration-pn/chemicals/${screen}${query}`, { timeout: API_TIMEOUT });
    await this.page.locator(SCREEN_ROOTS[screen]).waitFor({ state: 'visible', timeout: API_TIMEOUT });
  }

  /** Opens a Kemi screen by URL and picks the property by name (never relies on which property sorts first, E-10). */
  async openScreenFor(screen: ChemicalScreen, propertyName: string): Promise<void> {
    await this.gotoScreen(screen);
    await this.selectProperty(propertyName);
  }

  /**
   * Reloads the page. The ngrx store does not survive a reload, so the property is picked again by name (E-10);
   * the container would also restore it from ?propertyId=, and picking the selected one again is a no-op.
   */
  async reloadScreenFor(screen: ChemicalScreen, propertyName: string): Promise<void> {
    await this.page.reload({ timeout: API_TIMEOUT });
    await this.page.locator(SCREEN_ROOTS[screen]).waitFor({ state: 'visible', timeout: API_TIMEOUT });
    await this.selectProperty(propertyName);
  }

  /** Opens Rettigheder by URL (the menu has its own test); waits for the screen root. */
  async gotoPermissions(): Promise<void> {
    await this.gotoScreen('permissions');
  }

  settingsPage(): Locator {
    return this.page.locator('#chemicalSettingsPage');
  }

  /**
   * The settings form renders only after the stored settings arrived (a failed or pending load shows no
   * form), so its stock toggle being enabled means it is safe to edit and save.
   */
  async waitForSettingsLoaded(): Promise<void> {
    await this.page.locator('#chemicalSettingsStockToggle button').waitFor({ state: 'visible', timeout: API_TIMEOUT });
    await expect(this.page.locator('#chemicalSettingsStockToggle button')).toBeEnabled({ timeout: UI_TIMEOUT });
  }

  /** Opens Kemi > Indstillinger through the sidebar. */
  async goToSettingsViaMenu(): Promise<void> {
    await this.expandChemicalsGroup(this.settingsMenuButton());
    await this.settingsMenuButton().click({ timeout: UI_TIMEOUT });
    await this.settingsPage().waitFor({ state: 'visible', timeout: API_TIMEOUT });
  }

  /** Opens Indstillinger by URL (the menu has its own test); waits for the screen root. */
  async gotoSettings(): Promise<void> {
    await this.gotoScreen('settings');
  }

  /**
   * Picks an option in an mtx-select by its exact visible text (no substring match), or by a regexp the
   * caller anchors itself (e.g. one label per UI language), and waits for the panel to close.
   */
  async selectMtxOption(selector: string, text: string | RegExp): Promise<void> {
    await this.page.locator(selector).click({ timeout: UI_TIMEOUT });
    const panel = this.page.locator('.ng-dropdown-panel');
    await panel.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const label = typeof text === 'string' ? exactText(text) : text;
    await panel.locator('.ng-option').filter({ hasText: label }).click({ timeout: UI_TIMEOUT });
    await panel.waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
  }

  /** Picks a property without asserting the outcome (a dirty W4 screen answers with the discard confirm). */
  async pickProperty(name: string): Promise<void> {
    await this.selectMtxOption('#chemicalsPropertySelect', name);
  }

  async selectProperty(name: string): Promise<void> {
    await this.pickProperty(name);
    await expect(this.propertySelect()).toContainText(name, { timeout: UI_TIMEOUT });
  }

  // ----- W4 -------------------------------------------------------------------

  permissionRow(workerName: string): Locator {
    return this.page.locator('tr[id^="chemicalPermissionRow-"]').filter({ hasText: workerName });
  }

  /** The mat-checkbox host of one cell (carries `flag-checkbox--implied`). */
  permissionCell(flag: string, workerName: string): Locator {
    return this.permissionRow(workerName).locator(`mat-checkbox[id^="chemicalPermission-${flag}-"]`);
  }

  permissionCheckbox(flag: string, workerName: string): Locator {
    return this.permissionCell(flag, workerName).locator('input[type="checkbox"]');
  }

  title(): Locator {
    return this.page.locator('#chemicalsTitle');
  }

  undoButton(): Locator {
    return this.page.locator('#chemicalPermissionsUndoBtn');
  }

  saveButton(): Locator {
    return this.page.locator('#chemicalPermissionsSaveBtn');
  }

  copyButton(): Locator {
    return this.page.locator('#chemicalPermissionsCopyBtn');
  }

  /** The "copy from" menu item for a property (the menu renders in the CDK overlay). */
  copyFromItem(propertyName: string): Locator {
    return this.page.locator('.cdk-overlay-container [id^="chemicalPermissionsCopyFrom-"]').filter({ hasText: propertyName });
  }

  pushNote(): Locator {
    return this.page.locator('#chemicalPermissionsPushNote');
  }

  scopeNote(): Locator {
    return this.page.locator('#chemicalPermissionsScopeNote');
  }

  async waitForWorkerRow(workerName: string): Promise<void> {
    await this.permissionRow(workerName).waitFor({ state: 'visible', timeout: API_TIMEOUT });
  }

  saveBar(): Locator {
    return this.page.locator('#chemicalPermissionsSaveBar');
  }

  discardConfirmButton(): Locator {
    return this.page.locator('#chemicalPermissionsDiscardConfirm');
  }

  keepEditingButton(): Locator {
    return this.page.locator('#chemicalPermissionsDiscardConfirmCancel');
  }

  async savePermissions(): Promise<void> {
    const saved = this.waitForChemicalsCall('PUT', /^properties\/\d+\/permissions$/);
    await this.saveButton().click({ timeout: UI_TIMEOUT });
    await saved;
    await this.saveBar().waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
  }

  // ----- W5 -------------------------------------------------------------------

  async saveSettings(): Promise<void> {
    const saved = this.waitForChemicalsCall('PUT', /^properties\/\d+\/settings$/);
    await this.page.locator('#chemicalSettingsSaveBtn').click({ timeout: UI_TIMEOUT });
    await saved;
    await expect(this.page.locator('#chemicalSettingsSaveBtn')).toBeDisabled({ timeout: UI_TIMEOUT });
  }

  // ----- Menu -----------------------------------------------------------------

  /** The menu child that links a screen, e.g. `#backend-configuration-pn-chemicals-inventory`. */
  screenMenuButton(screen: ChemicalScreen): Locator {
    return this.page.locator(`#backend-configuration-pn-chemicals-${screen}`);
  }

  /** Every Kemi menu child, in DOM (menu) order. */
  screenMenuButtons(): Locator {
    return this.page.locator(CHEMICAL_SCREENS_IN_MENU_ORDER.map((screen) => `#backend-configuration-pn-chemicals-${screen}`).join(', '));
  }

  /** Expands the plugin dropdown and the Kemi group until the Kemi children show. */
  async expandChemicalsMenu(): Promise<void> {
    await this.expandChemicalsGroup(this.screenMenuButton('inventory'));
  }

  // ----- W1 / W2 ----------------------------------------------------------------

  inventoryRow(productName: string): Locator {
    return this.page.locator('#chemicalInventoryPage .mat-mdc-row').filter({ hasText: productName });
  }

  async openInventoryRowMenu(productName: string): Promise<ActionMenuItem> {
    return openRowActionMenu(this.page, this.inventoryRow(productName), `inventory row ${productName}`);
  }

  /** "Registrér placering": searches the register for `productName`, picks it, a location and a note, and saves. */
  async registerPlacement(productName: string, locationName: string, note: string): Promise<void> {
    const open = this.page.locator('#chemicalsRegisterPlacementBtn');
    // Enabled once the inventory (and so its active locations) has loaded.
    await expect(open).toBeEnabled({ timeout: API_TIMEOUT });
    await open.click({ timeout: UI_TIMEOUT });
    await this.page.locator('#chemicalRegisterSearchInput').fill(productName, { timeout: UI_TIMEOUT });
    const search = this.waitForChemicalsCall('GET', /^register\/search$/);
    await this.page.locator('#chemicalRegisterSearchBtn').click({ timeout: UI_TIMEOUT });
    await search;
    await this.page.locator('[id^="chemicalRegisterResult-"]').filter({ hasText: productName })
      .locator('input[type="radio"]').check({ timeout: UI_TIMEOUT });
    await this.selectMtxOption('#chemicalRegisterLocationSelect', locationName);
    await this.page.locator('#chemicalRegisterNote').fill(note, { timeout: UI_TIMEOUT });
    const saved = this.waitForChemicalsCall('POST', /^placements$/);
    await this.page.locator('#chemicalRegisterSaveBtn').click({ timeout: UI_TIMEOUT });
    await saved;
    await this.page.locator('#chemicalRegisterSaveBtn').waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    try {
      await this.inventoryRow(productName).first().waitFor({ state: 'visible', timeout: API_TIMEOUT });
    } catch (error) {
      throw new Error(`W1 does not show ${productName} after the save; ${this.browserReport()}`, { cause: error });
    }
  }

  /**
   * Clicks a download trigger (menu item or button), asserts the file arrived (a failed or cancelled
   * download has a failure text) and returns the browser's suggested file name.
   */
  async download(triggerSelector: string): Promise<string> {
    const downloaded = this.page.waitForEvent('download', { timeout: SLOW_API_TIMEOUT });
    await this.page.locator(triggerSelector).click({ timeout: UI_TIMEOUT });
    const file = await downloaded;
    expect(await file.failure(), `download ${file.suggestedFilename()}`).toBeNull();
    return file.suggestedFilename();
  }

  /** Opens the W1 export menu (enabled once the inventory has loaded and no export runs) and downloads one item. */
  async exportFromInventory(itemId: string): Promise<string> {
    const button = this.page.locator('#chemicalsExportBtn');
    await expect(button).toBeEnabled({ timeout: SLOW_API_TIMEOUT });
    await button.click({ timeout: UI_TIMEOUT });
    return this.download(`.cdk-overlay-container #${itemId}`);
  }

  // ----- W3 -------------------------------------------------------------------

  /** All location rows; archived ones only while "show archived" is on. */
  locationRows(): Locator {
    return this.page.locator('#chemicalLocationsList [id^="chemicalLocationRow-"]');
  }

  locationRow(name: string): Locator {
    return this.locationRows().filter({ has: this.page.locator('.chemicals-location-name', { hasText: exactText(name) }) });
  }

  /** The names of the active (not archived) rows, in screen order. */
  activeLocationNames(): Locator {
    return this.page.locator('#chemicalLocationsList [id^="chemicalLocationRow-"]:not(.chemicals-location-row--archived) .chemicals-location-name');
  }

  /** The location's id, read off its row id `chemicalLocationRow-<id>`. */
  async locationId(name: string): Promise<number> {
    const rowId = await this.locationRow(name).getAttribute('id', { timeout: UI_TIMEOUT });
    const id = Number((rowId ?? '').replace('chemicalLocationRow-', ''));
    if (!Number.isInteger(id) || id <= 0) {
      throw new Error(`location row '${name}' has no numeric id (got '${rowId}')`);
    }
    return id;
  }

  async createLocation(name: string): Promise<void> {
    const open = this.page.locator('#chemicalLocationsNewBtn');
    // Enabled once the property's locations have loaded.
    await expect(open).toBeEnabled({ timeout: API_TIMEOUT });
    await open.click({ timeout: UI_TIMEOUT });
    await this.page.locator('#chemicalLocationName').fill(name, { timeout: UI_TIMEOUT });
    const saved = this.waitForChemicalsCall('POST', /^locations$/);
    await this.page.locator('#chemicalLocationSaveBtn').click({ timeout: UI_TIMEOUT });
    await saved;
    await this.locationRow(name).waitFor({ state: 'visible', timeout: API_TIMEOUT });
  }

  /** Waits for the PUT of a new location order and asserts the server took it. */
  waitForLocationOrderSaved(): Promise<unknown> {
    return this.waitForChemicalsCall('PUT', /^properties\/\d+\/locations\/order$/);
  }

  // ----- W6 -------------------------------------------------------------------

  historyRows(): Locator {
    return this.page.locator('#chemicalHistoryTable tr.chemical-history-row');
  }

  /** The history page answered: the live region is empty again (not loading) and the table is there. */
  async waitForHistoryLoaded(): Promise<void> {
    await this.page.locator('#chemicalHistoryTable').waitFor({ state: 'visible', timeout: API_TIMEOUT });
    await expect(this.page.locator('#chemicalHistoryStatus')).toHaveText('', { timeout: API_TIMEOUT });
  }
}
