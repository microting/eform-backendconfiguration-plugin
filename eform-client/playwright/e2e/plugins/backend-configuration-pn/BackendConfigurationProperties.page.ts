import { Page, Locator, expect } from '@playwright/test';
import { openRowActionMenu } from './row-action-menu';
import { API_TIMEOUT, UI_TIMEOUT, waitForApiResponse } from './wait-helpers';

export class BackendConfigurationPropertiesPage {
  constructor(private page: Page) {}

  backendConfigurationPnButton(): Locator {
    return this.page.locator('#backend-configuration-pn');
  }

  backendConfigurationPnPropertiesButton(): Locator {
    return this.page.locator('#backend-configuration-pn-properties');
  }

  async goToProperties(): Promise<void> {
    const propertiesBtn = this.backendConfigurationPnPropertiesButton();
    const isVisible = await propertiesBtn.isVisible();
    if (!isVisible) {
      await this.backendConfigurationPnButton().click();
    }
    await propertiesBtn.click();
    await this.page.locator('app-properties-container').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  }

  propertyCreateBtn(): Locator {
    return this.page.locator('#propertyCreateBtn');
  }

  createPropertyName(): Locator {
    return this.page.locator('#createPropertyName');
  }

  createCHRNumber(): Locator {
    return this.page.locator('#createCHRNumber');
  }

  createCVRNumber(): Locator {
    return this.page.locator('#createCVRNumber');
  }

  createPropertyAddress(): Locator {
    return this.page.locator('#createPropertyAddress');
  }

  checkboxCreatePropertySelectLanguage(languageId: number): Locator {
    return this.page.locator(`#checkboxCreatePropertySelectLanguage${languageId}`);
  }

  propertyCreateSaveBtn(): Locator {
    return this.page.locator('#propertyCreateSaveBtn');
  }

  propertyCreateSaveCancelBtn(): Locator {
    return this.page.locator('#propertyCreateSaveCancelBtn');
  }

  propertyDeleteDeleteBtn(): Locator {
    return this.page.locator('#propertyDeleteDeleteBtn');
  }

  propertyDeleteCancelBtn(): Locator {
    return this.page.locator('#propertyDeleteCancelBtn');
  }

  editPropertyName(): Locator {
    return this.page.locator('#editPropertyName');
  }

  editCHRNumber(): Locator {
    return this.page.locator('#editCHRNumber');
  }

  editCVRNumber(): Locator {
    return this.page.locator('#editCVRNumber');
  }

  editPropertyAddress(): Locator {
    return this.page.locator('#editPropertyAddress');
  }

  checkboxEditPropertySelectLanguage(languageId: number): Locator {
    return this.page.locator(`#checkboxEditPropertySelectLanguage${languageId}`);
  }

  propertyEditSaveBtn(): Locator {
    return this.page.locator('#propertyEditSaveBtn');
  }

  propertyEditSaveCancelBtn(): Locator {
    return this.page.locator('#propertyEditSaveCancelBtn');
  }

  editPropertyAreasViewSaveBtn(): Locator {
    return this.page.locator('#editPropertyAreasViewSaveBtn');
  }

  editPropertyAreasViewCloseBtn(): Locator {
    return this.page.locator('#editPropertyAreasViewCloseBtn');
  }

  propertyAreasViewCloseBtn(): Locator {
    return this.page.locator('#propertyAreasViewCloseBtn');
  }

  propertyCreateWorkorderFlowEnableToggle(): Locator {
    return this.page.locator("[for='propertyCreateWorkorderFlowEnableToggle-input']");
  }

  propertyEditWorkorderFlowEnableToggleInput(): Locator {
    return this.page.locator('#propertyEditWorkorderFlowEnableToggle');
  }

  propertyEditWorkorderFlowEnableToggle(): Locator {
    return this.page.locator("[for='propertyEditWorkorderFlowEnableToggle']");
  }

  configurePropertyAreasBtn(): Locator {
    return this.page.locator('#configurePropertyAreasBtn');
  }

  navigateToPropertyArea(i: number): Locator {
    return this.page.locator('#navigateToPropertyArea').nth(i);
  }

  async rowNum(): Promise<number> {
    return this.page.locator('app-properties-table .mat-mdc-row').count();
  }

  async createProperty(property: PropertyCreateUpdate, clickCancel = false): Promise<void> {
    await this.openCreatePropertyModal(property);
    await this.closeCreatePropertyModal(clickCancel);
  }

