import loginPage from '../../../Page objects/Login.page';
import backendConfigurationPropertiesPage, {
  PropertyCreateUpdate,
} from '../../../Page objects/BackendConfiguration/BackendConfigurationProperties.page';
import { expect } from 'chai';
import { generateRandmString } from '../../../Helpers/helper-functions';
import backendConfigurationPropertyWorkersPage from '../../../Page objects/BackendConfiguration/BackendConfigurationPropertyWorkers.page';
import backendConfigurationAreaRulesPage, {
  AreaRulePlanningCreateUpdate,
} from '../../../Page objects/BackendConfiguration/BackendConfigurationAreaRules.page';
import { format } from 'date-fns';
import itemsPlanningPlanningPage from '../../../Page objects/ItemsPlanning/ItemsPlanningPlanningPage';
import applicationSettingsPage from '../../../Page objects/ApplicationSettings.page';

const property: PropertyCreateUpdate = {
  name: generateRandmString(),
  chrNumber: generateRandmString(),
  address: generateRandmString(),
  cvrNumber: '1111111',
  // selectedLanguages: [{ languageId: 1, languageName: 'Dansk' }],
};
const workerForCreate = {
  name: generateRandmString(),
  surname: generateRandmString(),
  language: 'Dansk',
  properties: [0],
};

