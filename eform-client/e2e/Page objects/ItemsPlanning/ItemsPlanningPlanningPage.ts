import itemsPlanningModalPage from './ItemsPlanningModal.page';
import { PageWithNavbarPage } from '../PageWithNavbar.page';
import { generateRandmString } from '../../Helpers/helper-functions';
import { format, parse } from 'date-fns';

export class ItemsPlanningPlanningPage extends PageWithNavbarPage {
  constructor() {
    super();
  }

  public get rowNum(): number {
    browser.pause(500);
    return $$('#tableBody > tr').length;
  }

  public get planningDeleteDeleteBtn() {
    const el = itemsPlanningModalPage.planningDeleteDeleteBtn;
    el.waitForDisplayed({ timeout: 20000 });
    el.waitForClickable({ timeout: 20000 });
    return el;
  }

  public get planningDeleteCancelBtn() {
    const el = itemsPlanningModalPage.planningDeleteCancelBtn;
    el.waitForDisplayed({ timeout: 20000 });
    el.waitForClickable({ timeout: 20000 });
    return el;
  }

  public clickIdTableHeader() {
    $('#idTableHeader').click();
    $('#spinner-animation').waitForDisplayed({ timeout: 90000, reverse: true });
  }

  public clickNameTableHeader() {
    $('#nameTableHeader').click();
    $('#spinner-animation').waitForDisplayed({ timeout: 90000, reverse: true });
  }

  public clickDescriptionTableHeader() {
    $('#descriptionTableHeader').click();
    $('#spinner-animation').waitForDisplayed({ timeout: 90000, reverse: true });
  }

  public get itemPlanningButton() {
    const el = $('#items-planning-pn');
    el.waitForDisplayed({ timeout: 20000 });
    el.waitForClickable({ timeout: 20000 });
    return el;
  }

  public get planningCreateBtn() {
    const el = $('#planningCreateBtn');
    el.waitForDisplayed({ timeout: 20000 });
    el.waitForClickable({ timeout: 90000 });
    return el;
  }

  public get planningManageTagsBtn() {
    const el = $('#planningManageTagsBtn');
    el.waitForDisplayed({ timeout: 20000 });
    el.waitForClickable({ timeout: 20000 });
    return el;
  }

  public get planningsButton() {
    const el = $('#items-planning-pn-plannings');
    el.waitForDisplayed({ timeout: 20000 });
    el.waitForClickable({ timeout: 20000 });
    return el;
  }

  public get planningId() {
    const el = $('#planningId');
    el.waitForDisplayed({ timeout: 20000 });
    return el;
  }
  public get deleteMultiplePluginsBtn() {
    const ele = $('#deleteMultiplePluginsBtn');
    ele.waitForDisplayed({ timeout: 20000 });
    return ele;
  }

  public get planningsMultipleDeleteCancelBtn() {
    const ele = $('#planningsMultipleDeleteCancelBtn');
    ele.waitForDisplayed({ timeout: 20000 });
    ele.waitForClickable({ timeout: 20000 });
    return ele;
  }

  public get planningsMultipleDeleteDeleteBtn() {
    const ele = $('#planningsMultipleDeleteDeleteBtn');
    ele.waitForDisplayed({ timeout: 20000 });
    return ele;
  }

  public get selectAllPlanningsCheckbox() {
    const ele = $('#selectAllPlanningsCheckbox');
    // ele.waitForDisplayed({ timeout: 20000 });
    // ele.waitForClickable({ timeout: 20000 });
    return ele;
  }

  public get selectAllPlanningsCheckboxForClick() {
    return this.selectAllPlanningsCheckbox.$('..');
  }

  public get importPlanningsBtn() {
    const ele = $('#importPlanningsBtn');
    ele.waitForDisplayed({ timeout: 20000 });
    ele.waitForClickable({ timeout: 20000 });
    return ele;
  }

  public goToPlanningsPage() {
    const spinnerAnimation = $('#spinner-animation');
    this.itemPlanningButton.click();
    this.planningsButton.click();
    spinnerAnimation.waitForDisplayed({ timeout: 90000, reverse: true });
    this.planningCreateBtn.waitForClickable({ timeout: 90000 });
  }

