import loginPage from '../../../Login.page';
import backendConfigurationReportsPage, {ReportFilters} from '../BackendConfigurationReports.page';
import path from 'path';
import {read} from 'xlsx';
import * as mammoth from 'mammoth';

const filters: ReportFilters = {
  dateRange: {
    yearFrom: 2021,
    monthFrom: 11,
    dayFrom: 1,
    yearTo: 2022,
    monthTo: 4,
    dayTo: 1,
  }
};

const fileName: string = '2021-11-01T00_00_00.000Z_2022-04-01T00_00_00.000Z_report';

describe('Reports', () => {
  before(() => {
    cy.visit('http://localhost:4200');
    loginPage.login();
    backendConfigurationReportsPage.goToReports();
  });
  it('should download correct files', () => {
    backendConfigurationReportsPage.fillFilters(filters);
    const downloadsFolder = Cypress.config('downloadsFolder');
    const fixturesFolder = Cypress.config('fixturesFolder');

    cy.log('**GENERATE WORD REPORT**')
    backendConfigurationReportsPage.generateWordBtn().click();
    const downloadedWordFilename = path.join(downloadsFolder, `${fileName}.docx`);
    const fixturesWordFilename = path.join(<string>fixturesFolder, `${fileName}.docx`);
    cy.readFile(fixturesWordFilename, null).then((fileContent1: Uint8Array) => {
      cy.readFile(downloadedWordFilename, null).then((fileContent2: Uint8Array) => {
        Promise.all([
          mammoth.convertToHtml({arrayBuffer: fileContent1}),
          mammoth.convertToHtml({arrayBuffer: fileContent2})])
          .then(([fileHtml1, fileHtml2]) => {
            expect(fileHtml1.value, 'word file').to.deep.equal(fileHtml2.value);
          });
      });
    });

    cy.log('**GENERATE EXCEL REPORT**')
    backendConfigurationReportsPage.generateExcelBtn().click();
    const downloadedExcelFilename = path.join(downloadsFolder, `${fileName}.xlsx`);
    const fixturesExcelFilename = path.join(<string>fixturesFolder, `${fileName}.xlsx`);
    cy.readFile(fixturesExcelFilename, 'binary').then((file1Content) => {
      cy.readFile(downloadedExcelFilename, 'binary').then((file2Content) => {
        expect(read(file1Content, {type: 'binary'}), 'excel file').to.deep.equal(read(file2Content, {type: 'binary'}));
      });
    });
  });
});
