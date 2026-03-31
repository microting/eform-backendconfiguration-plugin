import { Page, Locator } from '@playwright/test';
import { selectValueInNgSelector } from '../../../helper-functions';

export class BackendConfigurationAreaRulesPage {
  constructor(private page: Page) {}

  async rowNum(): Promise<number> {
    return this.page.locator('#mainTable .mat-row').count();
  }

  ruleCreateBtn(): Locator {
    return this.page.locator('#ruleCreateBtn');
  }

  createAreaRulesString(): Locator {
    return this.page.locator('#createAreaRulesString');
  }

  areaRulesGenerateBtn(): Locator {
    return this.page.locator('#areaRulesGenerateBtn');
  }

  areaRuleCreateSaveCancelBtn(): Locator {
    return this.page.locator('#areaRuleCreateSaveCancelBtn');
  }

  areaRuleCreateSaveBtn(): Locator {
    return this.page.locator('#areaRuleCreateSaveBtn');
  }

  createRuleType(i: number): Locator {
    return this.page.locator(`#createRuleType${i}`);
  }

  createRuleAlarm(i: number): Locator {
    return this.page.locator(`#createRuleAlarm${i}`);
  }

  createAreaDayOfWeek(i: number): Locator {
    return this.page.locator(`#createAreaDayOfWeek${i}`);
  }

  newAreaRulesDayOfWeek(): Locator {
    return this.page.locator('#newAreaRulesDayOfWeek');
  }

  createAreasDayOfWeek(): Locator {
    return this.page.locator('#createAreasDayOfWeek');
  }

  createRuleEformId(i: number): Locator {
    return this.page.locator(`#createRuleEformId${i}`);
  }

  areaRuleDeleteDeleteBtn(): Locator {
    return this.page.locator('#areaRuleDeleteDeleteBtn');
  }

  areaRuleDeleteCancelBtn(): Locator {
    return this.page.locator('#areaRuleDeleteCancelBtn');
  }

  areaRuleEditSaveBtn(): Locator {
    return this.page.locator('#areaRuleEditSaveBtn');
  }

  areaRuleEditSaveCancelBtn(): Locator {
    return this.page.locator('#areaRuleEditSaveCancelBtn');
  }

  editRuleName(i: number): Locator {
    return this.page.locator(`#editRuleName${i}`);
  }

  editRuleEformId(): Locator {
    return this.page.locator('#editRuleEformId');
  }

  editRuleType(): Locator {
    return this.page.locator('#editRuleType');
  }

  editRuleAlarm(): Locator {
    return this.page.locator('#editRuleAlarm');
  }

  editAreaRuleDayOfWeek(): Locator {
    return this.page.locator('#editAreaRuleDayOfWeek');
  }

  async updateAreaRulePlanningSaveBtn(): Promise<void> {
    await this.page.locator('#updateAreaRulePlanningSaveBtn').click();
  }

  async updateAreaRulePlanningSaveCancelBtn(): Promise<void> {
    await this.page.locator('#updateAreaRulePlanningSaveCancelBtn').click();
  }

  planAreaRuleStatusToggle(): Locator {
    return this.page.locator('#planAreaRuleStatusToggle-input');
  }

  planAreaRuleNotificationsToggle(): Locator {
    return this.page.locator('#planAreaRuleNotificationsToggle-input');
  }

  planAreaRuleComplianceEnableToggle(): Locator {
    return this.page.locator('#planAreaRuleComplianceEnableToggle-input');
  }

  planRepeatEvery(): Locator {
    return this.page.locator('#planRepeatEvery');
  }

  planRepeatType(): Locator {
    return this.page.locator('#planRepeatType');
  }

  planStartFrom(): Locator {
    return this.page.locator('#planStartFrom');
  }

  checkboxCreateAssignment(i: number): Locator {
    return this.page.locator(`#checkboxCreateAssignment${i}-input`);
  }

  async updateEntityList(): Promise<void> {
    await this.page.locator('.updateEntityList').click();
  }

  async entityListSaveBtn(): Promise<void> {
    await this.page.locator('#entityListSaveBtn').click();
  }

  async entityListSaveCancelBtn(): Promise<void> {
    await this.page.locator('#entityListSaveCancelBtn').click();
  }

  async addSingleEntitySelectableItem(): Promise<void> {
    await this.page.locator('#addSingleEntitySelectableItem').click();
  }

  entityItemEditNameBox(): Locator {
    return this.page.locator('#entityItemEditNameBox');
  }

  async entityItemSaveBtn(): Promise<void> {
    await this.page.locator('#entityItemSaveBtn').click();
  }

  async entityItemCancelBtn(): Promise<void> {
    await this.page.locator('#entityItemCancelBtn').click();
  }

  async createEntityItemName(i: number): Promise<void> {
    await this.page.locator('#createEntityItemName').nth(i).fill('Entity Name');
  }

