import Page from '../Page';

export class BackendConfigurationAreaRulesPage extends Page {
  constructor() {
    super();
  }

  public async rowNum(): Promise<number> {
    await browser.pause(500);
    return (await $$('#areaRulesTableBody > tr')).length;
  }

  public async ruleCreateBtn() {
    const ele = await $('#ruleCreateBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async createAreaRulesString() {
    const ele = await $('#createAreaRulesString');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async areaRulesGenerateBtn() {
    const ele = await $('#areaRulesGenerateBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async areaRuleCreateSaveCancelBtn() {
    const ele = await $('#areaRuleCreateSaveCancelBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async areaRuleCreateSaveBtn() {
    const ele = await $('#areaRuleCreateSaveBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async createRuleType(i: number) {
    return $(`#createRuleType${i}`);
  }

  public async createRuleAlarm(i: number) {
    return $(`#createRuleAlarm${i}`);
  }

  public async createRuleChecklistStable(i: number) {
    return $(`#createRuleChecklistStable${i}`);
  }

  public async createRuleTailBite(i: number) {
    return $(`#createRuleTailBite${i}`);
  }

  public async createAreaDayOfWeek(i: number) {
    return $(`#createAreaDayOfWeek${i}`);
  }

  public async newAreaRulesDayOfWeek() {
    return $(`#newAreaRulesDayOfWeek`);
  }

  public async createAreasDayOfWeek() {
    return $(`#createAreasDayOfWeek`);
  }

  public async createRuleEformId(i: number) {
    return $(`#createRuleEformId${i}`);
  }

  public async areaRuleDeleteDeleteBtn() {
    const ele = await $('#areaRuleDeleteDeleteBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async areaRuleDeleteCancelBtn() {
    const ele = await $('#areaRuleDeleteCancelBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async areaRuleEditSaveBtn() {
    const ele = await $('#areaRuleEditSaveBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async areaRuleEditSaveCancelBtn() {
    const ele = await $('#areaRuleEditSaveCancelBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async editRuleName(i: number) {
    const ele = await $(`#editRuleName${i}`);
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async editRuleEformId() {
    return $('#editRuleEformId');
  }

  public async editRuleChecklistStable() {
    return $('#editRuleChecklistStable');
  }

  public async editRuleTailBite() {
    return $('#editRuleTailBite');
  }

  public async editRuleType() {
    return $('#editRuleType');
  }

  public async editRuleAlarm() {
    return $('#editRuleAlarm');
  }

  public async editAreaRuleDayOfWeek() {
    return $('#editAreaRuleDayOfWeek');
  }

  public async updateAreaRulePlanningSaveBtn() {
    const ele = await $(`#updateAreaRulePlanningSaveBtn`);
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async updateAreaRulePlanningSaveCancelBtn() {
    const ele = await $(`#updateAreaRulePlanningSaveCancelBtn`);
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async planAreaRuleStatusToggle() {
    return $(`#planAreaRuleStatusToggle`);
  }

  public async planAreaRuleNotificationsToggle() {
    return $(`#planAreaRuleNotificationsToggle`);
  }

  public async planRepeatEvery() {
    return $(`#planRepeatEvery`);
  }

  public async planRepeatType() {
    return $(`#planRepeatType`);
  }

  public async planStartFrom() {
    return $(`#planStartFrom`);
  }

  public async checkboxCreateAssignment(i: number) {
    return $(`#checkboxCreateAssignment${i}`);
  }

  public async getFirstAreaRuleRowObject(): Promise<AreaRuleRowObject> {
    return await new AreaRuleRowObject().getRow(1);
  }

  public async getLastAreaRuleRowObject(): Promise<AreaRuleRowObject> {
    return await new AreaRuleRowObject().getRow(await this.rowNum());
  }

  public async getAreaRuleRowObjectByIndex(
    index: number
  ): Promise<AreaRuleRowObject> {
    return await new AreaRuleRowObject().getRow(index);
  }

  public async clearTable() {
    await browser.pause(2000);
    const rowCount = await this.rowNum();
    let indexForDelete = 1;
    for (let i = 1; i <= rowCount; i++) {
      const areaRule = await new AreaRuleRowObject().getRow(indexForDelete);
      if (
        areaRule &&
        areaRule.deleteRuleBtn &&
        (await areaRule.deleteRuleBtn.isDisplayed())
      ) {
        await areaRule.delete();
      } else {
        indexForDelete += 1;
      }
    }
  }

  public async createAreaRule(
    areaRule?: AreaRuleCreateUpdate,
    clickCancel = false
  ) {
    await this.openCreateAreaRuleModal(areaRule);
    await this.closeCreateAreaRuleModal(clickCancel);
  }

  public async openCreateAreaRuleModal(areaRule?: AreaRuleCreateUpdate) {
    await (await this.ruleCreateBtn()).click();
    await (await this.areaRuleCreateSaveCancelBtn()).waitForClickable({
      timeout: 40000,
    });
    if (areaRule) {
      if (areaRule.name) {
        if (areaRule.dayOfWeek) {
          await (await (await this.createAreasDayOfWeek()).$('input')).setValue(
            areaRule.dayOfWeek
          );
          const value = await (await this.createAreasDayOfWeek()).$(
            `.ng-option=${areaRule.dayOfWeek}`
          );
          value.waitForDisplayed({ timeout: 40000 });
          await value.click();
        }
        await (await this.createAreaRulesString()).setValue(areaRule.name);
        await (await this.areaRulesGenerateBtn()).click();
        if (areaRule.type) {
          await (await (await this.createRuleType(0)).$('input')).setValue(
            areaRule.type
          );
          const value = await (await this.createRuleType(0)).$(
            `.ng-option=${areaRule.type}`
          );
          value.waitForDisplayed({ timeout: 40000 });
          await value.click();
        }
        if (areaRule.alarm) {
          await (await (await this.createRuleAlarm(0)).$('input')).setValue(
            areaRule.alarm
          );
          const value = await (await this.createRuleAlarm(0)).$(
            `.ng-option=${areaRule.alarm}`
          );
          value.waitForDisplayed({ timeout: 40000 });
          await value.click();
        }
        if (areaRule.checkListStable !== undefined) {
          const checkListStable = areaRule.checkListStable ? 'Ja' : 'Ingen';
          await (
            await (await this.createRuleChecklistStable(0)).$('input')
          ).setValue(checkListStable);
          const value = await (await this.createRuleAlarm(0)).$(
            `.ng-option=${checkListStable}`
          );
          value.waitForDisplayed({ timeout: 40000 });
          await value.click();
        }
        if (areaRule.tailBite !== undefined) {
          const tailBite = areaRule.checkListStable ? 'Ja' : 'Ingen';
          await (await (await this.createRuleTailBite(0)).$('input')).setValue(
            tailBite
          );
          const value = await (await this.createRuleTailBite(0)).$(
            `.ng-option=${tailBite}`
          );
          value.waitForDisplayed({ timeout: 40000 });
          await value.click();
        }
        // if (areaRule.dayOfWeek) {
        //   await (await (await this.createAreaDayOfWeek(0)).$('input')).setValue(
        //     areaRule.dayOfWeek
        //   );
        //   const value = await (await this.createAreaDayOfWeek(0)).$(
        //     `.ng-option=${areaRule.dayOfWeek}`
        //   );
        //   value.waitForDisplayed({ timeout: 40000 });
        //   await value.click();
        // }
        if (areaRule.eform) {
          await (await (await this.createRuleEformId(0)).$('input')).setValue(
            areaRule.eform
          );
          const value = await (await this.createRuleEformId(0)).$(
            `.ng-option=${areaRule.eform}`
          );
          value.waitForDisplayed({ timeout: 40000 });
          await value.click();
        }
      }
    }
  }

  public async closeCreateAreaRuleModal(clickCancel = false) {
    if (clickCancel) {
      await (await this.areaRuleCreateSaveCancelBtn()).click();
    } else {
      await (await this.areaRuleCreateSaveBtn()).click();
    }
    await (await this.ruleCreateBtn()).waitForClickable({ timeout: 40000 });
  }
}

const backendConfigurationAreaRulesPage = new BackendConfigurationAreaRulesPage();
export default backendConfigurationAreaRulesPage;

export class AreaRuleRowObject {
  constructor() {}

  public row: WebdriverIO.Element;
  public name: string;
  public eform?: string;
  public rulePlanningStatus: boolean;
  public ruleType?: string;
  public ruleAlarm?: string;
  public ruleChecklistStable?: string;
  public ruleTailBite?: string;
  public ruleWeekDay?: string;
  public showAreaRulePlanningBtn: WebdriverIO.Element;
  public editRuleBtn?: WebdriverIO.Element;
  public deleteRuleBtn?: WebdriverIO.Element;

  public async getRow(rowNum: number): Promise<AreaRuleRowObject> {
    this.row = (await $$('#areaRulesTableBody tr'))[rowNum - 1];
    if (this.row) {
      this.name = await (await this.row.$('#ruleName')).getText();
      this.rulePlanningStatus =
        (await (await this.row.$('#rulePlanningStatus')).getText()) === 'Til';
      this.showAreaRulePlanningBtn = await this.row.$(
        '#showAreaRulePlanningBtn'
      );
      try {
        const ele1 = await this.row.$('#ruleEformNameT1');
        const ele2 = await this.row.$('#ruleEformNameT3');
        const ele3 = await this.row.$('#ruleEformNameT5');
        if (ele1 && (await ele1.isDisplayed())) {
          this.eform = await ele1.getText();
        } else if (ele2 && (await ele2.isDisplayed())) {
          this.eform = await ele2.getText();
        } else if (ele3 && (await ele3.isDisplayed())) {
          this.eform = await ele3.getText();
        }
      } catch (e) {}
      try {
        const ele = await this.row.$('#ruleType');
        if (ele && (await ele.isDisplayed())) {
          this.ruleType = await ele.getText();
        }
      } catch (e) {}
      try {
        const ele = await this.row.$('#ruleAlarm');
        if (ele && (await ele.isDisplayed())) {
          this.ruleAlarm = await ele.getText();
        }
      } catch (e) {}
      try {
        const ele = await this.row.$('#ruleChecklistStable');
        if (ele && (await ele.isDisplayed())) {
          this.ruleChecklistStable = await ele.getText();
        }
      } catch (e) {}
      try {
        const ele = await this.row.$('#ruleTailBite');
        if (ele && (await ele.isDisplayed())) {
          this.ruleTailBite = await ele.getText();
        }
      } catch (e) {}
      try {
        const ele = await this.row.$('#ruleWeekDay');
        if (ele && (await ele.isDisplayed())) {
          this.ruleWeekDay = await ele.getText();
        }
      } catch (e) {}
      try {
        this.editRuleBtn = await this.row.$('#showEditRuleBtn');
      } catch (e) {}
      try {
        this.deleteRuleBtn = await this.row.$('#deleteRuleBtn');
      } catch (e) {}
    }
    return this;
  }

  public async delete(clickCancel = false, waitCreateBtn = true) {
    if (this.deleteRuleBtn) {
      await this.openDeleteModal();
      await this.closeDeleteModal(clickCancel, waitCreateBtn);
    }
  }

  public async openDeleteModal() {
    if (this.deleteRuleBtn) {
      await this.deleteRuleBtn.click();
      await (
        await backendConfigurationAreaRulesPage.areaRuleDeleteCancelBtn()
      ).waitForClickable({ timeout: 40000 });
    }
  }

  public async closeDeleteModal(clickCancel = false, waitCreateBtn = true) {
    if (clickCancel) {
      await (
        await backendConfigurationAreaRulesPage.areaRuleDeleteCancelBtn()
      ).click();
    } else {
      await (
        await backendConfigurationAreaRulesPage.areaRuleDeleteDeleteBtn()
      ).click();
    }
    if (waitCreateBtn) {
      await (
        await backendConfigurationAreaRulesPage.ruleCreateBtn()
      ).waitForClickable({ timeout: 40000 });
    } else {
      browser.pause(500);
    }
  }

  public async edit(
    areaRule: AreaRuleCreateUpdate,
    clickCancel = false,
    waitCreateBtn = true
  ) {
    await this.openEditModal(areaRule);
    await this.closeEditModal(clickCancel, waitCreateBtn);
  }

  public async openEditModal(areaRule: AreaRuleCreateUpdate) {
    await this.editRuleBtn.click();
    await (
      await backendConfigurationAreaRulesPage.areaRuleEditSaveCancelBtn()
    ).waitForClickable({ timeout: 40000 });
    if (areaRule) {
      if (areaRule.name) {
        await (
          await backendConfigurationAreaRulesPage.editRuleName(0)
        ).setValue(areaRule.name);
      }
      if (areaRule.type) {
        await (
          await (await backendConfigurationAreaRulesPage.editRuleType()).$(
            'input'
          )
        ).setValue(areaRule.type);
        const value = await (
          await backendConfigurationAreaRulesPage.editRuleType()
        ).$(`.ng-option=${areaRule.type}`);
        value.waitForDisplayed({ timeout: 40000 });
        await value.click();
      }
      if (areaRule.alarm) {
        await (
          await (await backendConfigurationAreaRulesPage.editRuleAlarm()).$(
            'input'
          )
        ).setValue(areaRule.alarm);
        const value = await (
          await backendConfigurationAreaRulesPage.editRuleAlarm()
        ).$(`.ng-option=${areaRule.alarm}`);
        value.waitForDisplayed({ timeout: 40000 });
        await value.click();
      }
      if (areaRule.checkListStable !== undefined) {
        const checkListStable = areaRule.checkListStable ? 'Ja' : 'Ingen';
        await (
          await (
            await backendConfigurationAreaRulesPage.editRuleChecklistStable()
          ).$('input')
        ).setValue(checkListStable);
        const value = await (
          await backendConfigurationAreaRulesPage.editRuleChecklistStable()
        ).$(`.ng-option=${checkListStable}`);
        value.waitForDisplayed({ timeout: 40000 });
        await value.click();
      }
      if (areaRule.tailBite !== undefined) {
        const tailBite = areaRule.checkListStable ? 'Ja' : 'Ingen';
        await (
          await (await backendConfigurationAreaRulesPage.editRuleTailBite()).$(
            'input'
          )
        ).setValue(tailBite);
        const value = await (
          await backendConfigurationAreaRulesPage.editRuleTailBite()
        ).$(`.ng-option=${tailBite}`);
        value.waitForDisplayed({ timeout: 40000 });
        await value.click();
      }
      if (areaRule.dayOfWeek) {
        await (
          await (
            await backendConfigurationAreaRulesPage.editAreaRuleDayOfWeek()
          ).$('input')
        ).setValue(areaRule.dayOfWeek);
        const value = await (
          await backendConfigurationAreaRulesPage.editAreaRuleDayOfWeek()
        ).$(`.ng-option=${areaRule.dayOfWeek}`);
        value.waitForDisplayed({ timeout: 40000 });
        await value.click();
      }
      if (areaRule.eform) {
        await (
          await (await backendConfigurationAreaRulesPage.editRuleEformId()).$(
            'input'
          )
        ).setValue(areaRule.eform);
        browser.pause(500);
        const value = await (
          await backendConfigurationAreaRulesPage.editRuleEformId()
        ).$(`.ng-option=${areaRule.eform}`);
        value.waitForDisplayed({ timeout: 40000 });
        value.waitForClickable({ timeout: 40000 });
        await value.click();
      }
    }
  }

  public async closeEditModal(clickCancel = false, waitCreateBtn = true) {
    if (clickCancel) {
      await (
        await backendConfigurationAreaRulesPage.areaRuleEditSaveCancelBtn()
      ).click();
    } else {
      await (
        await backendConfigurationAreaRulesPage.areaRuleEditSaveBtn()
      ).waitForClickable({ timeout: 40000 });
      await (
        await backendConfigurationAreaRulesPage.areaRuleEditSaveBtn()
      ).click();
    }
    if (waitCreateBtn) {
      await (
        await backendConfigurationAreaRulesPage.ruleCreateBtn()
      ).waitForClickable({ timeout: 40000 });
    } else {
      browser.pause(500);
    }
  }

  public async createUpdatePlanning(
    areaRulePlanningCreateUpdate?: AreaRulePlanningCreateUpdate,
    clickCancel = false,
    waitCreateBtn = true
  ) {
    await this.openPlanningModal(areaRulePlanningCreateUpdate);
    await this.closePlanningModal(clickCancel, waitCreateBtn);
  }

  public async openPlanningModal(
    areaRulePlanningCreateUpdate?: AreaRulePlanningCreateUpdate
  ) {
    await this.showAreaRulePlanningBtn.click();
    await (
      await backendConfigurationAreaRulesPage.updateAreaRulePlanningSaveCancelBtn()
    ).waitForClickable({ timeout: 40000 });
    if (areaRulePlanningCreateUpdate) {
      if (areaRulePlanningCreateUpdate.status !== undefined) {
        await (
          await await backendConfigurationAreaRulesPage.planAreaRuleStatusToggle()
        ).click();
      }
      if (areaRulePlanningCreateUpdate.notification !== undefined) {
        await (
          await await backendConfigurationAreaRulesPage.planAreaRuleNotificationsToggle()
        ).click();
      }
      if (areaRulePlanningCreateUpdate.repeatEvery) {
        await (
          await backendConfigurationAreaRulesPage.planRepeatEvery()
        ).setValue(areaRulePlanningCreateUpdate.repeatEvery);
      }
      if (areaRulePlanningCreateUpdate.repeatType) {
        await (
          await (await backendConfigurationAreaRulesPage.planRepeatType()).$(
            'input'
          )
        ).setValue(areaRulePlanningCreateUpdate.repeatType);
        const value = await (
          await backendConfigurationAreaRulesPage.planRepeatType()
        ).$(`.ng-option=${areaRulePlanningCreateUpdate.repeatType}`);
        value.waitForDisplayed({ timeout: 40000 });
        await value.click();
      }
      if (areaRulePlanningCreateUpdate.startDate) {
        await (
          await backendConfigurationAreaRulesPage.planStartFrom()
        ).setValue(areaRulePlanningCreateUpdate.startDate);
      }
      if (areaRulePlanningCreateUpdate.workers) {
        for (let i = 0; i < areaRulePlanningCreateUpdate.workers.length; i++) {
          await (
            await (
              await backendConfigurationAreaRulesPage.checkboxCreateAssignment(
                areaRulePlanningCreateUpdate.workers[i].workerNumber
              )
            ).$('..')
          ).click();
        }
      }
    }
  }

  public async closePlanningModal(clickCancel = false, waitCreateBtn = true) {
    if (clickCancel) {
      await (
        await backendConfigurationAreaRulesPage.updateAreaRulePlanningSaveCancelBtn()
      ).click();
    } else {
      await (
        await backendConfigurationAreaRulesPage.updateAreaRulePlanningSaveBtn()
      ).waitForClickable({ timeout: 40000 });
      await (
        await backendConfigurationAreaRulesPage.updateAreaRulePlanningSaveBtn()
      ).click();
    }
    if (waitCreateBtn) {
      await (
        await backendConfigurationAreaRulesPage.ruleCreateBtn()
      ).waitForClickable({ timeout: 40000 });
    } else {
      browser.pause(500);
    }
  }

  public async readPlanning(
    waitCreateBtn = true
  ): Promise<AreaRulePlanningCreateUpdate> {
    await this.openPlanningModal();
    const plan = new AreaRulePlanningCreateUpdate();
    plan.status =
      (await (
        await backendConfigurationAreaRulesPage.planAreaRuleStatusToggle()
      ).getValue()) === 'true';
    if (
      await (
        await backendConfigurationAreaRulesPage.planAreaRuleNotificationsToggle()
      ).isDisplayed()
    ) {
      plan.notification =
        (await (
          await backendConfigurationAreaRulesPage.planAreaRuleNotificationsToggle()
        ).getValue()) === 'true';
    }
    if (
      await (
        await backendConfigurationAreaRulesPage.planRepeatEvery()
      ).isDisplayed()
    ) {
      plan.repeatEvery = await (
        await backendConfigurationAreaRulesPage.planRepeatEvery()
      ).getValue();
    }
    if (
      await (
        await backendConfigurationAreaRulesPage.planRepeatType()
      ).isDisplayed()
    ) {
      plan.repeatType = await (
        await (await backendConfigurationAreaRulesPage.planRepeatType()).$(
          'input'
        )
      ).getValue();
    }
    if (
      await (
        await backendConfigurationAreaRulesPage.planStartFrom()
      ).isDisplayed()
    ) {
      plan.startDate = await (
        await backendConfigurationAreaRulesPage.planStartFrom()
      ).getValue();
    }
    plan.workers = [];
    const masWorkers = await $$('#pairingModalTableBody > tr');
    for (let i = 0; i < masWorkers.length; i++) {
      const workerName = await (await masWorkers[i].$$('td')[1]).getText();
      const workerChecked =
        (await (
          await backendConfigurationAreaRulesPage.checkboxCreateAssignment(i)
        ).getValue()) === 'true';
      plan.workers = [
        ...plan.workers,
        { name: workerName, checked: workerChecked },
      ];
    }
    await this.closePlanningModal(true, waitCreateBtn);
    return plan;
  }
}

export class AreaRuleCreateUpdate {
  name?: string;
  eform?: string;
  type?: string;
  alarm?: string;
  checkListStable?: boolean;
  tailBite?: boolean;
  dayOfWeek?: string;
}

export class AreaRulePlanningCreateUpdate {
  status?: boolean;
  notification?: boolean;
  repeatEvery?: string;
  repeatType?: string;
  startDate?: string;
  workers?: { workerNumber?: number; name?: string; checked?: boolean }[];
}
