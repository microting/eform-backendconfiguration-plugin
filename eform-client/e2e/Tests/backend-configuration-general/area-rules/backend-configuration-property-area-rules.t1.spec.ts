import loginPage from '../../../Page objects/Login.page';
import backendConfigurationPropertiesPage, {
  PropertyCreateUpdate,
} from '../../../Page objects/BackendConfiguration/BackendConfigurationProperties.page';
import { expect } from 'chai';
import { generateRandmString } from '../../../Helpers/helper-functions';
import backendConfigurationPropertyWorkersPage from '../../../Page objects/BackendConfiguration/BackendConfigurationPropertyWorkers.page';
import backendConfigurationAreaRulesPage, {
  AreaRuleCreateUpdate,
} from '../../../Page objects/BackendConfiguration/BackendConfigurationAreaRules.page';

const property: PropertyCreateUpdate = {
  name: generateRandmString(),
  chrNumber: generateRandmString(),
  address: generateRandmString(),
  selectedLanguages: [{ languageId: 1, languageName: 'Danish' }],
};
const workerForCreate = {
  name: generateRandmString(),
  surname: generateRandmString(),
  language: 'Danish',
  properties: [0],
};
const areaRuleForCreate: AreaRuleCreateUpdate = {
  name: generateRandmString(),
};

describe('Backend Configuration Area Rules Type1', function () {
  before(async () => {
    await loginPage.open('/auth');
    await loginPage.login();
    await backendConfigurationPropertiesPage.goToProperties();
    await backendConfigurationPropertiesPage.createProperty(property);
    await backendConfigurationPropertyWorkersPage.goToPropertyWorkers();
    await backendConfigurationPropertyWorkersPage.create(workerForCreate);
    await backendConfigurationPropertiesPage.goToProperties();
    const lastProperty = await backendConfigurationPropertiesPage.getLastPropertyRowObject();
    await lastProperty.editBindWithAreas([0]); // bind all specific types
    await lastProperty.openAreasViewModal(0); // go to area rule page
  });
  it('should create new area rule type 1', async () => {
    const rowNum = await backendConfigurationAreaRulesPage.rowNum();
    expect(rowNum, 'have some non-default area rules').eq(2);
    await backendConfigurationAreaRulesPage.createAreaRule(areaRuleForCreate);
    expect(rowNum + 1).eq(await backendConfigurationAreaRulesPage.rowNum());
    const areRule = await backendConfigurationAreaRulesPage.getLastAreaRuleRowObject();
    expect(areRule.name).eq(areaRuleForCreate.name);
    expect(areRule.eform).eq('01. Vandforbrug');
    expect(areRule.rulePlanningStatus).eq(false);
  });
  it('should not edit created area rule type 1', async () => {
    const rowNum = await backendConfigurationAreaRulesPage.rowNum();
    const oldAreRule = await backendConfigurationAreaRulesPage.getLastAreaRuleRowObject();
    const areaRuleForUpdate: AreaRuleCreateUpdate = {
      name: generateRandmString(),
      eform: '01. Elforbrug',
    };
    await oldAreRule.edit(areaRuleForUpdate, true);
    expect(rowNum).eq(await backendConfigurationAreaRulesPage.rowNum());
    const areRule = await backendConfigurationAreaRulesPage.getLastAreaRuleRowObject();
    expect(areRule.name).eq(areaRuleForCreate.name);
    expect(areRule.eform).eq('01. Vandforbrug');
    expect(areRule.rulePlanningStatus).eq(false);
  });
  it('should edit created area rule type 1', async () => {
    const rowNum = await backendConfigurationAreaRulesPage.rowNum();
    let areRule = await backendConfigurationAreaRulesPage.getLastAreaRuleRowObject();
    const areaRuleForUpdate: AreaRuleCreateUpdate = {
      name: generateRandmString(),
      eform: '01. Elforbrug',
    };
    await areRule.edit(areaRuleForUpdate);
    expect(rowNum).eq(await backendConfigurationAreaRulesPage.rowNum());
    areRule = await backendConfigurationAreaRulesPage.getLastAreaRuleRowObject();
    expect(areRule.name).eq(areaRuleForUpdate.name);
    expect(areRule.eform).eq(areaRuleForUpdate.eform);
    expect(areRule.rulePlanningStatus).eq(false);
  });
  it('should not delete created area rule type 1', async () => {
    const rowNum = await backendConfigurationAreaRulesPage.rowNum();
    const areRule = await backendConfigurationAreaRulesPage.getLastAreaRuleRowObject();
    await areRule.delete(true);
    expect(rowNum).eq(await backendConfigurationAreaRulesPage.rowNum());
  });
  it('should delete created area rule type 1', async () => {
    const rowNum = await backendConfigurationAreaRulesPage.rowNum();
    const areRule = await backendConfigurationAreaRulesPage.getLastAreaRuleRowObject();
    await areRule.delete();
    expect(rowNum - 1).eq(await backendConfigurationAreaRulesPage.rowNum());
  });
  after(async () => {
    await backendConfigurationAreaRulesPage.clearTable();
    await backendConfigurationPropertiesPage.goToProperties();
    await backendConfigurationPropertiesPage.clearTable();
    await backendConfigurationPropertyWorkersPage.goToPropertyWorkers();
    await backendConfigurationPropertyWorkersPage.clearTable();
  });
});
