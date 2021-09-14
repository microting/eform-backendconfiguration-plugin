import loginPage from '../../Page objects/Login.page';
import itemsPlanningPlanningPage, {
  PlanningCreateUpdate,
  PlanningRowObject,
} from '../../Page objects/ItemsPlanning/ItemsPlanningPlanningPage';
import itemsPlanningModalPage from '../../Page objects/ItemsPlanning/ItemsPlanningModal.page';
import {
  generateRandmString,
  getRandomInt,
} from '../../Helpers/helper-functions';
import myEformsPage from '../../Page objects/MyEforms.page';
import foldersPage from '../../Page objects/Folders.page';
import { format, parse } from 'date-fns';

const expect = require('chai').expect;
let planningData: PlanningCreateUpdate = {
  name: [generateRandmString(), generateRandmString(), generateRandmString()],
  eFormName: generateRandmString(),
  description: generateRandmString(),
  repeatEvery: '1',
  repeatType: 'Dag',
  repeatUntil: new Date(2020, 6, 10),
  folderName: generateRandmString(),
  type: generateRandmString(),
  buildYear: '10',
  locationCode: '12345',
  startFrom: new Date(2020, 7, 9),
  number: '10',
  daysBeforeRedeploymentPushMessageRepeat: false,
  daysBeforeRedeploymentPushMessage: getRandomInt(1, 27),
};
let folderNameForEdit = generateRandmString();
let eFormNameForEdit = generateRandmString();
describe('Items planning actions - Edit', function () {
  before(function () {
    loginPage.open('/auth');
    loginPage.login();
    if (myEformsPage.rowNum >= 2) {
      planningData.eFormName = myEformsPage.getEformRowObj(1).eFormName;
      eFormNameForEdit = myEformsPage.getEformRowObj(2).eFormName;
    } else {
      // Create eforms
      if (myEformsPage.rowNum === 1) {
        planningData.eFormName = myEformsPage.getEformRowObj(1).eFormName;
      } else {
        myEformsPage.createNewEform(planningData.eFormName);
      }
      myEformsPage.createNewEform(eFormNameForEdit);
    }

    myEformsPage.Navbar.goToFolderPage();
    if (foldersPage.rowNum >= 2) {
      planningData.folderName = foldersPage.getFolder(1).name;
      folderNameForEdit = foldersPage.getFolder(2).name;
    } else {
      // Create two folder
      if (foldersPage.rowNum === 1) {
        planningData.folderName = foldersPage.getFolder(1).name;
      } else {
        foldersPage.createNewFolder(planningData.folderName, 'Description');
      }
      foldersPage.createNewFolder(folderNameForEdit, 'Description');
    }
    itemsPlanningPlanningPage.goToPlanningsPage();
  });
  it('should create a new planning', function () {
    itemsPlanningModalPage.createPlanning(planningData);
  });
  it('should change all fields after edit', function () {
    let planningRowObject = itemsPlanningPlanningPage.getPlaningByName(
      planningData.name[0]
    );
    const tempForSwapFolderName = planningData.folderName;
    const tempForSwapEFormFormName = planningData.eFormName;
    planningData = {
      name: [
        generateRandmString(),
        generateRandmString(),
        generateRandmString(),
      ],
      repeatType: 'Uge',
      description: generateRandmString(),
      folderName: folderNameForEdit,
      eFormName: eFormNameForEdit,
      number: '2',
      startFrom: parse('7/3/2020', 'M/d/yyyy', new Date()),
      locationCode: '54321',
      buildYear: '20',
      type: generateRandmString(),
      repeatUntil: parse('10/18/2020', 'M/d/yyyy', new Date()),
      repeatEvery: '2',
      daysBeforeRedeploymentPushMessageRepeat: true,
      daysBeforeRedeploymentPushMessage: getRandomInt(1, 27),
    };
    folderNameForEdit = tempForSwapFolderName;
    eFormNameForEdit = tempForSwapEFormFormName;
    planningRowObject.update(planningData);

    // Check that list is edited successfully in table
    planningRowObject = itemsPlanningPlanningPage.getPlaningByName(
      planningData.name[0]
    );
    planningRowObject.openEdit();
    browser.pause(1000);
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
  after(function () {
    // Delete
    const planningRowObject = itemsPlanningPlanningPage.getPlaningByName(
      planningData.name[0]
    );
    planningRowObject.delete();

    myEformsPage.Navbar.goToFolderPage();
    foldersPage.getFolderByName(planningData.folderName).delete();
    foldersPage.getFolderByName(folderNameForEdit).delete();

    myEformsPage.Navbar.goToMyEForms();
    myEformsPage.getEformsRowObjByNameEForm(eFormNameForEdit).deleteEForm();
    myEformsPage
      .getEformsRowObjByNameEForm(planningData.eFormName)
      .deleteEForm();
  });
});
