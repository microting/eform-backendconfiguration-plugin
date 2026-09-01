import { Page, Locator, expect } from '@playwright/test';
import { selectValueInNgSelector } from '../../helper-functions';
import { openRowActionMenu } from './row-action-menu';
import {
  API_TIMEOUT,
  ignoreUnhandledRejections,
  SLOW_API_TIMEOUT,
  UI_TIMEOUT,
  waitForApiResponse,
} from './wait-helpers';

export class BackendConfigurationPropertyWorkersPage {
  constructor(private page: Page) {}

  backendConfigurationPnButton(): Locator {
    return this.page.locator('#backend-configuration-pn');
  }

  backendConfigurationPnPropertyWorkers(): Locator {
    return this.page.locator('#backend-configuration-pn-property-workers');
  }

  async goToPropertyWorkers(): Promise<void> {
    const workersBtn = this.backendConfigurationPnPropertyWorkers();
    const isVisible = await workersBtn.isVisible();
    if (!isVisible) {
      await this.backendConfigurationPnButton().click();
    }
    await workersBtn.click();
  }

  createFirstNameInput(): Locator {
    return this.page.locator('#firstName');
  }

  newDeviceUserBtn(): Locator {
    return this.page.locator('#newDeviceUserBtn');
  }

  createLastNameInput(): Locator {
    return this.page.locator('#lastName');
  }

  createEmailInput(): Locator {
    return this.page.locator('#workerEmail');
  }

  getFirstRowObject(): WorkerRowObject {
    return new WorkerRowObject(this.page, this, 1);
  }

  getLastRowLocator(): Locator {
    return this.page.locator('tbody > tr:last-of-type');
  }

  saveCreateBtn(): Locator {
    return this.page.locator('#saveCreateBtn');
  }

  cancelCreateBtn(): Locator {
    return this.page.locator('#cancelCreateBtn');
  }

  editFirstNameInput(): Locator {
    return this.page.locator('#firstName');
  }

  editLastNameInput(): Locator {
    return this.page.locator('#lastName');
  }

  editEmailInput(): Locator {
    return this.page.locator('#workerEmail');
  }

  saveEditBtn(): Locator {
    return this.page.locator('#saveEditBtn');
  }

  cancelEditBtn(): Locator {
    return this.page.locator('#cancelEditBtn');
  }

  saveDeleteBtn(): Locator {
    return this.page.locator('#saveDeleteBtn');
  }

  cancelDeleteBtn(): Locator {
    return this.page.locator('#cancelDeleteBtn');
  }

  profileLanguageSelector(): Locator {
    return this.page.locator('#profileLanguageSelector');
  }

  TaskManagementEnableToggleInput(): Locator {
    return this.page.locator('#taskManagementEnabledToggle');
  }

  timeRegistrationEnabledToggle(): Locator {
    return this.page.locator('#timeRegistrationEnabledToggle');
  }

  tagSelector(): Locator {
    return this.page.locator('#tagSelector');
  }

  sitesManageTagsBtn(): Locator {
    return this.page.locator('#sitesManageTagsBtn');
  }

  profileLanguageSelectorCreate(): Locator {
    return this.page.locator('#profileLanguageSelectorCreate');
  }

  checkboxEditAssignment(i: number): Locator {
    return this.page.locator(`#checkboxCreateAssignment${i}-input`);
  }

  checkboxCreateAssignment(i: number): Locator {
    return this.page.locator(`#checkboxCreateAssignment${i}`);
  }

  async rowNum(): Promise<number> {
    return this.page.locator('.mat-mdc-row').count();
  }

  getDeviceUser(num: number): Locator {
    return this.page.locator(`.mat-mdc-row:nth-child(${num})`);
  }

  async create(propertyWorker: PropertyWorker, clickCancel = false): Promise<void> {
    await this.openCreateModal(propertyWorker);
    await this.closeCreateModal(clickCancel);
  }

