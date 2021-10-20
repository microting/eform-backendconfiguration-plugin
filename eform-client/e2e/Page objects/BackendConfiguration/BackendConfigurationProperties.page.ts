import Page from '../Page';
import { applicationLanguages } from '../../../src/app/common/const';
import * as R from 'ramda';

export class BackendConfigurationPropertiesPage extends Page {
  constructor() {
    super();
  }

  public async rowNum(): Promise<number> {
    await browser.pause(500);
    return (await $$('#properiesTableBody > tr')).length;
  }

  public async backendConfigurationPnButton() {
    const ele = await $('#backend-configuration-pn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async backendConfigurationPnPropertiesButton() {
    const ele = await $('#backend-configuration-pn-properties');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async propertyCreateBtn() {
    const ele = await $('#propertyCreateBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async createPropertyName() {
    const ele = await $('#createPropertyName');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async createCHRNumber() {
    const ele = await $('#createCHRNumber');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async createPropertyAddress() {
    const ele = await $('#createPropertyAddress');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async checkboxCreatePropertySelectLanguage(languageId: number) {
    const ele = await $(`#checkboxCreatePropertySelectLanguage${languageId}`);
    // await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async propertyCreateSaveBtn() {
    const ele = await $('#propertyCreateSaveBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async propertyCreateSaveCancelBtn() {
    const ele = await $('#propertyCreateSaveCancelBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async propertyDeleteDeleteBtn() {
    const ele = await $('#propertyDeleteDeleteBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async propertyDeleteCancelBtn() {
    const ele = await $('#propertyDeleteCancelBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async editPropertyName() {
    const ele = await $('#editPropertyName');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async editCHRNumber() {
    const ele = await $('#editCHRNumber');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async editPropertyAddress() {
    const ele = await $('#editPropertyAddress');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async checkboxEditPropertySelectLanguage(languageId: number) {
    const ele = await $(`#checkboxEditPropertySelectLanguage${languageId}`);
    // await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async propertyEditSaveBtn() {
    const ele = await $('#propertyEditSaveBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async propertyEditSaveCancelBtn() {
    const ele = await $('#propertyEditSaveCancelBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async editPropertyAreasViewSaveBtn() {
    const ele = await $('#editPropertyAreasViewSaveBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    // await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async editPropertyAreasViewCloseBtn() {
    const ele = await $('#editPropertyAreasViewCloseBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async propertyAreasViewCloseBtn() {
    const ele = await $('#propertyAreasViewCloseBtn');
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForClickable({ timeout: 40000 });
    return ele;
  }

  public async navigateToPropertyArea(i: number) {
    const ele = await $$(`#navigateToPropertyArea`)[i];
    await ele.waitForDisplayed({ timeout: 40000 });
    await ele.waitForDisplayed({ timeout: 40000 });
    return ele;
  }

  public async goToProperties() {
    const spinnerAnimation = await $('#spinner-animation');
    await (await this.backendConfigurationPnButton()).click();
    await (await this.backendConfigurationPnPropertiesButton()).click();
    await spinnerAnimation.waitForDisplayed({ timeout: 90000, reverse: true });
    await (await this.propertyCreateBtn()).waitForClickable({ timeout: 90000 });
  }

  public async createProperty(
    property: PropertyCreateUpdate,
    clickCancel = false
  ) {
    await this.openCreatePropertyModal(property);
    await this.closeCreatePropertyModal(clickCancel);
  }

  public async openCreatePropertyModal(property: PropertyCreateUpdate) {
    await (await this.propertyCreateBtn()).click();
    await (await this.propertyCreateSaveCancelBtn()).waitForClickable({
      timeout: 40000,
    });
    if (property) {
      if (property.name) {
        await (await this.createPropertyName()).setValue(property.name);
      }
      if (property.chrNumber) {
        await (await this.createCHRNumber()).setValue(property.chrNumber);
      }
      if (property.address) {
        await (await this.createPropertyAddress()).setValue(property.address);
      }
      if (property.selectedLanguages) {
        for (let i = 0; i < property.selectedLanguages.length; i++) {
          let languageId = 0;
          if (property.selectedLanguages[i].languageId) {
            languageId = property.selectedLanguages[i].languageId;
          } else {
            languageId = applicationLanguages.find(
              (x) => x.text === property.selectedLanguages[i].languageName
            ).id;
          }
          const checkboxForClick = await (
            await this.checkboxCreatePropertySelectLanguage(languageId)
          ).$('..');
          await checkboxForClick.click();
        }
      }
    }
  }

  public async closeCreatePropertyModal(clickCancel = false) {
    if (clickCancel) {
      await (await this.propertyCreateSaveCancelBtn()).click();
    } else {
      await (await this.propertyCreateSaveBtn()).click();
    }
    await (await this.propertyCreateBtn()).waitForClickable({ timeout: 40000 });
  }

  public async getFirstPropertyRowObject(): Promise<PropertyRowObject> {
    return await new PropertyRowObject().getRow(1);
  }

  public async getLastPropertyRowObject(): Promise<PropertyRowObject> {
    return await new PropertyRowObject().getRow(await this.rowNum());
  }

  public async getPropertyRowObjectByIndex(
    index: number
  ): Promise<PropertyRowObject> {
    return await new PropertyRowObject().getRow(index);
  }

  public async clearTable() {
    await browser.pause(2000);
    const rowCount = await this.rowNum();
    for (let i = 1; i <= rowCount; i++) {
      await (await new PropertyRowObject().getRow(1)).delete();
    }
  }
}

const backendConfigurationPropertiesPage = new BackendConfigurationPropertiesPage();
export default backendConfigurationPropertiesPage;

export class PropertyRowObject {
  constructor() {}

  public row: WebdriverIO.Element;
  public id: number;
  public name: string;
  public chrNumber: string;
  public address: string;
  public languages: { languageId: number; languageName: string }[];
  public showPropertyAreasBtn: WebdriverIO.Element;
  public editPropertyAreasBtn: WebdriverIO.Element;
  public editPropertyBtn: WebdriverIO.Element;
  public deletePropertyBtn: WebdriverIO.Element;

  public async getRow(rowNum: number): Promise<PropertyRowObject> {
    this.row = await $$('#properiesTableBody tr')[rowNum - 1];
    if (this.row) {
      this.id = +(await this.row.$('#propertyId')).getText();
      this.name = await (await this.row.$('#propertyName')).getText();
      this.chrNumber = await (await this.row.$('#propertyCHR')).getText();
      this.address = await (await this.row.$('#propertyAddress')).getText();
      const languages = (
        await (await this.row.$('#propertyLanguages')).getText()
      ).split(' | ');

      this.languages = [];
      if (languages.length > 0 && languages[0] !== '') {
        for (let i = 0; i < languages.length; i++) {
          const language = applicationLanguages.find(
            (x) => x.text === languages[i]
          );
          this.languages = [
            ...this.languages,
            { languageName: language.text, languageId: language.id },
          ];
        }
      }
      this.showPropertyAreasBtn = await this.row.$('#showPropertyAreasBtn');
      this.editPropertyAreasBtn = await this.row.$('#editPropertyAreasBtn');
      this.editPropertyBtn = await this.row.$('#editPropertyBtn');
      this.deletePropertyBtn = await this.row.$('#deletePropertyBtn');
    }
    return this;
  }

  public async delete(clickCancel = false) {
    await this.openDeleteModal();
    await this.closeDeleteModal(clickCancel);
  }

  public async openDeleteModal() {
    await this.deletePropertyBtn.click();
    await (
      await backendConfigurationPropertiesPage.propertyDeleteCancelBtn()
    ).waitForClickable({ timeout: 40000 });
  }

  public async closeDeleteModal(clickCancel = false) {
    if (clickCancel) {
      await (
        await backendConfigurationPropertiesPage.propertyDeleteCancelBtn()
      ).click();
    } else {
      await (
        await backendConfigurationPropertiesPage.propertyDeleteDeleteBtn()
      ).click();
    }
    await (
      await backendConfigurationPropertiesPage.propertyCreateBtn()
    ).waitForClickable({ timeout: 40000 });
  }

  public async edit(property: PropertyCreateUpdate, clickCancel = false) {
    await this.openEditModal(property);
    await this.closeEditModal(clickCancel);
  }

  public async openEditModal(property: PropertyCreateUpdate) {
    await this.editPropertyBtn.click();
    await (
      await backendConfigurationPropertiesPage.propertyEditSaveCancelBtn()
    ).waitForClickable({ timeout: 40000 });
    if (property) {
      if (property.name) {
        await (
          await backendConfigurationPropertiesPage.editPropertyName()
        ).setValue(property.name);
      }
      if (property.chrNumber) {
        await (
          await backendConfigurationPropertiesPage.editCHRNumber()
        ).setValue(property.chrNumber);
      }
      if (property.address) {
        await (
          await backendConfigurationPropertiesPage.editPropertyAddress()
        ).setValue(property.address);
      }
      if (property.selectedLanguages) {
        for (let i = 0; i < property.selectedLanguages.length; i++) {
          let languageId = 0;
          if (property.selectedLanguages[i].languageId) {
            languageId = property.selectedLanguages[i].languageId;
          } else {
            languageId = applicationLanguages.find(
              (x) => x.text === property.selectedLanguages[i].languageName
            ).id;
          }
          const checkboxForClick = await (
            await backendConfigurationPropertiesPage.checkboxEditPropertySelectLanguage(
              languageId
            )
          ).$('..');
          await checkboxForClick.click();
        }
      }
    }
  }

  public async closeEditModal(clickCancel = false) {
    if (clickCancel) {
      await (
        await backendConfigurationPropertiesPage.propertyEditSaveCancelBtn()
      ).click();
    } else {
      await (
        await backendConfigurationPropertiesPage.propertyEditSaveBtn()
      ).click();
    }
    await (
      await backendConfigurationPropertiesPage.propertyCreateBtn()
    ).waitForClickable({ timeout: 40000 });
  }

  public async bindOrUnbindWithAllAreas(clickCancel = false) {
    await this.editBindWithAreas(R.times(R.identity, 23), clickCancel);
  }

  public async editBindWithAreas(bindAreas?: number[], clickCancel = false) {
    await this.openBindPropertyWithAreasModal(bindAreas);
    await this.closeBindPropertyWithAreasModal(clickCancel);
  }

  public async openBindPropertyWithAreasModal(bindAreas?: number[]) {
    await this.editPropertyAreasBtn.click();
    await (
      await backendConfigurationPropertiesPage.editPropertyAreasViewCloseBtn()
    ).waitForClickable({ timeout: 40000 });
    if (bindAreas) {
      for (let i = 0; i < bindAreas.length; i++) {
        await (
          await $(`#checkboxAssignmentEdit${bindAreas[i]}`).$('..')
        ).click();
      }
    }
  }

  public async closeBindPropertyWithAreasModal(clickCancel = false) {
    if (clickCancel) {
      await (
        await backendConfigurationPropertiesPage.editPropertyAreasViewCloseBtn()
      ).click();
    } else {
      await (
        await backendConfigurationPropertiesPage.editPropertyAreasViewSaveBtn()
      ).click();
    }
    await (
      await backendConfigurationPropertiesPage.propertyCreateBtn()
    ).waitForClickable({ timeout: 40000 });
  }

  public async getBindAreas() {
    await this.openBindPropertyWithAreasModal();
    const checkboxes = await $$(`[id^="checkboxAssignmentEdit"]`);
    let mas = [];
    for (let i = 0; i < checkboxes.length; i++) {
      mas = [...mas, !!(await (await checkboxes[i]).getValue())];
    }
    await this.closeBindPropertyWithAreasModal(true);
    return mas;
  }

  public async openAreasViewModal(indexAreaForClick: number) {
    await this.showPropertyAreasBtn.waitForClickable({ timeout: 40000 });
    await this.showPropertyAreasBtn.click();
    await (
      await backendConfigurationPropertiesPage.propertyAreasViewCloseBtn()
    ).waitForClickable({ timeout: 40000 });
    await (
      await backendConfigurationPropertiesPage.navigateToPropertyArea(
        indexAreaForClick
      )
    ).click();
  }

  public async closeAreasViewModal() {
    await (
      await backendConfigurationPropertiesPage.propertyAreasViewCloseBtn()
    ).waitForClickable({ timeout: 40000 });
    await (
      await backendConfigurationPropertiesPage.propertyAreasViewCloseBtn()
    ).click();
    await (
      await backendConfigurationPropertiesPage.propertyCreateBtn()
    ).waitForClickable({ timeout: 40000 });
  }
}

export class PropertyCreateUpdate {
  name?: string;
  chrNumber?: string;
  address?: string;
  selectedLanguages?: { languageId?: number; languageName?: string }[];
}
