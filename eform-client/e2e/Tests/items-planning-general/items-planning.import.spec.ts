import loginPage from '../../Page objects/Login.page';
import itemsPlanningPlanningPage from '../../Page objects/ItemsPlanning/ItemsPlanningPlanningPage';
import myEformsPage from '../../Page objects/MyEforms.page';
import itemsPlanningModalPage from '../../Page objects/ItemsPlanning/ItemsPlanningModal.page';
import { planningsImportTestData } from '../../Page objects/ItemsPlanning/PlanningsTestImport.data';

const expect = require('chai').expect;

describe('Items planning - Import', function () {
  before(function () {
    loginPage.open('/auth');
    loginPage.login();
  });
  it('should be imported plannings', function () {
    const localPath = process.cwd();
    myEformsPage.importEformsBtn.click();
    browser.pause(2000);
    // import Eforms
    const filePath = localPath + '/e2e/Assets/Skabelon Døvmark NEW.xlsx';
    const remoteFilePath = browser.uploadFile(filePath);
    $('app-eforms-bulk-import-modal * *').waitForDisplayed({ timeout: 20000 });
    $('#spinner-animation').waitForDisplayed({ timeout: 90000, reverse: true });
    myEformsPage.xlsxImportInput.addValue(remoteFilePath);
    const eformsBeforeImport = myEformsPage.rowNum;
    myEformsPage.newEformBtn.waitForClickable({ timeout: 60000 });
    expect(eformsBeforeImport).not.eq(myEformsPage.rowNum);

    itemsPlanningPlanningPage.goToPlanningsPage();
    const planningsBeforeImport = itemsPlanningPlanningPage.rowNum;
    itemsPlanningPlanningPage.importPlanningsBtn.click();
    // import plannings
    $('app-plannings-bulk-import-modal * *').waitForDisplayed({
      timeout: 20000,
    });
    itemsPlanningModalPage.xlsxImportPlanningsInput.addValue(remoteFilePath);
    itemsPlanningPlanningPage.planningCreateBtn.waitForClickable({
      timeout: 60000,
    });
    expect(planningsBeforeImport, 'plannings not imported').not.eq(
      itemsPlanningPlanningPage.rowNum
    );
  });
  it('should be imported data equal moq data', function () {
    for (let i = 0; i < planningsImportTestData.length; i++) {
      const planning = itemsPlanningPlanningPage.getPlanningByIndex(i + 1);
      const testPlanning = planningsImportTestData[i];
      expect(planning.name, `Planning[${i}] name is incorrect`).eq(
        testPlanning.translatedName
      );
      expect(
        planning.description,
        `Planning[${i}] description is incorrect`
      ).eq(testPlanning.description);
      expect(planning.folderName, `Planning[${i}] folderName is incorrect`).eq(
        testPlanning.folder
      );
      expect(planning.eFormName, `Planning[${i}] eFormName is incorrect`).eq(
        testPlanning.relatedEFormName
      );
      expect(
        planning.repeatEvery,
        `Planning[${i}] repeatEvery is incorrect`
      ).eq(testPlanning.repeatEvery);
      expect(planning.repeatType, `Planning[${i}] repeatType is incorrect`).eq(
        testPlanning.repeatType
      );
      for (let j = 0; j < testPlanning.tags.length; j++) {
        expect(testPlanning.tags[j], `Planning[${i}] tag ${j} is incorrect`).eq(
          testPlanning.tags[j]
        );
      }
    }
  });
});