  async openCreateModal(propertyWorker: PropertyWorker): Promise<void> {
    await this.newDeviceUserBtn().click();
    await this.cancelCreateBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    // Wait for the component's async init (languages) to complete before
    // filling fields — component exposes this via a data-form-ready
    // attribute so we don't have to poll the save-button's disabled state.
    await expect(this.page.locator('form[data-form-ready]')).toHaveAttribute(
      'data-form-ready',
      'true',
      { timeout: 30000 }
    );
    if (propertyWorker) {
      if (propertyWorker.name) {
        await this.createFirstNameInput().fill(propertyWorker.name);
      }
      if (propertyWorker.surname) {
        await this.createLastNameInput().fill(propertyWorker.surname);
      }
      if (propertyWorker.workerEmail) {
        await this.createEmailInput().fill(propertyWorker.workerEmail);
      }
      if (propertyWorker.language) {
        // Select language inline using ng-select
        const langSelector = this.page.locator('#profileLanguageSelector');
        await langSelector.click();
        await this.page.locator('.ng-option').filter({ hasText: propertyWorker.language }).first().click();
      }
      // Complete all General tab interactions before switching tabs
      if (propertyWorker.tags && propertyWorker.tags.length > 0) {
        for (const tag of propertyWorker.tags) {
          await selectValueInNgSelector(this.page, '#tagSelector', tag);
        }
      }
      if (propertyWorker.workOrderFlow === true) {
        await this.TaskManagementEnableToggleInput().locator('button').click();
        await expect(
          this.TaskManagementEnableToggleInput().locator('button[role="switch"]')
        ).toHaveAttribute('aria-checked', 'true', { timeout: UI_TIMEOUT });
      }
      if (propertyWorker.timeRegistrationEnabled === true) {
        const toggle = this.timeRegistrationEnabledToggle();
        await toggle.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
        await toggle.locator('button').click();
        await expect(toggle.locator('button[role="switch"]')).toHaveAttribute('aria-checked', 'true', {
          timeout: UI_TIMEOUT,
        });
        if (propertyWorker.enableMobileAccess === true) {
          const mobileToggle = this.page.locator('#enableMobileAccessToggle');
          await mobileToggle.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
          await mobileToggle.locator('button').click();
          await expect(mobileToggle.locator('button[role="switch"]')).toHaveAttribute('aria-checked', 'true', {
            timeout: UI_TIMEOUT,
          });
        }
      }
      // Switch to Properties tab
      if (propertyWorker.properties) {
        await this.page.locator('.mat-mdc-tab').filter({ hasText: 'Ejendomme' }).click();
        await this.page.locator('#pairingModalTableBody').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
        for (let i = 0; i < propertyWorker.properties.length; i++) {
          const row = this.page
            .locator('#pairingModalTableBody > div > div > div > table > tbody > .mat-mdc-row')
            .filter({ hasText: propertyWorker.properties[i] });
          await row.scrollIntoViewIfNeeded();
          await row.locator('mat-checkbox').click();
          await expect(row.locator('mat-checkbox input[type="checkbox"]')).toBeChecked({ timeout: UI_TIMEOUT });
        }
      }
      // Switch to Timeregistration tab (only visible after toggle was clicked)
      if (propertyWorker.timeRegistrationEnabled === true && (propertyWorker.isManager || propertyWorker.managingTags)) {
        await this.page.locator('.mat-mdc-tab').filter({ hasText: 'Timeregistrering' }).click();
        await this.page.locator('#isManager').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
        if (propertyWorker.isManager === true) {
          await this.page.locator('#isManager').click();
          await expect(this.page.locator('#isManager input[type="checkbox"]')).toBeChecked({ timeout: UI_TIMEOUT });
          if (propertyWorker.managingTags && propertyWorker.managingTags.length > 0) {
            for (const tag of propertyWorker.managingTags) {
              await selectValueInNgSelector(this.page, 'mtx-select[formControlName="managingTagIds"]', tag);
            }
          }
        }
      }
    }
  }

  async closeCreateModal(clickCancel = false): Promise<void> {
    if (clickCancel) {
      await this.cancelCreateBtn().click();
    } else {
      // Set up all response listeners before clicking save
      // create-device-user provisions through the Microting SDK, so it gets the
      // slower budget; the two follow-ups are plain CRUD calls.
      const createResponsePromise = waitForApiResponse(
        this.page,
        'PUT /api/backend-configuration-pn/properties/assignment/create-device-user',
        r =>
          r.url().includes('/api/backend-configuration-pn/properties/assignment/create-device-user') &&
          r.request().method() === 'PUT',
        SLOW_API_TIMEOUT
      );
      const assignResponsePromise = waitForApiResponse(
        this.page,
        'POST /api/backend-configuration-pn/properties/assignment (assign worker to properties)',
        r =>
          r.url().includes('/api/backend-configuration-pn/properties/assignment') &&
          !r.url().includes('create-device-user') &&
          !r.url().includes('index-device-user') &&
          r.request().method() === 'POST',
        API_TIMEOUT
      );
      const indexResponsePromise = waitForApiResponse(
        this.page,
        'POST /api/backend-configuration-pn/properties/assignment/index-device-user (device user list refresh)',
        r =>
          r.url().includes('/api/backend-configuration-pn/properties/assignment/index-device-user') &&
          r.request().method() === 'POST',
        API_TIMEOUT
      );
      // The backend-error path below never awaits the last two.
      ignoreUnhandledRejections(createResponsePromise, assignResponsePromise, indexResponsePromise);

      // Ensure we're on the General tab so save button validation settles
      const generalTab = this.page.locator('mat-tab-group > .mat-mdc-tab-header .mat-mdc-tab').filter({ hasText: 'General' }).first();
      if (await generalTab.count() > 0) {
        await generalTab.click();
        await expect(generalTab).toHaveAttribute('aria-selected', 'true', { timeout: UI_TIMEOUT });
      }
      await expect(this.saveCreateBtn()).toBeEnabled({ timeout: 30000 });
      await this.saveCreateBtn().click();

      // Wait for create-device-user PUT
      const createResponse = await createResponsePromise;
      const reqBody = createResponse.request().postData();
      const resBody = await createResponse.json().catch(() => null);
      console.log(`create-device-user: status=${createResponse.status()}, success=${resBody?.success}, reqBody=${reqBody}`);

      if (createResponse.status() >= 400) {
        // Backend returned an error (e.g. 500 from missing security group in CI).
        // The user/site/AssignedSite are already created in the DB before the error,
        // but Angular doesn't close the dialog on error. Close it manually.
        console.log(`create-device-user returned ${createResponse.status()}, closing dialog manually`);
        await this.cancelCreateBtn().click();
      } else {
        // Success path: wait for assignment POST and index refresh
        await assignResponsePromise;
        await indexResponsePromise;
      }
    }
    await this.cancelCreateBtn().waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    await this.newDeviceUserBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  }