  entityItemEditBtn(i: number): Locator {
    return this.page.locator('#entityItemEditBtn').nth(i);
  }

  entityItemDeleteBtn(i: number): Locator {
    return this.page.locator('#entityItemDeleteBtn').nth(i);
  }

  async getCountEntityListItems(): Promise<number> {
    await this.page.waitForTimeout(500);
    return this.page.locator('#createEntityItemName').count();
  }

  getFirstAreaRuleRowObject(): AreaRuleRowObject {
    return new AreaRuleRowObject(this.page, this, 1);
  }

  getAreaRuleRowObjectByIndex(index: number): AreaRuleRowObject {
    return new AreaRuleRowObject(this.page, this, index);
  }

  getFirstRowObject(): AreaRuleRowObject {
    return this.getAreaRuleRowObjectByIndex(1);
  }

  async clearTable(): Promise<void> {
    const rowNum = await this.rowNum();
    for (let i = rowNum; i > 0; i--) {
      await this.getFirstRowObject().delete();
      await this.page.waitForTimeout(500);
    }
  }

  async createAreaRule(areaRule: AreaRuleCreateUpdate, clickCancel = false): Promise<void> {
    await this.openCreateAreaRuleModal(areaRule);
    await this.closeCreateAreaRuleModal(clickCancel);
  }

  async openCreateAreaRuleModal(areaRule?: AreaRuleCreateUpdate): Promise<void> {
    await this.ruleCreateBtn().click();
    await this.areaRuleCreateSaveCancelBtn().waitFor({ state: 'visible' });
    if (areaRule) {
      if (areaRule.name) {
        if (areaRule.dayOfWeek) {
          await selectValueInNgSelector(this.page, '[id^=createAreasDayOfWeek]', areaRule.dayOfWeek);
        }
        await this.createAreaRulesString().fill(areaRule.name);
        await this.areaRulesGenerateBtn().click();
        if (areaRule.type) {
          await selectValueInNgSelector(this.page, '[id^=createRuleType]', areaRule.type);
        }
        if (areaRule.alarm) {
          await selectValueInNgSelector(this.page, '[id^=createRuleAlarm]', areaRule.alarm);
        }
        if (areaRule.eform) {
          await selectValueInNgSelector(this.page, '[id^=createRuleEformId]', areaRule.eform, true);
        }
      }
    }
  }

  async closeCreateAreaRuleModal(clickCancel = false): Promise<void> {
    if (clickCancel) {
      await this.areaRuleCreateSaveCancelBtn().click();
    } else {
      await this.areaRuleCreateSaveBtn().click();
    }
    await this.ruleCreateBtn().waitFor({ state: 'visible' });
  }
}

export class AreaRuleRowObject {
  constructor(
    private page: Page,
    private parentPage: BackendConfigurationAreaRulesPage,
    private rowNum: number = 1,
    private rowName?: string
  ) {}

  private getRowLocator(): Locator {
    if (this.rowName) {
      return this.page
        .locator('.mat-row')
        .filter({ hasText: this.rowName })
        .first();
    }
    return this.page.locator('.mat-row').nth(this.rowNum - 1);
  }

  showAreaRulePlanningBtn(): Locator {
    return this.getRowLocator().locator('[id^=showAreaRulePlanningBtn]').first();
  }

  editRuleBtn(): Locator {
    if (this.rowName) {
      return this.getRowLocator().locator('[id^=editDeviceUserBtn]').first();
    }
    return this.getRowLocator().locator('[id^=showEditRuleBtn]').first();
  }

  deleteRuleBtn(): Locator {
    if (this.rowName) {
      return this.getRowLocator().locator('[id^=deleteDeviceUserBtn]').first();
    }
    return this.getRowLocator().locator('[id^=deleteRuleBtn]').first();
  }

  async delete(clickCancel = false, waitCreateBtn = true): Promise<void> {
    await this.openDeleteModal();
    await this.closeDeleteModal(clickCancel, waitCreateBtn);
  }

  async openDeleteModal(): Promise<void> {
    await this.deleteRuleBtn().click();
    await this.parentPage.areaRuleDeleteCancelBtn().waitFor({ state: 'visible' });
  }

  async closeDeleteModal(clickCancel = false, waitCreateBtn = true): Promise<void> {
    if (clickCancel) {
      await this.parentPage.areaRuleDeleteCancelBtn().click();
    } else {
      await this.parentPage.areaRuleDeleteDeleteBtn().click();
    }
    if (waitCreateBtn) {
      await this.parentPage.ruleCreateBtn().waitFor({ state: 'visible' });
    } else {
      await this.page.waitForTimeout(500);
    }
  }
}

export class AreaRuleCreateUpdate {
  name?: string;
  eform?: string;
  type?: string;
  alarm?: string;
  dayOfWeek?: string;
}