  public getPlaningByName(namePlanning: string) {
    for (let i = 1; i < this.rowNum + 1; i++) {
      const planning = new PlanningRowObject(i);
      if (planning.name === namePlanning) {
        return planning;
      }
    }
    return null;
  }

  public createDummyPlannings(
    template,
    folderName,
    createCount = 3
  ): PlanningCreateUpdate[] {
    const masResult = new Array<PlanningCreateUpdate>();
    for (let i = 0; i < createCount; i++) {
      const planningData: PlanningCreateUpdate = {
        name: [
          generateRandmString(),
          generateRandmString(),
          generateRandmString(),
        ],
        eFormName: template,
        description: generateRandmString(),
        repeatEvery: '1',
        repeatType: 'Dag',
        repeatUntil: new Date('5/15/2020'),
        folderName: folderName,
      };
      masResult.push(planningData);
      itemsPlanningModalPage.createPlanning(planningData);
    }
    return masResult;
  }

  public clearTable() {
    browser.pause(2000);
    const rowCount = itemsPlanningPlanningPage.rowNum;
    for (let i = 1; i <= rowCount; i++) {
      const planningRowObject = new PlanningRowObject(1);
      planningRowObject.delete();
    }
  }

  getAllPlannings(countFirstElements = 0): PlanningRowObject[] {
    browser.pause(1000);
    const resultMas = new Array<PlanningRowObject>();
    if (countFirstElements === 0) {
      countFirstElements = this.rowNum;
    }
    for (let i = 1; i < countFirstElements + 1; i++) {
      resultMas.push(new PlanningRowObject(i));
    }
    return resultMas;
  }

  getPlanningByIndex(i: number): PlanningRowObject {
    return new PlanningRowObject(i);
  }

  openMultipleDelete() {
    if (this.deleteMultiplePluginsBtn.isClickable()) {
      this.deleteMultiplePluginsBtn.click();
    }
  }

  closeMultipleDelete(clickCancel = false) {
    if (clickCancel) {
      this.planningsMultipleDeleteCancelBtn.click();
    } else {
      this.planningsMultipleDeleteDeleteBtn.click();
      $('#spinner-animation').waitForDisplayed({
        timeout: 90000,
        reverse: true,
      });
    }
    this.planningCreateBtn.waitForDisplayed({ timeout: 20000 });
  }

  multipleDelete(clickCancel = false) {
    this.openMultipleDelete();
    this.closeMultipleDelete(clickCancel);
  }

  selectAllPlanningsForDelete(valueCheckbox = true, pickOne = false) {
    if (!pickOne) {
      if (
        this.selectAllPlanningsCheckbox.getValue() !== valueCheckbox.toString()
      ) {
        this.selectAllPlanningsCheckboxForClick.click();
      }
    } else {
      const plannings = this.getAllPlannings();
      for (let i = 0; i < plannings.length; i++) {
        plannings[i].clickOnCheckboxForMultipleDelete();
      }
    }
  }
}

const itemsPlanningPlanningPage = new ItemsPlanningPlanningPage();
export default itemsPlanningPlanningPage;

export class PlanningRowObject {
  constructor(rowNumber) {
    const row = $$('#tableBody tr')[rowNumber - 1];
    if (row) {
      this.id = +row.$('#planningId').getText();
      this.name = row.$('#planningName').getText();
      this.description = row.$('#planningDescription').getText();
      this.folderName = row.$('#planningFolderName').getText();
      this.eFormName = row.$('#planningRelatedEformName').getText();
      this.tags = row.$$('#planningTags').map((element) => element.getText());
      this.repeatEvery = +row.$('#planningRepeatEvery').getText();
      this.repeatType = row.$('#planningRepeatType').getText();
      const date = row.$('#planningRepeatUntil').getText();
      this.repeatUntil = parse(date, 'dd.MM.yyyy HH:mm:ss', new Date());
      this.pairingBtn = row.$('#planningAssignmentBtn');
      this.updateBtn = row.$('#updatePlanningBtn');
      this.deleteBtn = row.$('#deletePlanningBtn');
      try {
        this.checkboxDelete = $(`#planningCheckbox${rowNumber - 1}`);
      } catch (e) {}
      try {
        this.checkboxDeleteForClick = this.checkboxDelete.$('..');
      } catch (e) {}
    }
  }