  async createTag(tagName: string): Promise<void> {
    await this.sitesManageTagsBtn().click();
    await this.page.locator('#newTagBtn').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await this.page.locator('#newTagBtn').click();
    await this.page.locator('#newTagName').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await this.page.locator('#newTagName').fill(tagName);
    await this.page.locator('#newTagSaveBtn').click();
    // The saved tag showing up in the modal's list is the post-condition. Scope
    // to the dialog: the device-user table behind it also has .mat-mdc-row rows,
    // and a worker carrying this tag would otherwise win the match.
    await this.tagsModalRow(tagName).waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await this.closeTagsModal();
  }

  async deleteTag(tagName: string): Promise<void> {
    await this.sitesManageTagsBtn().click();
    await this.page.locator('#newTagBtn').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const tagRow = this.tagsModalRow(tagName);
    await tagRow.locator('#deleteTagBtn').click();
    await this.page.locator('#tagDeleteSaveBtn').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await this.page.locator('#tagDeleteSaveBtn').click();
    // The row disappearing from the modal's list is the post-condition.
    await tagRow.waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    await this.closeTagsModal();
  }

  /** A row of the open tags dialog, never the table behind it. */
  private tagsModalRow(tagName: string): Locator {
    return this.page.locator('mat-dialog-container .mat-mdc-row').filter({ hasText: tagName }).first();
  }

  private async closeTagsModal(): Promise<void> {
    await this.page.locator('#tagsModalCloseBtn').click();
    await this.page.locator('#newTagBtn').waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
  }

  async clearTable(): Promise<void> {
    const rows = this.page.locator('.mat-mdc-row');
    const rowNum = await rows.count();
    for (let i = rowNum; i > 0; i--) {
      await this.getFirstRowObject().delete();
      await expect(rows).toHaveCount(i - 1, { timeout: UI_TIMEOUT });
    }
  }
}

export class WorkerRowObject {
  constructor(
    private page: Page,
    private parentPage: BackendConfigurationPropertyWorkersPage,
    private rowNum: number = 1,
    private deviceUserName?: string
  ) {}

  private getRowLocator(): Locator {
    if (this.deviceUserName) {
      return this.page
        .locator('.mat-mdc-row')
        .filter({ hasText: this.deviceUserName })
        .first();
    }
    return this.page.locator('.mat-mdc-row').nth(this.rowNum - 1);
  }

  async delete(clickCancel = false): Promise<void> {
    await this.openDeleteModal();
    await this.closeDeleteModal(clickCancel);
  }

  async openDeleteModal(): Promise<void> {
    const menuItem = await openRowActionMenu(this.page, this.getRowLocator(), 'Device-user row');
    await menuItem('deleteDeviceUserBtn').click({ timeout: UI_TIMEOUT });
    await this.parentPage.cancelDeleteBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  }

  async closeDeleteModal(clickCancel = false): Promise<void> {
    if (clickCancel) {
      await this.parentPage.cancelDeleteBtn().click();
    } else {
      await Promise.all([
        waitForApiResponse(
          this.page,
          'DELETE /api/device-users/delete/ (delete device user)',
          r => r.url().includes('/api/device-users/delete/') && r.request().method() === 'DELETE',
          API_TIMEOUT
        ),
        waitForApiResponse(
          this.page,
          'GET /api/backend-configuration-pn/properties/assignment (device user list refresh after delete)',
          r => r.url().includes('/api/backend-configuration-pn/properties/assignment') && r.request().method() === 'GET',
          API_TIMEOUT
        ),
        this.parentPage.saveDeleteBtn().click(),
      ]);
    }
    await this.parentPage.cancelDeleteBtn().waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    await this.parentPage.newDeviceUserBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  }
}

export class PropertyWorker {
  name?: string;
  surname?: string;
  language?: string;
  properties?: string[];
  workOrderFlow?: boolean;
  workerEmail?: string;
  timeRegistrationEnabled?: boolean;
  enableMobileAccess?: boolean;
  isManager?: boolean;
  managingTags?: string[];
  tags?: string[];
}
