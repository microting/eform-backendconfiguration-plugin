import { PageWithNavbarPage } from '../PageWithNavbar.page';
import itemsPlanningPlanningPage from './ItemsPlanningPlanningPage';
import { DeviceUsersRowObject } from '../DeviceUsers.page';

export class ItemsPlanningPairingPage extends PageWithNavbarPage {
  constructor() {
    super();
  }

  public get pairingBtn() {
    const ele = $('#items-planning-pn-pairing');
    ele.waitForDisplayed({ timeout: 20000 });
    ele.waitForClickable({ timeout: 20000 });
    return ele;
  }

  public goToPairingPage() {
    itemsPlanningPlanningPage.itemPlanningButton.click();
    this.pairingBtn.click();
    $('#spinner-animation').waitForDisplayed({ timeout: 90000, reverse: true });
    this.savePairingGridBtn.waitForDisplayed();
  }

  public get countPlanningRow(): number {
    browser.pause(500);
    return $$('#planningName').length;
  }

  public get savePairingGridBtn() {
    const ele = $('#savePairingGridBtn');
    ele.waitForDisplayed({ timeout: 20000 });
    // ele.waitForClickable({timeout: 20000});
    return ele;
  }

  public get updatePairingsSaveBtn() {
    const ele = $('#updatePairingsSaveBtn');
    ele.waitForDisplayed({ timeout: 20000 });
    // ele.waitForClickable({timeout: 20000});
    return ele;
  }

  public get updatePairingsSaveCancelBtn() {
    const ele = $('#updatePairingsSaveCancelBtn');
    ele.waitForDisplayed({ timeout: 20000 });
    ele.waitForClickable({ timeout: 20000 });
    return ele;
  }

  public savePairing(clickCancel = false) {
    this.savePairingGridBtn.click();
    if (clickCancel) {
      this.updatePairingsSaveCancelBtn.click();
    } else {
      this.updatePairingsSaveBtn.click();
      $('#spinner-animation').waitForDisplayed({
        timeout: 90000,
        reverse: true,
      });
    }
    this.savePairingGridBtn.waitForDisplayed();
  }

  public get countDeviceUserCol(): number {
    browser.pause(500);
    let i = 0;
    while ($(`#deviceUserTableHeader${i}`).isExisting()) {
      i++;
    }
    return i > 0 ? i + 1 : 0;
  }

  public planningRowByPlanningName(planningName: string): PairingRowObject {
    for (let i = 1; i < this.countPlanningRow + 1; i++) {
      const element = new PairingRowObject(i);
      if (element.planningName === planningName) {
        return element;
      }
    }
    return null;
  }

  getDeviceUserByIndex(index: number): PairingColObject {
    if (index > 0 && index <= this.countDeviceUserCol) {
      return new PairingColObject(index);
    }
    return null;
  }

  getPlanningByIndex(index: number): PairingRowObject {
    if (index > 0 && index <= this.countPlanningRow) {
      return new PairingRowObject(index);
    }
    return null;
  }

  public indexColDeviceUserInTableByName(deviceUserName: string): number {
    for (let i = 0; i < this.countDeviceUserCol; i++) {
      const deviceUser = this.getDeviceUserByIndex(i);
      if (deviceUser.deviceUserName === deviceUserName) {
        return i;
      }
    }
    return -1;
  }
}

const itemsPlanningPairingPage = new ItemsPlanningPairingPage();
export default itemsPlanningPairingPage;

export class PairingRowObject {
  constructor(rowNumber) {
    this.row = $$('tr')[rowNumber];
    if (this.row.isExisting()) {
      this.planningName = this.row.$('#planningName').getText();
      this.pairRow = this.row.$(`#planningRowCheckbox${rowNumber - 1}`);
      this.pairRowForClick = this.pairRow.$('..');
      this.pairCheckboxes = [];
      for (
        let i = 0;
        i < itemsPlanningPairingPage.countDeviceUserCol - 1;
        i++
      ) {
        this.pairCheckboxes.push(
          $(`#deviceUserCheckbox${i}_planning${rowNumber - 1}`)
        );
      }
      this.pairCheckboxesForClick = [];
      for (let i = 0; i < this.pairCheckboxes.length; i++) {
        this.pairCheckboxesForClick.push(this.pairCheckboxes[i].$('..'));
      }
    } else {
      return null;
    }
  }

