import { Page, Locator } from '@playwright/test';

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
    await this.page.locator('app-properties-container').waitFor({ state: 'visible' });
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
    await this.propertyCreateSaveCancelBtn().waitFor({ state: 'visible' });
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
        await this.page.waitForTimeout(500);
      }
    }
  }

  async closeCreatePropertyModal(clickCancel = false): Promise<void> {
    if (clickCancel) {
      await this.propertyCreateSaveCancelBtn().click();
    } else {
      const [_response] = await Promise.all([
        this.page.waitForResponse(
          r => r.url().includes('/api/backend-configuration-pn/properties/index') && r.request().method() === 'POST'
        ),
        this.propertyCreateSaveBtn().click(),
      ]);
    }
    await this.page.waitForTimeout(500);
    await this.propertyCreateBtn().waitFor({ state: 'visible' });
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
    await this.page.locator('app-properties-table').waitFor({ state: 'visible' });
    const rowNum = await this.page.locator('app-properties-table .mat-mdc-row').count();
    if (rowNum === 0) {
      return;
    }
    for (let i = rowNum; i > 0; i--) {
      const [_response] = await Promise.all([
        this.page.waitForResponse(
          r => r.url().includes('/api/backend-configuration-pn/properties/index') && r.request().method() === 'POST'
        ),
        this.getFirstRowObject().delete(),
      ]);
      await this.page.waitForTimeout(500);
    }
  }

  async goToPlanningPage(): Promise<void> {
    const planningsBtn = this.planningsButton();
    const isVisible = await planningsBtn.isVisible();
    if (!isVisible) {
      await this.itemPlanningButton().click();
    }
    const [_response] = await Promise.all([
      this.page.waitForResponse(
        r => r.url().includes('/api/items-planning-pn/plannings/index') && r.request().method() === 'POST'
      ),
      planningsBtn.click(),
    ]);
    await this.planningCreateBtn().waitFor({ state: 'visible' });
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

  private async openActionMenu(): Promise<void> {
    const row = this.getRowLocator();
    const actionCell = row.locator('[id^="action-items"]').filter({ hasText: '' }).first();
    const actionMenu = actionCell.locator('#actionMenu').first();
    await actionMenu.click({ force: true });
  }

  viewAreasBtn(): Locator {
    return this.page.locator('[id^=showPropertyAreasBtn]').first();
  }

  editPropertyBtn(): Locator {
    return this.page.locator('[id^=editPropertyBtn]').first();
  }

  deleteBtn(): Locator {
    return this.page.locator('[id^=deletePropertyBtn]').first();
  }

  async goToAreas(): Promise<void> {
    await this.openActionMenu();
    await this.viewAreasBtn().click();
    await this.parentPage.configurePropertyAreasBtn().waitFor({ state: 'visible' });
  }

  async delete(clickCancel = false): Promise<void> {
    await this.openDeleteModal();
    await this.closeDeleteModal(clickCancel);
  }

  async openDeleteModal(): Promise<void> {
    await this.openActionMenu();
    await this.deleteBtn().click();
    await this.parentPage.propertyDeleteCancelBtn().waitFor({ state: 'visible' });
  }

  async closeDeleteModal(clickCancel = false): Promise<void> {
    if (clickCancel) {
      await this.parentPage.propertyDeleteCancelBtn().click();
    } else {
      const [_response] = await Promise.all([
        this.page.waitForResponse(
          r => r.url().includes('/api/backend-configuration-pn/properties') && r.request().method() === 'DELETE'
        ),
        this.parentPage.propertyDeleteDeleteBtn().click(),
      ]);
    }
    await this.parentPage.propertyCreateBtn().waitFor({ state: 'visible' });
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
    await this.parentPage.editPropertyAreasViewCloseBtn().waitFor({ state: 'visible' });
  }

  async closeBindAreasModal(clickCancel = false, returnToProperties = false): Promise<void> {
    if (clickCancel) {
      await this.parentPage.editPropertyAreasViewCloseBtn().click();
    } else {
      const [_response] = await Promise.all([
        this.page.waitForResponse(
          r => r.url().includes('/api/backend-configuration-pn/property-areas') && r.request().method() === 'PUT'
        ),
        this.parentPage.editPropertyAreasViewSaveBtn().click(),
      ]);
    }
    await this.parentPage.configurePropertyAreasBtn().waitFor({ state: 'visible' });
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