  public id: number;
  public name: string;
  public description: string;
  public folderName: string;
  public eFormName: string;
  public tags: string[];
  public repeatEvery: number;
  public repeatType: string;
  public repeatUntil: Date;
  public updateBtn: WebdriverIO.Element;
  public deleteBtn: WebdriverIO.Element;
  public pairingBtn: WebdriverIO.Element;
  public checkboxDelete: WebdriverIO.Element;
  public checkboxDeleteForClick: WebdriverIO.Element;

  public static closeEdit(clickCancel = false) {
    if (!clickCancel) {
      itemsPlanningModalPage.planningEditSaveBtn.click();
      $('#spinner-animation').waitForDisplayed({
        timeout: 90000,
        reverse: true,
      });
    } else {
      itemsPlanningModalPage.planningEditCancelBtn.click();
    }
    itemsPlanningPlanningPage.planningId.waitForDisplayed();
  }

  public static closeDelete(clickCancel = false) {
    if (!clickCancel) {
      itemsPlanningPlanningPage.planningDeleteDeleteBtn.click();
      $('#spinner-animation').waitForDisplayed({
        timeout: 90000,
        reverse: true,
      });
    } else {
      itemsPlanningPlanningPage.planningDeleteCancelBtn.click();
    }
    browser.pause(500);
  }

  public openDelete() {
    this.deleteBtn.waitForClickable({ timeout: 20000 });
    this.deleteBtn.click();
    itemsPlanningPlanningPage.planningDeleteDeleteBtn.waitForDisplayed({
      timeout: 20000,
    });
  }

  public openEdit() {
    this.updateBtn.click();
    $('#spinner-animation').waitForDisplayed({ timeout: 90000, reverse: true });
    itemsPlanningModalPage.planningEditSaveBtn.waitForDisplayed({
      timeout: 20000,
    });
  }

