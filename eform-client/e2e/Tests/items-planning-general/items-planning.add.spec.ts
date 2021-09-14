import loginPage from '../../Page objects/Login.page';
import itemsPlanningPlanningPage, {
  PlanningCreateUpdate,
  PlanningRowObject,
} from '../../Page objects/ItemsPlanning/ItemsPlanningPlanningPage';
import itemsPlanningModalPage from '../../Page objects/ItemsPlanning/ItemsPlanningModal.page';
import myEformsPage from '../../Page objects/MyEforms.page';
import foldersPage from '../../Page objects/Folders.page';
import {
  generateRandmString,
  getRandomInt,
} from '../../Helpers/helper-functions';
import { format, parse } from 'date-fns';

const expect = require('chai').expect;

const planningData: PlanningCreateUpdate = {
  name: [generateRandmString(), generateRandmString(), generateRandmString()],
  eFormName: generateRandmString(),
  folderName: generateRandmString(),
  description: generateRandmString(),
  repeatEvery: '1',
  repeatType: 'Dag',
  startFrom: new Date(2020, 7, 9),
  repeatUntil: new Date(2020, 6, 10),
  type: generateRandmString(),
  locationCode: '12345',
  buildYear: '10',
  number: '10',
  daysBeforeRedeploymentPushMessage: getRandomInt(1, 27),
  daysBeforeRedeploymentPushMessageRepeat: true,
};
describe('Items planning - Add', function () {
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
  it('should create planning with all fields', function () {
    const rowNumBeforeCreatePlanning = itemsPlanningPlanningPage.rowNum;
    itemsPlanningModalPage.createPlanning(planningData);
    expect(rowNumBeforeCreatePlanning + 1, 'Planning not created').eq(
      itemsPlanningPlanningPage.rowNum
    );
  });
  it('check all fields planning', function () {
    // Check that planning is created in table
    const planningRowObject = itemsPlanningPlanningPage.getPlaningByName(
      planningData.name[0]
    );
    expect(planningRowObject.name, 'Saved name in table is incorrect').equal(
      planningData.name[0]
    );
    expect(
      planningRowObject.eFormName,
      'Saved template in table is incorrect'
    ).equal(planningData.eFormName);
    expect(
      planningRowObject.description,
      'Saved description in table is incorrect'
    ).equal(planningData.description);
    expect(
      planningRowObject.repeatEvery.toString(),
      'Saved repeat every in table is incorrect'
    ).equal(planningData.repeatEvery);
    const repeatUntil = new Date(planningData.repeatUntil);
    expect(
      planningRowObject.repeatUntil.getDate(),
      'Saved repeat Until in table is incorrect'
    ).equal(repeatUntil.getDate());
    expect(
      planningRowObject.repeatType,
      'Saved repeat type in table is incorrect'
    ).equal(planningData.repeatType);
    // Check that all planning fields are saved
    planningRowObject.openEdit();
    for (let i = 0; i < planningData.name.length; i++) {
      expect(
        itemsPlanningModalPage.editPlanningItemName(i).getValue(),
        'Name save is incorrect'
      ).eq(planningData.name[i]);
    }
    expect(
      itemsPlanningModalPage.editPlanningDescription.getValue(),
      'Description save is incorrect'
    ).eq(planningData.description);
    expect(
      itemsPlanningModalPage.editPlanningSelector.$('.ng-value').getText(),
      'Saved template is incorrect'
    ).eq(planningData.eFormName);
    expect(
      itemsPlanningModalPage.editRepeatEvery.getValue(),
      'Saved repeat every is incorrect'
    ).eq(planningData.repeatEvery);
    expect(
      format(
        parse(
          itemsPlanningModalPage.editRepeatUntil.getValue(),
          'M/d/yyyy',
          new Date()
        ),
        'M/d/yyyy'
      ),
      'Saved repeat until is incorrect'
    ).eq(format(planningData.repeatUntil, 'M/d/yyyy'));
    expect(
      itemsPlanningModalPage.editRepeatType.$('.ng-value-label').getText(),
      'Saved repeat type is incorrect'
    ).eq(planningData.repeatType);
    expect(
      itemsPlanningModalPage.editItemType.getValue(),
      'Saved type is incorrect'
    ).eq(planningData.type);
    expect(
      itemsPlanningModalPage.editItemBuildYear.getValue(),
      'Saved build year is incorrect'
    ).eq(planningData.buildYear);
    expect(
      itemsPlanningModalPage.editFolderName
        .$('#editFolderSelectorInput')
        .getValue(),
      'Saved folder name is incorrect'
    ).eq(planningData.folderName);
    expect(
      itemsPlanningModalPage.editItemLocationCode.getValue(),
      'Saved location code is incorrect'
    ).eq(planningData.locationCode);
    expect(
      format(
        parse(
          itemsPlanningModalPage.editStartFrom.getValue(),
          'M/d/yyyy',
          new Date()
        ),
        'M/d/yyyy'
      ),
      'Saved start from is incorrect'
    ).eq(format(planningData.startFrom, 'M/d/yyyy'));
    expect(
      itemsPlanningModalPage.daysBeforeRedeploymentPushMessageRepeatEdit
        .$('.ng-value-label')
        .getText(),
      'Saved daysBeforeRedeploymentPushMessageRepeat code is incorrect'
    ).eq(
      planningData.daysBeforeRedeploymentPushMessageRepeat
        ? 'Aktiveret'
        : 'Deaktiveret'
    );
    expect(
      +itemsPlanningModalPage.editDaysBeforeRedeploymentPushMessage
        .$('.ng-value-label')
        .getText(),
      'Saved editDaysBeforeRedeploymentPushMessage code is incorrect'
    ).eq(planningData.daysBeforeRedeploymentPushMessage);
    PlanningRowObject.closeEdit(true);
  });
  after('delete all created in this test', function () {
    // Delete
    const planningRowObject = itemsPlanningPlanningPage.getPlaningByName(
      planningData.name[0]
    );
    planningRowObject.delete();

    myEformsPage.Navbar.goToFolderPage();
    foldersPage.getFolderByName(planningData.folderName).delete();

    myEformsPage.Navbar.goToMyEForms();
    myEformsPage
      .getEformsRowObjByNameEForm(planningData.eFormName)
      .deleteEForm();
  });
});
