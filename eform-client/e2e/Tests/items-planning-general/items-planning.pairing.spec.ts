import loginPage from '../../Page objects/Login.page';
import itemsPlanningPlanningPage, {
  PlanningCreateUpdate,
  PlanningRowObject,
} from '../../Page objects/ItemsPlanning/ItemsPlanningPlanningPage';
import myEformsPage from '../../Page objects/MyEforms.page';
import foldersPage from '../../Page objects/Folders.page';
import { generateRandmString } from '../../Helpers/helper-functions';
import deviceUsersPage, {
  DeviceUsersRowObject,
} from '../../Page objects/DeviceUsers.page';
import itemsPlanningPairingPage from '../../Page objects/ItemsPlanning/ItemsPlanningPairingPage';
import itemsPlanningModalPage from '../../Page objects/ItemsPlanning/ItemsPlanningModal.page';

const expect = require('chai').expect;
let template = generateRandmString();
let folderName = generateRandmString();
let planningRowObjects: PlanningRowObject[];
const deviceUsers = new Array<DeviceUsersRowObject>();
const countDeviceUsers = 4; // constant, how need create user devices for test
const countPlanning = 4; // constant, how need create plannings

describe('Items planning plugin - Pairing', function () {
  before(function () {
    loginPage.open('/auth');
    loginPage.login();

    if (myEformsPage.rowNum <= 0) {
      myEformsPage.createNewEform(template); // Create eform
    } else {
      template = myEformsPage.getFirstMyEformsRowObj().eFormName;
    }

    myEformsPage.Navbar.goToDeviceUsersPage();
    while (deviceUsersPage.rowNum !== countDeviceUsers) {
      // create device users
      deviceUsersPage.createNewDeviceUser(
        generateRandmString(),
        generateRandmString()
      );
    }
    for (let i = 1; i < countDeviceUsers + 1; i++) {
      deviceUsers.push(deviceUsersPage.getDeviceUser(i));
    }

    myEformsPage.Navbar.goToFolderPage();
    if (foldersPage.rowNum <= 0) {
      foldersPage.createNewFolder(folderName, 'Description'); // Create folder
    } else {
      folderName = foldersPage.getFolder(1).name;
    }

    itemsPlanningPlanningPage.goToPlanningsPage();
    while (itemsPlanningPlanningPage.rowNum < countPlanning) {
      const planningData: PlanningCreateUpdate = {
        name: [
          generateRandmString(),
          generateRandmString(),
          generateRandmString(),
        ],
        eFormName: template,
        folderName: folderName,
      };
      itemsPlanningModalPage.createPlanning(planningData);
    }
    planningRowObjects = [
      ...itemsPlanningPlanningPage.getAllPlannings(countPlanning),
    ];

    itemsPlanningPairingPage.goToPairingPage();
  });
  it('should pair one device user which all plannings', function () {
    const pair = true;
    const pairingColObject = itemsPlanningPairingPage.getDeviceUserByIndex(1);
    pairingColObject.pairWhichAllPlannings(pair);
    for (let i = 0; i < pairingColObject.pairCheckboxesForClick.length; i++) {
      expect(pairingColObject.pairCheckboxes[i].getValue()).eq(pair.toString());
    }
  });
  it('should unpair one device user which all plannings', function () {
    const pair = false;
    const pairingColObject = itemsPlanningPairingPage.getDeviceUserByIndex(1);
    pairingColObject.pairWhichAllPlannings(pair, true);
    for (let i = 0; i < pairingColObject.pairCheckboxesForClick.length; i++) {
      expect(pairingColObject.pairCheckboxes[i].getValue()).eq(pair.toString());
    }
  });
  it('should pair one planning which all device user', function () {
    const pair = true;
    const pairingRowObject = itemsPlanningPairingPage.getPlanningByIndex(1);
    pairingRowObject.pairWhichAllDeviceUsers(pair);
    for (let i = 0; i < pairingRowObject.pairCheckboxesForClick.length; i++) {
      expect(pairingRowObject.pairCheckboxes[i].getValue()).eq(pair.toString());
    }
  });
  it('should unpair one planning which all device user', function () {
    const pair = false;
    const pairingRowObject = itemsPlanningPairingPage.getPlanningByIndex(1);
    pairingRowObject.pairWhichAllDeviceUsers(pair, true);
    for (let i = 0; i < pairingRowObject.pairCheckboxesForClick.length; i++) {
      expect(pairingRowObject.pairCheckboxes[i].getValue()).eq(pair.toString());
    }
  });
  it('should pair one planning which one device user', function () {
    const pair = true;
    const indexDeviceForPair = 1;
    const pairingRowObject = itemsPlanningPairingPage.getPlanningByIndex(1);
    pairingRowObject.pairWithOneDeviceUser(pair, indexDeviceForPair);
    expect(pairingRowObject.pairCheckboxes[indexDeviceForPair].getValue()).eq(
      pair.toString()
    );
  });
  it('should unpair one planning which one device user', function () {
    const pair = false;
    const indexDeviceForPair = 1;
    const pairingRowObject = itemsPlanningPairingPage.getPlanningByIndex(1);
    pairingRowObject.pairWithOneDeviceUser(pair, indexDeviceForPair);
    expect(pairingRowObject.pairCheckboxes[indexDeviceForPair].getValue()).eq(
      pair.toString()
    );
  });
  after('delete all created for this test', function () {
    itemsPlanningPlanningPage.goToPlanningsPage();
    itemsPlanningPlanningPage.clearTable();

    myEformsPage.Navbar.goToFolderPage();
    foldersPage.getFolderByName(folderName).delete();

    myEformsPage.Navbar.goToDeviceUsersPage();
    for (let i = 0; i < deviceUsers.length; i++) {
      deviceUsers[i].delete();
    }

    myEformsPage.Navbar.goToMyEForms();
    myEformsPage.getEformsRowObjByNameEForm(template).deleteEForm();
  });
});