  async openCreatePropertyModal(property: PropertyCreateUpdate): Promise<void> {
    await this.propertyCreateBtn().click();
    await this.propertyCreateSaveCancelBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    if (property) {
      if (property.cvrNumber) {
        await this.createCVRNumber().fill(property.cvrNumber);
      }
      if (property.name) {
        await this.createPropertyName().fill(property.name);
      }
      if (property.chrNumber) {
        await this.createCHRNumber().fill(property.chrNumber);
      }
      if (property.address) {
        await this.createPropertyAddress().fill(property.address);
      }
      if (property.workOrderFlow === true) {
        await this.propertyCreateWorkorderFlowEnableToggle().click();
        await expect(
          this.page.locator('#propertyCreateWorkorderFlowEnableToggle button[role="switch"]')
        ).toHaveAttribute('aria-checked', 'true', { timeout: UI_TIMEOUT });
      }
    }
  }

  async closeCreatePropertyModal(clickCancel = false): Promise<void> {
    if (clickCancel) {
      await this.propertyCreateSaveCancelBtn().click();
    } else {
      await Promise.all([
        waitForApiResponse(
          this.page,
          'POST /api/backend-configuration-pn/properties/index (property list refresh after save)',
          r => r.url().includes('/api/backend-configuration-pn/properties/index') && r.request().method() === 'POST',
          API_TIMEOUT
        ),
        this.propertyCreateSaveBtn().click(),
      ]);
    }
    // The dialog closing is the real post-condition; propertyCreateBtn lives on
    // the page *behind* the dialog, so waiting on it alone proves nothing.
    await this.propertyCreateSaveCancelBtn().waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    await this.propertyCreateBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  }

  getFirstRowObject(): PropertyRowObject {
    return new PropertyRowObject(this.page, this);
  }

  getRowObjectByNum(num: number): PropertyRowObject {
    return new PropertyRowObject(this.page, this, num);
  }

  getRowObjectByName(name: string): PropertyRowObject {
    return new PropertyRowObject(this.page, this, undefined, name);
  }

  getRowObjects(maxNum: number): PropertyRowObject[] {
    const rowObjects: PropertyRowObject[] = [];
    for (let i = 1; i <= maxNum; i++) {
      rowObjects.push(this.getRowObjectByNum(i));
    }
    return rowObjects;
  }

  async clearTable(): Promise<void> {
    await this.page.locator('app-properties-table').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    const rows = this.page.locator('app-properties-table .mat-mdc-row');
    const rowNum = await rows.count();
    for (let i = rowNum; i > 0; i--) {
      await Promise.all([
        waitForApiResponse(
          this.page,
          'POST /api/backend-configuration-pn/properties/index (property list refresh after delete)',
          r => r.url().includes('/api/backend-configuration-pn/properties/index') && r.request().method() === 'POST',
          API_TIMEOUT
        ),
        this.getFirstRowObject().delete(),
      ]);
      await expect(rows).toHaveCount(i - 1, { timeout: UI_TIMEOUT });
    }
  }

  async goToPlanningPage(): Promise<void> {
    const planningsBtn = this.planningsButton();
    const isVisible = await planningsBtn.isVisible();
    if (!isVisible) {
      await this.itemPlanningButton().click();
    }
    await Promise.all([
      waitForApiResponse(
        this.page,
        'POST /api/items-planning-pn/plannings/index (plannings list)',
        r => r.url().includes('/api/items-planning-pn/plannings/index') && r.request().method() === 'POST',
        API_TIMEOUT
      ),
      planningsBtn.click(),
    ]);
    await this.planningCreateBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  }

  planningsButton(): Locator {
    return this.page.locator('#items-planning-pn-plannings');
  }

  itemPlanningButton(): Locator {
    return this.page.locator('#items-planning-pn');
  }

  planningCreateBtn(): Locator {
    return this.page.locator('#planningCreateBtn');
  }
}

export class PropertyRowObject {
  private rowNum: number | undefined;
  private propertyName: string | undefined;

  constructor(
    private page: Page,
    private parentPage: BackendConfigurationPropertiesPage,
    rowNum?: number,
    propertyName?: string
  ) {
    this.rowNum = rowNum ?? 1;
    this.propertyName = propertyName;
  }

  private getRowLocator(): Locator {
    if (this.propertyName) {
      return this.page
        .locator('.mat-mdc-row')
        .filter({ hasText: this.propertyName })
        .first();
    }
    return this.page.locator('.mat-mdc-row').nth((this.rowNum ?? 1) - 1);
  }