describe('Backend Configuration Area Rules Planning Type1', function () {
  beforeEach(async () => {
    await loginPage.open('/auth');
    await loginPage.login();
    await backendConfigurationPropertiesPage.goToProperties();
    await backendConfigurationPropertiesPage.createProperty(property);
    await backendConfigurationPropertyWorkersPage.goToPropertyWorkers();
    await backendConfigurationPropertyWorkersPage.create(workerForCreate);
    await backendConfigurationPropertiesPage.goToProperties();
    const lastProperty = await backendConfigurationPropertiesPage.getLastPropertyRowObject();
    await lastProperty.editBindWithAreas([1]); // bind specific type1
    await lastProperty.openAreasViewModal(0); // go to area rule page
  });
  it('should create new planning from default area rule at 0 days', async () => {
    const rowNum = await backendConfigurationAreaRulesPage.rowNum();
    expect(rowNum, 'have some non-default area rules').eq(8);
    const areaRule = await backendConfigurationAreaRulesPage.getFirstAreaRuleRowObject();
    const areaRulePlanning: AreaRulePlanningCreateUpdate = {
      //   startDate: format(new Date(), 'yyyy/MM/dd'),
      workers: [{ workerNumber: 0 }],
      enableCompliance: false,
      repeatEvery: 'Hver',
      repeatType: 'Dag',
    };
    await areaRule.createUpdatePlanning(areaRulePlanning);
    // areaRulePlanning.startDate = format(
    //   sub(new Date(), { days: 1 }),
    //   'yyyy/MM/dd'
    // ); // fix test
    const areaRulePlanningCreated = await areaRule.readPlanning();
    // expect(areaRulePlanningCreated.startDate).eq(areaRulePlanning.startDate);
    expect(areaRulePlanningCreated.workers[0].name).eq(
      `${workerForCreate.name} ${workerForCreate.surname}`
    );
    // expect(
    //   await (await $(`#mat-checkbox-0`)).getValue(),
    //   `User ${areaRulePlanningCreated.workers[0]} not paired`
    // ).eq('true');
    expect(areaRulePlanningCreated.workers[0].checked).eq(true);
    expect(areaRulePlanningCreated.workers[0].status).eq('Klar til server');
    expect(areaRulePlanningCreated.enableCompliance).eq(areaRulePlanning.enableCompliance);
    await itemsPlanningPlanningPage.goToPlanningsPage();
    expect(
      await itemsPlanningPlanningPage.rowNum(),
      'items planning not create or create not correct'
    ).eq(1);
    const itemPlanning = await itemsPlanningPlanningPage.getLastPlanningRowObject();
    expect(itemPlanning.eFormName).eq('1.1 Aflæsning vand');
    expect(itemPlanning.name).eq(areaRule.name);
    expect(itemPlanning.folderName).eq(
      `${property.name} - 01. Logbøger Miljøledelse`
    );
    expect(itemPlanning.repeatEvery).eq(0);
    expect(itemPlanning.repeatType).eq('Dag');

    const today = new Date();
    const todayDate = format(today, 'dd.MM.y');

    expect(itemPlanning.nextExecution.split(' ')[0]).eq('--');
    const lastExecution = itemPlanning.lastExecution.split(' ')[0];
    expect(lastExecution).eq(todayDate);

    const workers = await itemPlanning.readPairing();
    expect([
      {
        workerName: `${workerForCreate.name} ${workerForCreate.surname}`,
        workerValue: true,
      },
    ]).deep.eq(workers);
    // browser.back();
    // await areaRule.createUpdatePlanning({status: false});
  });
  it('should create new planning from default area rule at 2 days', async () => {
    const rowNum = await backendConfigurationAreaRulesPage.rowNum();
    expect(rowNum, 'have some non-default area rules').eq(8);
    const areaRule = await backendConfigurationAreaRulesPage.getFirstAreaRuleRowObject();
    const areaRulePlanning: AreaRulePlanningCreateUpdate = {
    //   startDate: format(new Date(), 'yyyy/MM/dd'),
      workers: [{ workerNumber: 0 }],
      enableCompliance: true,
      repeatEvery: '2',
      repeatType: 'Dag',
    };
    await areaRule.createUpdatePlanning(areaRulePlanning);
    // areaRulePlanning.startDate = format(
    //   sub(new Date(), { days: 1 }),
    //   'yyyy/MM/dd'
    // ); // fix test
    const areaRulePlanningCreated = await areaRule.readPlanning();
    // expect(areaRulePlanningCreated.startDate).eq(areaRulePlanning.startDate);
    expect(areaRulePlanningCreated.workers[0].name).eq(
      `${workerForCreate.name} ${workerForCreate.surname}`
    );
    // expect(
    //   await (await $(`#mat-checkbox-0`)).getValue(),
    //   `User ${areaRulePlanningCreated.workers[0]} not paired`
    // ).eq('true');
    expect(areaRulePlanningCreated.workers[0].checked).eq(true);
    expect(areaRulePlanningCreated.workers[0].status).eq('Klar til server');
    expect(areaRulePlanningCreated.enableCompliance).eq(areaRulePlanning.enableCompliance);
    await itemsPlanningPlanningPage.goToPlanningsPage();
    expect(
      await itemsPlanningPlanningPage.rowNum(),
      'items planning not create or create not correct'
    ).eq(1);
    const itemPlanning = await itemsPlanningPlanningPage.getLastPlanningRowObject();
    expect(itemPlanning.eFormName).eq('1.1 Aflæsning vand');
    expect(itemPlanning.name).eq(areaRule.name);
    expect(itemPlanning.folderName).eq(
      `${property.name} - 01. Logbøger Miljøledelse`
    );
    expect(itemPlanning.repeatEvery).eq(2);
    expect(itemPlanning.repeatType).eq('Dag');

    // compare itemPlanning.lastExecution with today's date
    const today = new Date();
    const todayDate = format(today, 'dd.MM.y');
    const now = new Date();
    const diff = now.getTime() - new Date(now.getFullYear(), 0, 1).getTime();
    const multiplier = Math.floor(diff / (2 * 24 * 60 * 60 * 1000));
    const startOfThisYear = new Date(now.getFullYear(), 0, 1);

    let nextExecutionTime = new Date(startOfThisYear.getTime() + multiplier * 2 * 24 * 60 * 60 * 1000);
    if (nextExecutionTime < now) {
      nextExecutionTime = new Date(nextExecutionTime.getTime() + 2 * 24 * 60 * 60 * 1000);
    }
    expect(itemPlanning.nextExecution.split(' ')[0]).eq(format(nextExecutionTime, 'dd.MM.y'));
    const lastExecution = itemPlanning.lastExecution.split(' ')[0];
    expect(lastExecution).eq(todayDate);

    const workers = await itemPlanning.readPairing();
    expect([
      {
        workerName: `${workerForCreate.name} ${workerForCreate.surname}`,
        workerValue: true,
      },
    ]).deep.eq(workers);
    // browser.back();
    // await areaRule.createUpdatePlanning({status: false});
  });
  it('should create new planning from default area rule at 3 days', async () => {
    const rowNum = await backendConfigurationAreaRulesPage.rowNum();
    expect(rowNum, 'have some non-default area rules').eq(8);
    const areaRule = await backendConfigurationAreaRulesPage.getFirstAreaRuleRowObject();
    const areaRulePlanning: AreaRulePlanningCreateUpdate = {
      //   startDate: format(new Date(), 'yyyy/MM/dd'),
      workers: [{ workerNumber: 0 }],
      enableCompliance: true,
      repeatEvery: '3',
      repeatType: 'Dag',
    };
    await areaRule.createUpdatePlanning(areaRulePlanning);
    // areaRulePlanning.startDate = format(
    //   sub(new Date(), { days: 1 }),
    //   'yyyy/MM/dd'
    // ); // fix test
    const areaRulePlanningCreated = await areaRule.readPlanning();
    // expect(areaRulePlanningCreated.startDate).eq(areaRulePlanning.startDate);
    expect(areaRulePlanningCreated.workers[0].name).eq(
      `${workerForCreate.name} ${workerForCreate.surname}`
    );
    // expect(
    //   await (await $(`#mat-checkbox-0`)).getValue(),
    //   `User ${areaRulePlanningCreated.workers[0]} not paired`
    // ).eq('true');
    expect(areaRulePlanningCreated.workers[0].checked).eq(true);
    expect(areaRulePlanningCreated.workers[0].status).eq('Klar til server');
    expect(areaRulePlanningCreated.enableCompliance).eq(areaRulePlanning.enableCompliance);
    await itemsPlanningPlanningPage.goToPlanningsPage();
    expect(
      await itemsPlanningPlanningPage.rowNum(),
      'items planning not create or create not correct'
    ).eq(1);
    const itemPlanning = await itemsPlanningPlanningPage.getLastPlanningRowObject();
    expect(itemPlanning.eFormName).eq('1.1 Aflæsning vand');
    expect(itemPlanning.name).eq(areaRule.name);
    expect(itemPlanning.folderName).eq(
      `${property.name} - 01. Logbøger Miljøledelse`
    );
    expect(itemPlanning.repeatEvery).eq(3);
    expect(itemPlanning.repeatType).eq('Dag');

    // compare itemPlanning.lastExecution with today's date
    const today = new Date();
    const todayDate = format(today, 'dd.MM.y');
    const now = new Date();
    const diff = now.getTime() - new Date(now.getFullYear(), 0, 1).getTime();
    const multiplier = Math.floor(diff / (3 * 24 * 60 * 60 * 1000));
    const startOfThisYear = new Date(now.getFullYear(), 0, 1);

    let nextExecutionTime = new Date(startOfThisYear.getTime() + multiplier * 3 * 24 * 60 * 60 * 1000);
    if (nextExecutionTime < now) {
      nextExecutionTime = new Date(nextExecutionTime.getTime() + 3 * 24 * 60 * 60 * 1000);
    }
    expect(itemPlanning.nextExecution.split(' ')[0]).eq(format(nextExecutionTime, 'dd.MM.y'));
    const lastExecution = itemPlanning.lastExecution.split(' ')[0];
    expect(lastExecution).eq(todayDate);

    const workers = await itemPlanning.readPairing();
    expect([
      {
        workerName: `${workerForCreate.name} ${workerForCreate.surname}`,
        workerValue: true,
      },
    ]).deep.eq(workers);
    // browser.back();
    // await areaRule.createUpdatePlanning({status: false});
  });
  it('should create new planning from default area rule at 6 days', async () => {
    const rowNum = await backendConfigurationAreaRulesPage.rowNum();
    expect(rowNum, 'have some non-default area rules').eq(8);
    const areaRule = await backendConfigurationAreaRulesPage.getFirstAreaRuleRowObject();
    const areaRulePlanning: AreaRulePlanningCreateUpdate = {
      //   startDate: format(new Date(), 'yyyy/MM/dd'),
      workers: [{ workerNumber: 0 }],
      enableCompliance: true,
      repeatEvery: '6',
      repeatType: 'Dag',
    };
    await areaRule.createUpdatePlanning(areaRulePlanning);
    // areaRulePlanning.startDate = format(
    //   sub(new Date(), { days: 1 }),
    //   'yyyy/MM/dd'
    // ); // fix test
    const areaRulePlanningCreated = await areaRule.readPlanning();
    // expect(areaRulePlanningCreated.startDate).eq(areaRulePlanning.startDate);
    expect(areaRulePlanningCreated.workers[0].name).eq(
      `${workerForCreate.name} ${workerForCreate.surname}`
    );
    // expect(
    //   await (await $(`#mat-checkbox-0`)).getValue(),
    //   `User ${areaRulePlanningCreated.workers[0]} not paired`
    // ).eq('true');
    expect(areaRulePlanningCreated.workers[0].checked).eq(true);
    expect(areaRulePlanningCreated.workers[0].status).eq('Klar til server');
    expect(areaRulePlanningCreated.enableCompliance).eq(areaRulePlanning.enableCompliance);
    await itemsPlanningPlanningPage.goToPlanningsPage();
    expect(
      await itemsPlanningPlanningPage.rowNum(),
      'items planning not create or create not correct'
    ).eq(1);
    const itemPlanning = await itemsPlanningPlanningPage.getLastPlanningRowObject();
    expect(itemPlanning.eFormName).eq('1.1 Aflæsning vand');
    expect(itemPlanning.name).eq(areaRule.name);
    expect(itemPlanning.folderName).eq(
      `${property.name} - 01. Logbøger Miljøledelse`
    );
    expect(itemPlanning.repeatEvery).eq(6);
    expect(itemPlanning.repeatType).eq('Dag');

    // compare itemPlanning.lastExecution with today's date
    const today = new Date();
    const todayDate = format(today, 'dd.MM.y');
    const now = new Date();
    const diff = now.getTime() - new Date(now.getFullYear(), 0, 1).getTime();
    const multiplier = Math.floor(diff / (6 * 24 * 60 * 60 * 1000));
    const startOfThisYear = new Date(now.getFullYear(), 0, 1);

    let nextExecutionTime = new Date(startOfThisYear.getTime() + multiplier * 6 * 24 * 60 * 60 * 1000);
    if (nextExecutionTime < now) {
      nextExecutionTime = new Date(nextExecutionTime.getTime() + 6 * 24 * 60 * 60 * 1000);
    }
    expect(itemPlanning.nextExecution.split(' ')[0]).eq(format(nextExecutionTime, 'dd.MM.y'));
    const lastExecution = itemPlanning.lastExecution.split(' ')[0];
    expect(lastExecution).eq(todayDate);

    const workers = await itemPlanning.readPairing();
    expect([
      {
        workerName: `${workerForCreate.name} ${workerForCreate.surname}`,
        workerValue: true,
      },
    ]).deep.eq(workers);
    // browser.back();
    // await areaRule.createUpdatePlanning({status: false});
  });
  it('should create new planning from default area rule at 12 days', async () => {
    const rowNum = await backendConfigurationAreaRulesPage.rowNum();
    expect(rowNum, 'have some non-default area rules').eq(8);
    const areaRule = await backendConfigurationAreaRulesPage.getFirstAreaRuleRowObject();
    const areaRulePlanning: AreaRulePlanningCreateUpdate = {
      //   startDate: format(new Date(), 'yyyy/MM/dd'),
      workers: [{ workerNumber: 0 }],
      enableCompliance: true,
      repeatEvery: '12',
      repeatType: 'Dag',
    };
    await areaRule.createUpdatePlanning(areaRulePlanning);
    // areaRulePlanning.startDate = format(
    //   sub(new Date(), { days: 1 }),
    //   'yyyy/MM/dd'
    // ); // fix test
    const areaRulePlanningCreated = await areaRule.readPlanning();
    // expect(areaRulePlanningCreated.startDate).eq(areaRulePlanning.startDate);
    expect(areaRulePlanningCreated.workers[0].name).eq(
      `${workerForCreate.name} ${workerForCreate.surname}`
    );
    // expect(
    //   await (await $(`#mat-checkbox-0`)).getValue(),
    //   `User ${areaRulePlanningCreated.workers[0]} not paired`
    // ).eq('true');
    expect(areaRulePlanningCreated.workers[0].checked).eq(true);
    expect(areaRulePlanningCreated.workers[0].status).eq('Klar til server');
    expect(areaRulePlanningCreated.enableCompliance).eq(areaRulePlanning.enableCompliance);
    await itemsPlanningPlanningPage.goToPlanningsPage();
    expect(
      await itemsPlanningPlanningPage.rowNum(),
      'items planning not create or create not correct'
    ).eq(1);
    const itemPlanning = await itemsPlanningPlanningPage.getLastPlanningRowObject();
    expect(itemPlanning.eFormName).eq('1.1 Aflæsning vand');
    expect(itemPlanning.name).eq(areaRule.name);
    expect(itemPlanning.folderName).eq(
      `${property.name} - 01. Logbøger Miljøledelse`
    );
    expect(itemPlanning.repeatEvery).eq(12);
    expect(itemPlanning.repeatType).eq('Dag');

    // compare itemPlanning.lastExecution with today's date
    const today = new Date();
    const todayDate = format(today, 'dd.MM.y');
    const now = new Date();
    const diff = now.getTime() - new Date(now.getFullYear(), 0, 1).getTime();
    const multiplier = Math.floor(diff / (12 * 24 * 60 * 60 * 1000));
    const startOfThisYear = new Date(now.getFullYear(), 0, 1);

    let nextExecutionTime = new Date(startOfThisYear.getTime() + multiplier * 12 * 24 * 60 * 60 * 1000);
    if (nextExecutionTime < now) {
      nextExecutionTime = new Date(nextExecutionTime.getTime() + 12 * 24 * 60 * 60 * 1000);
    }
    expect(itemPlanning.nextExecution.split(' ')[0]).eq(format(nextExecutionTime, 'dd.MM.y'));
    const lastExecution = itemPlanning.lastExecution.split(' ')[0];
    expect(lastExecution).eq(todayDate);

    const workers = await itemPlanning.readPairing();
    expect([
      {
        workerName: `${workerForCreate.name} ${workerForCreate.surname}`,
        workerValue: true,
      },
    ]).deep.eq(workers);
    // browser.back();
    // await areaRule.createUpdatePlanning({status: false});
  });it('should create new planning from default area rule at 24 days', async () => {
    const rowNum = await backendConfigurationAreaRulesPage.rowNum();
    expect(rowNum, 'have some non-default area rules').eq(8);
    const areaRule = await backendConfigurationAreaRulesPage.getFirstAreaRuleRowObject();
    const areaRulePlanning: AreaRulePlanningCreateUpdate = {
      //   startDate: format(new Date(), 'yyyy/MM/dd'),
      workers: [{ workerNumber: 0 }],
      enableCompliance: true,
      repeatEvery: '24',
      repeatType: 'Dag',
    };
    await areaRule.createUpdatePlanning(areaRulePlanning);
    // areaRulePlanning.startDate = format(
    //   sub(new Date(), { days: 1 }),
    //   'yyyy/MM/dd'
    // ); // fix test
    const areaRulePlanningCreated = await areaRule.readPlanning();
    // expect(areaRulePlanningCreated.startDate).eq(areaRulePlanning.startDate);
    expect(areaRulePlanningCreated.workers[0].name).eq(
      `${workerForCreate.name} ${workerForCreate.surname}`
    );
    // expect(
    //   await (await $(`#mat-checkbox-0`)).getValue(),
    //   `User ${areaRulePlanningCreated.workers[0]} not paired`
    // ).eq('true');
    expect(areaRulePlanningCreated.workers[0].checked).eq(true);
    expect(areaRulePlanningCreated.workers[0].status).eq('Klar til server');
    expect(areaRulePlanningCreated.enableCompliance).eq(areaRulePlanning.enableCompliance);
    await itemsPlanningPlanningPage.goToPlanningsPage();
    expect(
      await itemsPlanningPlanningPage.rowNum(),
      'items planning not create or create not correct'
    ).eq(1);
    const itemPlanning = await itemsPlanningPlanningPage.getLastPlanningRowObject();
    expect(itemPlanning.eFormName).eq('1.1 Aflæsning vand');
    expect(itemPlanning.name).eq(areaRule.name);
    expect(itemPlanning.folderName).eq(
      `${property.name} - 01. Logbøger Miljøledelse`
    );
    expect(itemPlanning.repeatEvery).eq(24);
    expect(itemPlanning.repeatType).eq('Dag');

    // compare itemPlanning.lastExecution with today's date
    const today = new Date();
    const todayDate = format(today, 'dd.MM.y');
    const now = new Date();
    const diff = now.getTime() - new Date(now.getFullYear(), 0, 1).getTime();
    const multiplier = Math.floor(diff / (24 * 24 * 60 * 60 * 1000));
    const startOfThisYear = new Date(now.getFullYear(), 0, 1);

    let nextExecutionTime = new Date(startOfThisYear.getTime() + multiplier * 24 * 24 * 60 * 60 * 1000);
    if (nextExecutionTime < now) {
      nextExecutionTime = new Date(nextExecutionTime.getTime() + 24 * 24 * 60 * 60 * 1000);
    }
    expect(itemPlanning.nextExecution.split(' ')[0]).eq(format(nextExecutionTime, 'dd.MM.y'));
    const lastExecution = itemPlanning.lastExecution.split(' ')[0];
    expect(lastExecution).eq(todayDate);

    const workers = await itemPlanning.readPairing();
    expect([
      {
        workerName: `${workerForCreate.name} ${workerForCreate.surname}`,
        workerValue: true,
      },
    ]).deep.eq(workers);
    // browser.back();
    // await areaRule.createUpdatePlanning({status: false});
  });
  afterEach(async () => {
    await backendConfigurationPropertiesPage.goToProperties();
    await backendConfigurationPropertiesPage.clearTable();
    await backendConfigurationPropertyWorkersPage.goToPropertyWorkers();
    await backendConfigurationPropertyWorkersPage.clearTable();
    await applicationSettingsPage.Navbar.logout();
  });
});