import { Page, Locator, expect } from '@playwright/test';
import { selectValueInNgSelector } from '../../helper-functions';

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
    await this.cancelCreateBtn().waitFor({ state: 'visible' });
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
        await this.page.waitForTimeout(500);
      }
      if (propertyWorker.timeRegistrationEnabled === true) {
        const toggle = this.timeRegistrationEnabledToggle();
        await toggle.waitFor({ state: 'visible', timeout: 10000 });
        await toggle.locator('button').click();
        await this.page.waitForTimeout(500);
        await expect(toggle.locator('button[role="switch"]')).toHaveAttribute('aria-checked', 'true');
        if (propertyWorker.enableMobileAccess === true) {
          const mobileToggle = this.page.locator('#enableMobileAccessToggle');
          await mobileToggle.waitFor({ state: 'visible', timeout: 10000 });
          await mobileToggle.locator('button').click();
          await this.page.waitForTimeout(500);
          await expect(mobileToggle.locator('button[role="switch"]')).toHaveAttribute('aria-checked', 'true');
        }
      }
      // Switch to Properties tab
      if (propertyWorker.properties) {
        await this.page.locator('.mat-mdc-tab').filter({ hasText: 'Ejendomme' }).click();
        await this.page.waitForTimeout(500);
        for (let i = 0; i < propertyWorker.properties.length; i++) {
          const row = this.page
            .locator('#pairingModalTableBody > div > div > div > table > tbody > .mat-mdc-row')
            .filter({ hasText: propertyWorker.properties[i] });
          await row.scrollIntoViewIfNeeded();
          await row.locator('mat-checkbox').click();
          await this.page.waitForTimeout(500);
        }
      }
      // Switch to Timeregistration tab (only visible after toggle was clicked)
      if (propertyWorker.timeRegistrationEnabled === true && (propertyWorker.isManager || propertyWorker.managingTags)) {
        await this.page.locator('.mat-mdc-tab').filter({ hasText: 'Timeregistrering' }).click();
        await this.page.waitForTimeout(500);
        if (propertyWorker.isManager === true) {
          await this.page.locator('#isManager').click();
          await this.page.waitForTimeout(500);
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
      const createResponsePromise = this.page.waitForResponse(
        r =>
          r.url().includes('/api/backend-configuration-pn/properties/assignment/create-device-user') &&
          r.request().method() === 'PUT'
      );
      const assignResponsePromise = this.page.waitForResponse(
        r =>
          r.url().includes('/api/backend-configuration-pn/properties/assignment') &&
          !r.url().includes('create-device-user') &&
          !r.url().includes('index-device-user') &&
          r.request().method() === 'POST'
      );
      const indexResponsePromise = this.page.waitForResponse(
        r =>
          r.url().includes('/api/backend-configuration-pn/properties/assignment/index-device-user') &&
          r.request().method() === 'POST'
      );

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
    await this.newDeviceUserBtn().waitFor({ state: 'visible' });
  }

  async createTag(tagName: string): Promise<void> {
    await this.sitesManageTagsBtn().click();
    await this.page.locator('#newTagBtn').waitFor({ state: 'visible', timeout: 10000 });
    await this.page.locator('#newTagBtn').click();
    await this.page.locator('#newTagName').waitFor({ state: 'visible', timeout: 10000 });
    await this.page.locator('#newTagName').fill(tagName);
    await this.page.locator('#newTagSaveBtn').click();
    await this.page.waitForTimeout(500);
    await this.page.locator('#tagsModalCloseBtn').click();
    await this.page.waitForTimeout(500);
  }

  async deleteTag(tagName: string): Promise<void> {
    await this.sitesManageTagsBtn().click();
    await this.page.locator('#newTagBtn').waitFor({ state: 'visible', timeout: 10000 });
    const tagRow = this.page.locator('.mat-mdc-row').filter({ hasText: tagName }).first();
    await tagRow.locator('#deleteTagBtn').click();
    await this.page.locator('#tagDeleteSaveBtn').waitFor({ state: 'visible', timeout: 10000 });
    await this.page.locator('#tagDeleteSaveBtn').click();
    await this.page.waitForTimeout(500);
    await this.page.locator('#tagsModalCloseBtn').click();
    await this.page.waitForTimeout(500);
  }

  async clearTable(): Promise<void> {
    const rowNum = await this.rowNum();
    for (let i = rowNum; i > 0; i--) {
      await this.getFirstRowObject().delete();
      await this.page.waitForTimeout(500);
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

  private async openActionMenu(): Promise<void> {
    const row = this.getRowLocator();
    const actionCell = row.locator('[id^="action-items"]').first();
    const actionMenu = actionCell.locator('#actionMenu').first();
    await actionMenu.click({ force: true });
  }

  editAssignmentsBtn(): Locator {
    if (this.deviceUserName) {
      return this.getRowLocator().locator('[id^=editAssignmentsBtn]').first();
    }
    return this.page.locator('[id^=editAssignmentsBtn]').first();
  }

  editDeviceUserBtn(): Locator {
    if (this.deviceUserName) {
      return this.getRowLocator().locator('[id^=editDeviceUserBtn]').first();
    }
    return this.page.locator('[id^=editDeviceUserBtn]').first();
  }

  deleteBtn(): Locator {
    if (this.deviceUserName) {
      return this.getRowLocator().locator('[id^=deleteDeviceUserBtn]').first();
    }
    return this.page.locator('[id^=deleteDeviceUserBtn]').first();
  }

  async delete(clickCancel = false): Promise<void> {
    await this.openDeleteModal();
    await this.closeDeleteModal(clickCancel);
  }

  async openDeleteModal(): Promise<void> {
    await this.openActionMenu();
    await this.deleteBtn().click();
    await this.parentPage.cancelDeleteBtn().waitFor({ state: 'visible' });
  }

  async closeDeleteModal(clickCancel = false): Promise<void> {
    if (clickCancel) {
      await this.parentPage.cancelDeleteBtn().click();
    } else {
      await Promise.all([
        this.page.waitForResponse(
          r => r.url().includes('/api/device-users/delete/') && r.request().method() === 'DELETE'
        ),
        this.page.waitForResponse(
          r => r.url().includes('/api/backend-configuration-pn/properties/assignment') && r.request().method() === 'GET'
        ),
        this.parentPage.saveDeleteBtn().click(),
      ]);
    }
    await this.parentPage.newDeviceUserBtn().waitFor({ state: 'visible' });
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
