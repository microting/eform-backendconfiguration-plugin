import loginPage from '../../Page objects/Login.page';
import itemsPlanningPlanningPage, {
  PlanningCreateUpdate,
} from '../../Page objects/ItemsPlanning/ItemsPlanningPlanningPage';
import itemsPlanningModalPage from '../../Page objects/ItemsPlanning/ItemsPlanningModal.page';
import { generateRandmString } from '../../Helpers/helper-functions';
import myEformsPage from '../../Page objects/MyEforms.page';
import foldersPage from '../../Page objects/Folders.page';

const expect = require('chai').expect;
const planningData: PlanningCreateUpdate = {
  name: [generateRandmString(), generateRandmString(), generateRandmString()],
  eFormName: generateRandmString(),
  description: 'Description',
  repeatEvery: '1',
  repeatType: 'Dag',
  repeatUntil: new Date('5/15/2020'),
  folderName: generateRandmString(),
};

describe('Items planning actions - Delete', function () {
  before(function () {
    loginPage.open('/auth');
    loginPage.login();
    if (myEformsPage.rowNum <= 0) {
      myEformsPage.createNewEform(planningData.eFormName); // Create eform
    } else {
      planningData.eFormName = myEformsPage.getFirstMyEformsRowObj().eFormName;
    }
    myEformsPage.Navbar.goToFolderPage();
    if (foldersPage.rowNum <= 0) {
      foldersPage.createNewFolder(planningData.folderName, 'Description'); // Create folder
    } else {
      planningData.folderName = foldersPage.getFolder(1).name;
    }
    itemsPlanningPlanningPage.goToPlanningsPage();
  });
  it('should should create planning', function () {
    itemsPlanningModalPage.createPlanning(planningData);
  });
  it('should not delete existing planning', function () {
    const numRowBeforeDelete = itemsPlanningPlanningPage.rowNum;
    const planningRowObject = itemsPlanningPlanningPage.getPlaningByName(
      planningData.name[0]
    );
    planningRowObject.delete(true);
    expect(numRowBeforeDelete, 'Planning is deleted').eq(
      itemsPlanningPlanningPage.rowNum
    );
  });
  it('should delete existing planning', function () {
    const numRowBeforeDelete = itemsPlanningPlanningPage.rowNum;
    const planningRowObject = itemsPlanningPlanningPage.getPlaningByName(
      planningData.name[0]
    );
    planningRowObject.delete();
    expect(numRowBeforeDelete - 1, 'Planning is not deleted').eq(
      itemsPlanningPlanningPage.rowNum
    );
  });
});
