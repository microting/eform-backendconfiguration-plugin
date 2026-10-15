import { Locator, Page, expect } from '@playwright/test';
import { API_TIMEOUT, UI_TIMEOUT, waitForApiResponse } from './wait-helpers';

const BASE_URL = 'http://localhost:4200';

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
 *
 * No sleeps: every wait is a bounded wait on a concrete condition.
 */
export class BackendConfigurationChemicalsPage {
  constructor(public page: Page) {}

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

  /** Opens Kemi > Rettigheder through the sidebar: expands each dropdown only if the next level is hidden. */
  async goToPermissionsViaMenu(): Promise<void> {
    if (!(await this.permissionsMenuButton().isVisible())) {
      if (!(await this.chemicalsGroupButton().isVisible())) {
        await this.pluginMenuButton().click({ timeout: UI_TIMEOUT });
      }
      await this.chemicalsGroupButton().click({ timeout: UI_TIMEOUT });
    }
    await this.permissionsMenuButton().click({ timeout: UI_TIMEOUT });
    await this.permissionsPage().waitFor({ state: 'visible', timeout: API_TIMEOUT });
  }

  /** Opens Rettigheder by URL (the menu has its own test); waits for the screen root. */
  async gotoPermissions(): Promise<void> {
    await this.page.goto(`${BASE_URL}/plugins/backend-configuration-pn/chemicals/permissions`);
    await this.permissionsPage().waitFor({ state: 'visible', timeout: API_TIMEOUT });
  }

  settingsPage(): Locator {
    return this.page.locator('#chemicalSettingsPage');
  }

  /** Opens Kemi > Indstillinger through the sidebar: expands each dropdown only if the next level is hidden. */
  async goToSettingsViaMenu(): Promise<void> {
    if (!(await this.settingsMenuButton().isVisible())) {
      if (!(await this.chemicalsGroupButton().isVisible())) {
        await this.pluginMenuButton().click({ timeout: UI_TIMEOUT });
      }
      await this.chemicalsGroupButton().click({ timeout: UI_TIMEOUT });
    }
    await this.settingsMenuButton().click({ timeout: UI_TIMEOUT });
    await this.settingsPage().waitFor({ state: 'visible', timeout: API_TIMEOUT });
  }

  /** Opens Indstillinger by URL (the menu has its own test); waits for the screen root. */
  async gotoSettings(): Promise<void> {
    await this.page.goto(`${BASE_URL}/plugins/backend-configuration-pn/chemicals/settings`);
    await this.settingsPage().waitFor({ state: 'visible', timeout: API_TIMEOUT });
  }

  /** Picks an option in an mtx-select by its exact visible text (no substring match) and waits for the panel to close. */
  async selectMtxOption(selector: string, text: string): Promise<void> {
    await this.page.locator(selector).click({ timeout: UI_TIMEOUT });
    const panel = this.page.locator('.ng-dropdown-panel');
    await panel.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await panel.locator('.ng-option').filter({ hasText: exactText(text) }).click({ timeout: UI_TIMEOUT });
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
    const saved = waitForApiResponse(
      this.page,
      'PUT chemicals/properties/{id}/permissions',
      (r) =>
        /\/api\/backend-configuration-pn\/chemicals\/properties\/\d+\/permissions$/.test(r.url()) &&
        r.request().method() === 'PUT',
      API_TIMEOUT
    );
    await this.saveButton().click({ timeout: UI_TIMEOUT });
    const response = await saved;
    expect((await response.json()).success).toBe(true);
    await this.saveBar().waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
  }

  // ----- W5 -------------------------------------------------------------------

  async saveSettings(): Promise<void> {
    const saved = waitForApiResponse(
      this.page,
      'PUT chemicals/properties/{id}/settings',
      (r) =>
        /\/api\/backend-configuration-pn\/chemicals\/properties\/\d+\/settings$/.test(r.url()) &&
        r.request().method() === 'PUT',
      API_TIMEOUT
    );
    await this.page.locator('#chemicalSettingsSaveBtn').click({ timeout: UI_TIMEOUT });
    const response = await saved;
    expect((await response.json()).success).toBe(true);
    await expect(this.page.locator('#chemicalSettingsSaveBtn')).toBeDisabled({ timeout: UI_TIMEOUT });
  }
}