  update(
    planning: PlanningCreateUpdate,
    clearTags = false,
    clickCancel = false
  ) {
    this.openEdit();
    const spinnerAnimation = $('#spinner-animation');
    const ngOption = $('.ng-option');
    if (planning.name && planning.name.length > 0) {
      for (let i = 0; i < planning.name.length; i++) {
        if (
          itemsPlanningModalPage.editPlanningItemName(i).getValue() !==
          planning.name[i]
        ) {
          itemsPlanningModalPage
            .editPlanningItemName(i)
            .setValue(planning.name[i]);
        }
      }
    }
    if (
      planning.folderName &&
      itemsPlanningModalPage.editFolderName
        .$('#editFolderSelectorInput')
        .getValue() !== planning.folderName
    ) {
      itemsPlanningModalPage.selectFolder(planning.folderName);
    }
    if (
      planning.eFormName &&
      itemsPlanningModalPage.editPlanningSelector.$('.ng-value').getText() !==
        planning.eFormName
    ) {
      itemsPlanningModalPage.editPlanningSelector
        .$('input')
        .setValue(planning.eFormName);
      spinnerAnimation.waitForDisplayed({ timeout: 90000, reverse: true });
      ngOption.waitForDisplayed({ timeout: 20000 });
      itemsPlanningModalPage.editPlanningSelector
        .$('.ng-dropdown-panel')
        .$(`.ng-option=${planning.eFormName}`)
        .click();
      spinnerAnimation.waitForDisplayed({ timeout: 90000, reverse: true });
    }
    if (clearTags) {
      const clearButton = itemsPlanningModalPage.editPlanningTagsSelector.$(
        'span.ng-clear'
      );
      if (clearButton.isExisting()) {
        clearButton.click();
      }
    }
    if (planning.tags && planning.tags.length > 0) {
      for (let i = 0; i < planning.tags.length; i++) {
        itemsPlanningModalPage.editPlanningTagsSelector.addValue(
          planning.tags[i]
        );
        browser.keys(['Return']);
      }
    }
    if (
      planning.repeatEvery &&
      itemsPlanningModalPage.editRepeatEvery.getValue() !== planning.repeatEvery
    ) {
      itemsPlanningModalPage.editRepeatEvery.setValue(planning.repeatEvery);
    }
    if (
      planning.repeatType &&
      itemsPlanningModalPage.editRepeatType.$('.ng-value-label').getText() !==
        planning.repeatType
    ) {
      itemsPlanningModalPage.editRepeatType
        .$('input')
        .setValue(planning.repeatType);
      spinnerAnimation.waitForDisplayed({ timeout: 90000, reverse: true });
      ngOption.waitForDisplayed({ timeout: 20000 });
      itemsPlanningModalPage.editRepeatType
        .$('ng-dropdown-panel')
        .$(`.ng-option=${planning.repeatType}`)
        .click();
    }
    if (
      planning.startFrom &&
      parse(
        itemsPlanningModalPage.editStartFrom.getValue(),
        'M/d/yyyy',
        new Date()
      ).toISOString() !== planning.startFrom.toISOString()
    ) {
      itemsPlanningModalPage.editStartFrom.setValue(
        format(planning.startFrom, 'M/d/yyyy')
      );
    }
    if (
      planning.repeatUntil &&
      parse(
        itemsPlanningModalPage.editRepeatUntil.getValue(),
        'M/d/yyyy',
        new Date()
      ).toISOString() !== planning.repeatUntil.toISOString()
    ) {
      itemsPlanningModalPage.editRepeatUntil.setValue(
        format(planning.repeatUntil, 'M/d/yyyy')
      );
    }
    if (
      planning.number &&
      itemsPlanningModalPage.editItemNumber.getValue() !== planning.number
    ) {
      itemsPlanningModalPage.editItemNumber.setValue(planning.number);
    }
    if (
      planning.description &&
      itemsPlanningModalPage.editPlanningDescription.getValue() !==
        planning.description
    ) {
      itemsPlanningModalPage.editPlanningDescription.setValue(
        planning.description
      );
    }
    if (
      planning.locationCode &&
      itemsPlanningModalPage.editItemLocationCode.getValue() !==
        planning.locationCode
    ) {
      itemsPlanningModalPage.editItemLocationCode.setValue(
        planning.locationCode
      );
    }
    if (
      planning.buildYear &&
      itemsPlanningModalPage.editItemBuildYear.getValue() !== planning.buildYear
    ) {
      itemsPlanningModalPage.editItemBuildYear.setValue(planning.buildYear);
    }
    if (
      planning.type &&
      itemsPlanningModalPage.editItemType.getValue() !== planning.type
    ) {
      itemsPlanningModalPage.editItemType.setValue(planning.type);
    }
    if (planning.daysBeforeRedeploymentPushMessageRepeat != null) {
      const status = planning.daysBeforeRedeploymentPushMessageRepeat
        ? 'Aktiveret'
        : 'Deaktiveret';
      itemsPlanningModalPage.daysBeforeRedeploymentPushMessageRepeatEdit
        .$('input')
        .setValue(status);
      let value = itemsPlanningModalPage.daysBeforeRedeploymentPushMessageRepeatEdit
        .$('ng-dropdown-panel')
        .$(`.ng-option=${status}`);
      value.waitForDisplayed({ timeout: 40000 });
      value.click();

      itemsPlanningModalPage.editDaysBeforeRedeploymentPushMessage
        .$('input')
        .setValue(planning.daysBeforeRedeploymentPushMessage);
      value = itemsPlanningModalPage.editDaysBeforeRedeploymentPushMessage
        .$('ng-dropdown-panel')
        .$(`.ng-option=${planning.daysBeforeRedeploymentPushMessage}`);
      value.waitForDisplayed({ timeout: 40000 });
      value.click();
    }
    PlanningRowObject.closeEdit(clickCancel);
  }

  delete(clickCancel = false) {
    this.openDelete();
    PlanningRowObject.closeDelete(clickCancel);
  }

  clickOnCheckboxForMultipleDelete(valueCheckbox = true) {
    if (this.checkboxDelete.getValue() !== valueCheckbox.toString()) {
      this.checkboxDeleteForClick.click();
    }
  }
}

export class PlanningCreateUpdate {
  public name: string[];
  public folderName: string;
  public eFormName: string;
  public tags?: string[];
  public repeatEvery?: string;
  public repeatType?: string;
  public startFrom?: Date;
  public repeatUntil?: Date;
  public number?: string;
  public description?: string;
  public locationCode?: string;
  public buildYear?: string;
  public type?: string;
  public daysBeforeRedeploymentPushMessageRepeat?: boolean;
  public daysBeforeRedeploymentPushMessage?: number;
}