  async goToAreas(): Promise<void> {
    const menuItem = await openRowActionMenu(this.page, this.getRowLocator(), 'Property row');
    await menuItem('showPropertyAreasBtn').click({ timeout: UI_TIMEOUT });
    await this.parentPage.configurePropertyAreasBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  }

  async delete(clickCancel = false): Promise<void> {
    await this.openDeleteModal();
    await this.closeDeleteModal(clickCancel);
  }

  async openDeleteModal(): Promise<void> {
    const menuItem = await openRowActionMenu(this.page, this.getRowLocator(), 'Property row');
    await menuItem('deletePropertyBtn').click({ timeout: UI_TIMEOUT });
    await this.parentPage.propertyDeleteCancelBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  }

  async closeDeleteModal(clickCancel = false): Promise<void> {
    if (clickCancel) {
      await this.parentPage.propertyDeleteCancelBtn().click();
    } else {
      await Promise.all([
        waitForApiResponse(
          this.page,
          'DELETE /api/backend-configuration-pn/properties (delete property)',
          r => r.url().includes('/api/backend-configuration-pn/properties') && r.request().method() === 'DELETE',
          API_TIMEOUT
        ),
        this.parentPage.propertyDeleteDeleteBtn().click(),
      ]);
    }
    await this.parentPage.propertyDeleteCancelBtn().waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
    await this.parentPage.propertyCreateBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  }

  async bindAreasByName(areasName: string[] = [], clickCancel = false, returnToProperties = false): Promise<void> {
    await this.openBindAreasModal();
    for (let i = 0; i < areasName.length; i++) {
      const row = this.page
        .locator('mat-dialog-container .mat-mdc-row')
        .filter({ hasText: areasName[i] });
      await row.scrollIntoViewIfNeeded();
      await row.locator('mat-checkbox').click();
    }
    await this.closeBindAreasModal(clickCancel, returnToProperties);
  }

  async bindAreasByNumberInTable(areasNum: number[] = [], clickCancel = false, returnToProperties = false): Promise<void> {
    await this.openBindAreasModal();
    for (let i = 0; i < areasNum.length; i++) {
      const row = this.page.locator('mat-dialog-container .mat-mdc-row').nth(areasNum[i]);
      await row.scrollIntoViewIfNeeded();
      await row.locator('mat-checkbox').click();
    }
    await this.closeBindAreasModal(clickCancel, returnToProperties);
  }

  async bindAllAreas(clickCancel = false, returnToProperties = false): Promise<void> {
    await this.openBindAreasModal();
    const container = this.page
      .locator('mat-dialog-container .mat-mdc-row')
      .locator('..')
      .locator('..')
      .locator('..');
    await container.scrollIntoViewIfNeeded();
    await container.locator('mat-checkbox').click({ force: true });
    await this.closeBindAreasModal(clickCancel, returnToProperties);
  }

  async openBindAreasModal(): Promise<void> {
    await this.parentPage.configurePropertyAreasBtn().click();
    await this.parentPage.editPropertyAreasViewCloseBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  }

  async closeBindAreasModal(clickCancel = false, returnToProperties = false): Promise<void> {
    if (clickCancel) {
      await this.parentPage.editPropertyAreasViewCloseBtn().click();
    } else {
      await Promise.all([
        waitForApiResponse(
          this.page,
          'PUT /api/backend-configuration-pn/property-areas (bind areas to property)',
          r => r.url().includes('/api/backend-configuration-pn/property-areas') && r.request().method() === 'PUT',
          API_TIMEOUT
        ),
        this.parentPage.editPropertyAreasViewSaveBtn().click(),
      ]);
    }
    await this.parentPage.configurePropertyAreasBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    if (returnToProperties) {
      await this.parentPage.goToProperties();
    }
  }

  async goToPropertyAreaByName(nameBindArea: string, needGoToPropertyAreasPage = false): Promise<void> {
    if (needGoToPropertyAreasPage) {
      await this.goToAreas();
    }
    const row = this.page.locator('.mat-mdc-row').filter({ hasText: nameBindArea });
    await row.scrollIntoViewIfNeeded();
    const navigateBtn = row.locator('.cdk-column-book > div').locator('#navigateToPropertyArea');
    await navigateBtn.click();
  }
}

export class PropertyCreateUpdate {
  name?: string;
  chrNumber?: string;
  cvrNumber?: string;
  address?: string;
  workOrderFlow?: boolean;
}
