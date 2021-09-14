import loginPage from '../../Page objects/Login.page';
import itemsPlanningPlanningPage from '../../Page objects/ItemsPlanning/ItemsPlanningPlanningPage';
import myEformsPage from '../../Page objects/MyEforms.page';
import foldersPage from '../../Page objects/Folders.page';
import { generateRandmString } from '../../Helpers/helper-functions';

const expect = require('chai').expect;
let template = generateRandmString();
let folderName = generateRandmString();
const countPlannings = 5;

describe('Items planning plannings - Multiple delete', function () {
  before(function () {
    loginPage.open('/auth');
    loginPage.login();
    if (myEformsPage.rowNum <= 0) {
      myEformsPage.createNewEform(template); // Create eform
    } else {
      template = myEformsPage.getFirstMyEformsRowObj().eFormName;
    }
    myEformsPage.Navbar.goToFolderPage();
    if (foldersPage.rowNum <= 0) {
      foldersPage.createNewFolder(folderName, 'Description'); // Create folder
    } else {
      folderName = foldersPage.getFolder(1).name;
    }
    itemsPlanningPlanningPage.goToPlanningsPage();
  });
  it('should create dummy plannings', function () {
    itemsPlanningPlanningPage.createDummyPlannings(
      template,
      folderName,
      countPlannings
    );
  });
  it('should not delete because click cancel', function () {
    const countBeforeDelete = itemsPlanningPlanningPage.rowNum;
    itemsPlanningPlanningPage.selectAllPlanningsForDelete();
    itemsPlanningPlanningPage.multipleDelete(true);
    expect(countBeforeDelete, 'plannings has been delete').eq(
      itemsPlanningPlanningPage.rowNum
    );
  });
  it('should multiple delete plannings', function () {
    const countBeforeDelete = itemsPlanningPlanningPage.rowNum;
    itemsPlanningPlanningPage.multipleDelete();
    expect(countBeforeDelete - countPlannings, 'plannings not delete').eq(
      itemsPlanningPlanningPage.rowNum
    );
  });
});
