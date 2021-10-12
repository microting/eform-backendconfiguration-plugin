import backendConfigurationPropertiesPage from './BackendConfigurationProperties.page';
import Page from '../Page';

class BackendConfigurationPropertyWorkersPage extends Page {
  constructor() {
    super();
  }

  public async backendConfigurationPnPropertyWorkers() {
    const ele = await $('#backend-configuration-pn-property-workers');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async goToPropertyWorkers() {
    const spinnerAnimation = await $('#spinner-animation');
    await (
      await backendConfigurationPropertiesPage.backendConfigurationPnButton()
    ).click();
    await (await this.backendConfigurationPnPropertyWorkers()).click();
    await spinnerAnimation.waitForDisplayed({ timeout: 90000, reverse: true });
    await (await this.newDeviceUserBtn()).waitForClickable({ timeout: 90000 });
  }

  public async newDeviceUserBtn(): Promise<WebdriverIO.Element> {
    const ele = await $('#newDeviceUserBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async createFirstNameInput(): Promise<WebdriverIO.Element> {
    const ele = await $('#firstName');
    await ele.waitForDisplayed({ timeout: 40000 });
    // ele.waitForClickable({timeout: 40000});
    return ele;
  }

  public async createLastNameInput(): Promise<WebdriverIO.Element> {
    const ele = await $('#lastName');
    await ele.waitForDisplayed({ timeout: 40000 });
    // ele.waitForClickable({timeout: 40000});
    return ele;
  }

  async getFirstRowObject(): Promise<PropertyWorkerRowObject> {
    const result = new PropertyWorkerRowObject();
    return await result.getRow(1);
  }

  async getLastRowObject(): Promise<PropertyWorkerRowObject> {
    const result = new PropertyWorkerRowObject();
    return await result.getRow(await this.rowNum());
  }

  public async saveCreateBtn(): Promise<WebdriverIO.Element> {
    const ele = await $('#saveCreateBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async cancelCreateBtn(): Promise<WebdriverIO.Element> {
    const ele = await $('#cancelCreateBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async editFirstNameInput(): Promise<WebdriverIO.Element> {
    const ele = await $('#editFirstNameInput');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async editLastNameInput(): Promise<WebdriverIO.Element> {
    const ele = await $('#editLastNameInput');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async saveEditBtn(): Promise<WebdriverIO.Element> {
    const ele = await $('#saveEditBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async cancelEditBtn(): Promise<WebdriverIO.Element> {
    const ele = await $('#cancelEditBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async saveDeleteBtn(): Promise<WebdriverIO.Element> {
    const ele = await $('#saveDeleteBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async cancelDeleteBtn(): Promise<WebdriverIO.Element> {
    const ele = await $('#cancelDeleteBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async profileLanguageSelector() {
    const ele = await $('#profileLanguageSelector');
    // await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async profileLanguageSelectorCreate() {
    const ele = await $('#profileLanguageSelectorCreate');
    // await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async checkboxEditAssignment(i: number) {
    const ele = await $(`#checkboxEditAssignment${i}`);
    // await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async checkboxCreateAssignment(i: number) {
    const ele = await $(`#checkboxCreateAssignment${i}`);
    // await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async rowNum(): Promise<number> {
    await browser.pause(500);
    return (await $$('#tableBody > tr')).length;
  }

  async getDeviceUser(num): Promise<PropertyWorkerRowObject> {
    const result = new PropertyWorkerRowObject();
    return await result.getRow(num);
  }

  async getDeviceUserByName(name: string): Promise<PropertyWorkerRowObject> {
    for (let i = 1; i < (await this.rowNum()) + 1; i++) {
      const deviceUser = await this.getDeviceUser(i);
      if (deviceUser.firstName === name) {
        return deviceUser;
      }
    }
    return null;
  }

  async create(propertyWorker?: PropertyWorker, clickCancel = false) {
    await this.openCreateModal(propertyWorker);
    await this.closeCreateModal(clickCancel);
  }

  async openCreateModal(propertyWorker?: PropertyWorker) {
    await (await this.newDeviceUserBtn()).waitForClickable({ timeout: 40000 });
    await (await this.newDeviceUserBtn()).click();
    await (
      await backendConfigurationPropertyWorkersPage.cancelCreateBtn()
    ).waitForClickable({
      timeout: 40000,
    });
    if (propertyWorker) {
      if (propertyWorker.name) {
        await (
          await backendConfigurationPropertyWorkersPage.createFirstNameInput()
        ).setValue(propertyWorker.name);
      }
      if (propertyWorker.surname) {
        await (
          await backendConfigurationPropertyWorkersPage.createLastNameInput()
        ).setValue(propertyWorker.surname);
      }
      if (propertyWorker.language) {
        await (
          await (
            await backendConfigurationPropertyWorkersPage.profileLanguageSelectorCreate()
          ).$('input')
        ).setValue(propertyWorker.language);
        const value = await (
          await backendConfigurationPropertyWorkersPage.profileLanguageSelectorCreate()
        ).$(`.ng-option=${propertyWorker.language}`);
        value.waitForDisplayed({ timeout: 40000 });
        await value.click();
      }
      if (propertyWorker.properties) {
        for (let i = 0; i < propertyWorker.properties.length; i++) {
          await (
            await (
              await backendConfigurationPropertyWorkersPage.checkboxCreateAssignment(
                propertyWorker.properties[i]
              )
            ).$('..')
          ).click();
        }
      }
    }
  }

  async closeCreateModal(clickCancel = false) {
    if (clickCancel) {
      await (await this.cancelCreateBtn()).click();
    } else {
      await (await this.saveCreateBtn()).click();
    }
    await (
      await backendConfigurationPropertyWorkersPage.newDeviceUserBtn()
    ).waitForClickable({ timeout: 40000 });
  }

  public async clearTable() {
    await browser.pause(2000);
    const rowCount = await this.rowNum();
    for (let i = 1; i <= rowCount; i++) {
      await (await new PropertyWorkerRowObject().getRow(1)).delete();
    }
  }
}

const backendConfigurationPropertyWorkersPage = new BackendConfigurationPropertyWorkersPage();
export default backendConfigurationPropertyWorkersPage;

export class PropertyWorkerRowObject {
  constructor() {}

  siteId: number;
  firstName: string;
  lastName: string;
  language: string;
  editBtn: WebdriverIO.Element;
  deleteBtn: WebdriverIO.Element;

  async getRow(rowNum: number) {
    if ((await $$('#deviceUserId'))[rowNum - 1]) {
      this.siteId = +(await (await $$('#deviceUserId')[rowNum - 1]).getText());
      try {
        this.firstName = await (
          await $$('#deviceUserFirstName')[rowNum - 1]
        ).getText();
      } catch (e) {}
      try {
        this.lastName = await (
          await $$('#deviceUserLastName')[rowNum - 1]
        ).getText();
      } catch (e) {}
      this.language = await (await $$('#deviceUserLanguage'))[
        rowNum - 1
      ].getText();
      this.editBtn = (await $$('#editDeviceUserBtn'))[rowNum - 1];
      this.deleteBtn = (await $$('#deleteDeviceUserBtn'))[rowNum - 1];
    }
    return this;
  }

  async delete(clickCancel = false) {
    await this.openDeleteModal();
    await this.closeDeleteModal(clickCancel);
  }

  async openDeleteModal() {
    this.deleteBtn.waitForClickable({ timeout: 40000 });
    this.deleteBtn.click();
    await (
      await backendConfigurationPropertyWorkersPage.saveDeleteBtn()
    ).waitForClickable({
      timeout: 40000,
    });
  }

  async closeDeleteModal(clickCancel = false) {
    if (clickCancel) {
      await (
        await backendConfigurationPropertyWorkersPage.cancelDeleteBtn()
      ).click();
    } else {
      await (
        await backendConfigurationPropertyWorkersPage.saveDeleteBtn()
      ).click();
    }
    await (
      await backendConfigurationPropertyWorkersPage.newDeviceUserBtn()
    ).waitForClickable({ timeout: 40000 });
  }

  async edit(propertyWorker?: PropertyWorker, clickCancel = false) {
    await this.openEditModal(propertyWorker);
    await this.closeEditModal(clickCancel);
  }

  async openEditModal(propertyWorker?: PropertyWorker) {
    await this.editBtn.waitForClickable({ timeout: 40000 });
    await this.editBtn.click();
    await (
      await backendConfigurationPropertyWorkersPage.cancelEditBtn()
    ).waitForClickable({
      timeout: 40000,
    });
    if (propertyWorker) {
      if (propertyWorker.name) {
        await (
          await backendConfigurationPropertyWorkersPage.editFirstNameInput()
        ).setValue(propertyWorker.name);
      }
      if (propertyWorker.surname) {
        await (
          await backendConfigurationPropertyWorkersPage.editLastNameInput()
        ).setValue(propertyWorker.surname);
      }
      if (propertyWorker.language) {
        await (
          await (
            await backendConfigurationPropertyWorkersPage.profileLanguageSelector()
          ).$('input')
        ).setValue(propertyWorker.language);
        const value = await (
          await backendConfigurationPropertyWorkersPage.profileLanguageSelector()
        ).$(`.ng-option=${propertyWorker.language}`);
        value.waitForDisplayed({ timeout: 40000 });
        await value.click();
      }
      if (propertyWorker.properties) {
        for (let i = 0; i < propertyWorker.properties.length; i++) {
          await (
            await (
              await backendConfigurationPropertyWorkersPage.checkboxEditAssignment(
                propertyWorker.properties[i]
              )
            ).$('..')
          ).click();
        }
      }
    }
  }

  async closeEditModal(clickCancel = false) {
    if (clickCancel) {
      await (
        await backendConfigurationPropertyWorkersPage.cancelEditBtn()
      ).click();
    } else {
      await (
        await backendConfigurationPropertyWorkersPage.saveEditBtn()
      ).click();
    }
    await (
      await backendConfigurationPropertyWorkersPage.newDeviceUserBtn()
    ).waitForDisplayed();
  }

  async getAssignedProperties(): Promise<
    { propertyName: string; checked: boolean }[]
  > {
    await this.openEditModal();
    const pairingEditModalTableBody = await $('#pairingEditModalTableBody');
    let masForReturn: { propertyName: string; checked: boolean }[] = new Array<{
      propertyName: string;
      checked: boolean;
    }>();
    const propertyLength = await (
      await pairingEditModalTableBody.$$('#propertyName')
    ).length;
    for (let i = 0; i < propertyLength; i++) {
      masForReturn = [
        ...masForReturn,
        {
          propertyName: await (
            await pairingEditModalTableBody.$$('#propertyName')
          )[i].getText(),
          checked:
            (await (
              await backendConfigurationPropertyWorkersPage.checkboxEditAssignment(
                i
              )
            ).getValue()) === 'true',
        },
      ];
    }
    await this.closeEditModal(true);
    return masForReturn;
  }
}

export class PropertyWorker {
  name?: string;
  surname?: string;
  language?: string;
  properties?: number[];
}
