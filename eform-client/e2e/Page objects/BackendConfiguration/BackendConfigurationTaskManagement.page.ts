import Page from '../Page';
import backendConfigurationPropertiesPage from './BackendConfigurationProperties.page';
import {selectValueInNgSelector} from '../../Helpers/helper-functions';

export class BackendConfigurationTaskManagementPage extends Page {
  constructor() {
    super();
  }

  public async rowNum(): Promise<number> {
    await browser.pause(500);
    return (await $$('#taskManagementTableBody > tr')).length;
  }

  public async backendConfigurationPnTaskManagement() {
    const ele = await $('#backend-configuration-pn-task-management');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async goToTaskManagement() {
    const spinnerAnimation = await $('#spinner-animation');
    await (
      await backendConfigurationPropertiesPage.backendConfigurationPnButton()
    ).click();
    await (await this.backendConfigurationPnTaskManagement()).click();
    await spinnerAnimation.waitForDisplayed({ timeout: 90000, reverse: true });
    await (await this.createNewTaskBtn()).waitForClickable({ timeout: 90000 });
  }

  public async getFirstTaskRowObject(): Promise<TaskRowObject> {
    return await new TaskRowObject().getRow(1);
  }

  public async getLastTaskRowObject(): Promise<TaskRowObject> {
    return await new TaskRowObject().getRow(await this.rowNum());
  }

  public async getTaskRowObjectByIndex(
    index: number
  ): Promise<TaskRowObject> {
    return await new TaskRowObject().getRow(index);
  }

  public async propertyId() {
    const ele = await $('#propertyId');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async areaName() {
    const ele = await $('#areaName');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async assignedTo() {
    const ele = await $('#assignedTo');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async addNewImages() {
    const ele = await $('#addNewImages');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async descriptionTask() {
    const ele = await $('#descriptionTask');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async descriptionTaskInput() {
    const ele = await (await this.descriptionTask()).$('.NgxEditor__Content');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async taskManagementCreateShowSaveBtn() {
    const ele = await $('#taskManagementCreateShowSaveBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async taskManagementCreateShowSaveCancelBtn() {
    const ele = await $('#taskManagementCreateShowSaveCancelBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async showReportBtn() {
    const ele = await $('#showReportBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async wordBtn() {
    const ele = await $('#wordBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async excelBtn() {
    const ele = await $('#excelBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async propertyIdFilter() {
    const ele = await $('#propertyIdFilter');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async areaNameFilter() {
    const ele = await $('#areaNameFilter');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async createdByFilter() {
    const ele = await $('#createdByFilter');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async lastAssignedToFilter() {
    const ele = await $('#lastAssignedToFilter');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async statusFilter() {
    const ele = await $('#statusFilter');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async dateFilter() {
    const ele = await $('#dateFilter');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async taskManagementDeleteBtn() {
    const ele = await $('#taskManagementDeleteBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async taskManagementDeleteCancelBtn() {
    const ele = await $('#taskManagementDeleteCancelBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async createNewTaskBtn() {
    const ele = await $('#createNewTaskBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async clearTable() {
    await browser.pause(2000);
    const rowCount = await this.rowNum();
    let indexForDelete = 1;
    for (let i = 1; i <= rowCount; i++) {
      const task = await new TaskRowObject().getRow(indexForDelete);
      if (
        task &&
        task.deleteTaskBtn &&
        (await task.deleteTaskBtn.isDisplayed())
      ) {
        await task.delete();
      } else {
        indexForDelete += 1;
      }
    }
  }

  public async createTask(
    areaRule?: TaskCreateShow,
    clickCancel = false
  ) {
    await this.openCreateTaskModal(areaRule);
    await this.closeCreateTaskModal(clickCancel);
  }

  public async openCreateTaskModal(task?: TaskCreateShow) {
    await (await this.createNewTaskBtn()).click();
    await (await this.taskManagementCreateShowSaveCancelBtn()).waitForClickable({
      timeout: 40000,
    });
    if (task) {
      if (task.propertyName) {
        await selectValueInNgSelector(await this.propertyId(), task.propertyName)
      }
      if (task.areaName) {
        await selectValueInNgSelector(await this.areaName(), task.areaName)
      }
      if (task.assignedTo) {
        await selectValueInNgSelector(await this.assignedTo(), task.assignedTo)
      }
      if(task.description) {
        await (await this.descriptionTaskInput()).setValue(task.description);
      }
    }
  }

  public async closeCreateTaskModal(clickCancel = false) {
    if (clickCancel) {
      await (await this.taskManagementCreateShowSaveCancelBtn()).click();
    } else {
      await (await this.taskManagementCreateShowSaveBtn()).click();
    }
    await (await this.createNewTaskBtn()).waitForClickable({ timeout: 40000 });
  }
}

const backendConfigurationTaskManagementPage = new BackendConfigurationTaskManagementPage();
export default backendConfigurationTaskManagementPage;

export class TaskRowObject {
  constructor() {}

  public row: WebdriverIO.Element;
  public id: string;
  public createdDate: string;
  public propertyName: string
  public area: string;
  public createdBy1: string;
  public createdBy2: string;
  public lastAssignedTo: string;
  public showTaskBtn: WebdriverIO.Element;
  public deleteTaskBtn?: WebdriverIO.Element;
  public description: string;
  public lastUpdatedDate: string;
  public lastUpdatedBy: string;
  public status: string;

  public async getRow(rowNum: number): Promise<TaskRowObject> {
    this.row = (await $$('#taskManagementTableBody tr'))[rowNum - 1];
    if (this.row) {
      this.id = await (await this.row.$('#id')).getText();
      this.createdDate = await (await this.row.$('#createdDate')).getText();
      this.propertyName = await (await this.row.$('#propertyName')).getText();
      this.area = await (await this.row.$('#areaName')).getText();
      this.createdBy1 = await (await this.row.$('#createdByName')).getText();
      this.createdBy2 = await (await this.row.$('#createdByText')).getText();
      this.lastAssignedTo = await (await this.row.$('#lastAssignedTo')).getText();
      this.showTaskBtn = await this.row.$('#taskManagementViewBtn');
      this.deleteTaskBtn = await this.row.$('#taskManagementDeleteBtn');
      this.description = await (await this.row.$('#description')).getText();
      this.lastUpdatedDate = await (await this.row.$('#lastUpdateDate')).getText();
      this.lastUpdatedBy = await (await this.row.$('#lastUpdatedBy')).getText();
      this.status = await (await this.row.$('#status')).getText();
    }
    return this;
  }

  public async delete(clickCancel = false) {
    if (this.deleteTaskBtn) {
      await this.openDeleteModal();
      await this.closeDeleteModal(clickCancel);
    }
  }

  public async openDeleteModal() {
    if (this.deleteTaskBtn) {
      await this.deleteTaskBtn.click();
      await (
        await backendConfigurationTaskManagementPage.taskManagementDeleteCancelBtn()
      ).waitForClickable({ timeout: 40000 });
    }
  }

  public async closeDeleteModal(clickCancel = false) {
    if (clickCancel) {
      await (
        await backendConfigurationTaskManagementPage.taskManagementDeleteCancelBtn()
      ).click();
    } else {
      await (
        await backendConfigurationTaskManagementPage.taskManagementDeleteBtn()
      ).click();
    }
    await (
      await backendConfigurationTaskManagementPage.createNewTaskBtn()
    ).waitForClickable({ timeout: 40000 });
  }

  public async openShowModal() {
    await this.showTaskBtn.click();
    await (
      await backendConfigurationTaskManagementPage.taskManagementCreateShowSaveCancelBtn()
    ).waitForClickable({ timeout: 40000 });
  }

  public async closeShowModal(clickCancel = false, waitCreateBtn = true) {
    if (clickCancel) {
      await (
        await backendConfigurationTaskManagementPage.taskManagementCreateShowSaveCancelBtn()
      ).click();
    } else {
      await (
        await backendConfigurationTaskManagementPage.taskManagementCreateShowSaveBtn()
      ).waitForClickable({ timeout: 40000 });
      await (
        await backendConfigurationTaskManagementPage.taskManagementCreateShowSaveBtn()
      ).click();
    }
    await (
      await backendConfigurationTaskManagementPage.createNewTaskBtn()
    ).waitForClickable({ timeout: 40000 });
  }

  public async readTask(): Promise<TaskCreateShow> {
    await this.openShowModal();
    const task = new TaskCreateShow();
    task.propertyName =
      await (
        await (await backendConfigurationTaskManagementPage.propertyId()).$(
          'input'
        )
      ).getValue();
    task.areaName =
      await (
        await (await backendConfigurationTaskManagementPage.areaName()).$(
          'input'
        )
      ).getValue();
    task.assignedTo =
      await (
        await (await backendConfigurationTaskManagementPage.assignedTo()).$(
          'input'
        )
      ).getValue();
    task.description =
      await (
        await (await backendConfigurationTaskManagementPage.descriptionTask()).$(
          'input'
        )
      ).getText();
    await this.closeShowModal(true);
    return task;
  }
}

export class TaskCreateShow {
  propertyName: string;
  areaName: string;
  assignedTo: string;
  description?: string;
}
