import { Locator, Page, expect } from '@playwright/test';
import { API_TIMEOUT, UI_TIMEOUT, waitForApiResponse } from './wait-helpers';

const BASE_URL = 'http://localhost:4200';

/**
 * Page object for the Kemi web screens (flutter-chemistry spec §11),
 * `/plugins/backend-configuration-pn/chemicals/<screen>`. DOM reference:
 * `modules/chemicals/` in this repo.
 *
 *   - Nav: the plugin dropdown `#backend-configuration-pn` holds the Kemi group
 *     `#backend-configuration-pn-chemicals`, which holds one child per shipped
 *     screen, e.g. `#backend-configuration-pn-chemicals-permissions` (all seeded
 *     by PluginMenuSyncService from EformBackendConfigurationPlugin).
 *   - Shell: title `#chemicalsTitle`, property picker `#chemicalsPropertySelect`
 *     (mtx-select). E1 has no tab bar: each screen is a menu child.
 *   - W4: `#chemicalPermissionsPage`, rows `#chemicalPermissionRow-<workerId>`,
 *     checkboxes `#chemicalPermission-<flag>-<workerId>`, save bar
 *     `#chemicalPermissionsSaveBar` with `#chemicalPermissionsUndoBtn` /
 *     `#chemicalPermissionsSaveBtn`, discard confirm
 *     `#chemicalPermissionsDiscardConfirm` / `#chemicalPermissionsDiscardConfirmCancel`.
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

  /** Picks an option in an mtx-select by its visible text and waits for the panel to close. */
  async selectMtxOption(selector: string, text: string): Promise<void> {
    await this.page.locator(selector).click({ timeout: UI_TIMEOUT });
    const panel = this.page.locator('.ng-dropdown-panel');
    await panel.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await panel.locator('.ng-option').filter({ hasText: text }).first().click({ timeout: UI_TIMEOUT });
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
    await this.page.locator('#chemicalPermissionsSaveBtn').click({ timeout: UI_TIMEOUT });
    const response = await saved;
    expect((await response.json()).success).toBe(true);
    await this.saveBar().waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
  }
}
