import loginPage from '../../Page objects/Login.page';
import itemsPlanningPlanningPage from '../../Page objects/ItemsPlanning/ItemsPlanningPlanningPage';
import myEformsPage from '../../Page objects/MyEforms.page';
import foldersPage from '../../Page objects/Folders.page';
import { generateRandmString } from '../../Helpers/helper-functions';

const expect = require('chai').expect;
let template = generateRandmString();
let folderName = generateRandmString();
describe('Items planning plannings - Sorting', function () {
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
    itemsPlanningPlanningPage.createDummyPlannings(template, folderName);
  });
  it('should be able to sort by ID', function () {
    browser.pause(1000);
    const planningBefore = $$('#planningId').map((item) => {
      return item.getText();
    });

    // check that sorting is correct in both directions
    for (let i = 0; i < 2; i++) {
      itemsPlanningPlanningPage.clickIdTableHeader();

      const planningAfter = $$('#planningId').map((item) => {
        return item.getText();
      });

      // get current direction of sorting
      const sortIcon = $('#idTableHeader i').getText();
      let sorted;
      if (sortIcon === 'expand_more') {
        sorted = planningBefore.sort().reverse();
      } else if (sortIcon === 'expand_less') {
        sorted = planningBefore.sort();
      } else {
        sorted = planningBefore;
      }
      expect(sorted, 'Sort by ID incorrect').deep.equal(planningAfter);
    }
    $('#spinner-animation').waitForDisplayed({ timeout: 90000, reverse: true });
  });
  it('should be able to sort by Name', function () {
    const planningBefore = $$('#planningName').map((item) => {
      return item.getText();
    });

    // check that sorting is correct in both directions
    for (let i = 0; i < 2; i++) {
      itemsPlanningPlanningPage.clickNameTableHeader();

      const planningAfter = $$('#planningName').map((item) => {
        return item.getText();
      });

      // get current direction of sorting
      const sortIcon = $('#nameTableHeader i').getText();
      let sorted;
      if (sortIcon === 'expand_more') {
        sorted = planningBefore.sort().reverse();
      } else if (sortIcon === 'expand_less') {
        sorted = planningBefore.sort();
      } else {
        sorted = planningBefore;
      }

      $('#spinner-animation').waitForDisplayed({
        timeout: 90000,
        reverse: true,
      });
      expect(sorted, 'Sort by Name incorrect').deep.equal(planningAfter);
    }
    $('#spinner-animation').waitForDisplayed({ timeout: 90000, reverse: true });
  });
  it('should be able to sort by Description', function () {
    const planningBefore = $$('#planningDescription').map((item) => {
      return item.getText();
    });

    // check that sorting is correct in both directions
    for (let i = 0; i < 2; i++) {
      itemsPlanningPlanningPage.clickDescriptionTableHeader();

      const planningAfter = $$('#planningDescription').map((item) => {
        return item.getText();
      });

      // get current direction of sorting
      const sortIcon = $('#descriptionTableHeader i').getText();
      let sorted;
      if (sortIcon === 'expand_more') {
        sorted = planningBefore.sort().reverse();
      } else if (sortIcon === 'expand_less') {
        sorted = planningBefore.sort();
      } else {
        sorted = planningBefore;
      }

      expect(sorted, 'Sort by Description incorrect').deep.equal(planningAfter);
    }
    $('#spinner-animation').waitForDisplayed({ timeout: 90000, reverse: true });
  });
  it('should clear table', function () {
    itemsPlanningPlanningPage.clearTable();
  });
});