  public planningName: string;
  public pairRow: WebdriverIO.Element;
  public pairRowForClick: WebdriverIO.Element;
  public pairCheckboxes: WebdriverIO.Element[];
  public pairCheckboxesForClick: WebdriverIO.Element[];
  public row: WebdriverIO.Element;

  public pairWhichAllDeviceUsers(
    pair: boolean,
    clickOnPairRow = false,
    clickCancel = false
  ) {
    if (clickOnPairRow) {
      this.pairRowForClick.click();
      if (this.pairRow.getValue() !== pair.toString()) {
        this.pairRowForClick.click();
      }
    } else {
      for (let i = 0; i < this.pairCheckboxesForClick.length; i++) {
        if (this.pairCheckboxes[i].getValue() !== pair.toString()) {
          this.pairCheckboxesForClick[i].click();
        }
      }
    }
    itemsPlanningPairingPage.savePairing(clickCancel);
  }

  public pairWithOneDeviceUser(
    pair: boolean,
    indexDeviceForPair: number,
    clickCancel = false
  ) {
    if (
      this.pairCheckboxes[indexDeviceForPair].getValue() !== pair.toString()
    ) {
      this.pairCheckboxesForClick[indexDeviceForPair].click();
    }
    itemsPlanningPairingPage.savePairing(clickCancel);
  }

  public isPair(deviceUser: DeviceUsersRowObject): boolean {
    const index = itemsPlanningPairingPage.indexColDeviceUserInTableByName(
      `${deviceUser.firstName} ${deviceUser.lastName}`
    );
    return Boolean(this.pairCheckboxes[index - 1].getValue()) as boolean;
  }
}

export class PairingColObject {
  constructor(rowNumber) {
    const ele = $(`#deviceUserTableHeader${rowNumber - 1}`);
    ele.waitForDisplayed({ timeout: 20000 });
    if (ele.isExisting()) {
      this.deviceUserName = ele.getText();
      this.pairCol = $(`#deviceUserColumnCheckbox${rowNumber - 1}`);
      this.pairColForClick = this.pairCol.$('..');
      this.pairCheckboxesForClick = [];
      this.pairCheckboxes = [];
      for (let i = 0; i < itemsPlanningPairingPage.countPlanningRow; i++) {
        this.pairCheckboxes.push(
          $(`#deviceUserCheckbox${rowNumber - 1}_planning${i}`)
        );
      }
      for (let i = 0; i < this.pairCheckboxes.length; i++) {
        this.pairCheckboxesForClick.push(this.pairCheckboxes[i].$('..'));
      }
    }
  }

  public deviceUserName: string;
  public pairCol: WebdriverIO.Element;
  public pairColForClick: WebdriverIO.Element;
  public pairCheckboxesForClick: WebdriverIO.Element[];
  public pairCheckboxes: WebdriverIO.Element[];

  public pairWhichAllPlannings(
    pair: boolean,
    clickOnPairRow = false,
    clickCancel = false
  ) {
    if (clickOnPairRow) {
      this.pairColForClick.click();
      if (this.pairCol.getValue() !== pair.toString()) {
        this.pairColForClick.click();
      }
    } else {
      for (let i = 0; i < this.pairCheckboxesForClick.length; i++) {
        if (this.pairCheckboxes[i].getValue() !== pair.toString()) {
          this.pairCheckboxesForClick[i].click();
        }
      }
    }
    itemsPlanningPairingPage.savePairing(clickCancel);
  }
}
