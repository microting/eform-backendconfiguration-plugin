import loginPage from '../../../Login.page';
import backendConfigurationTaskTrackerPage from '../../../BackendConfiguration/BackendConfigurationTaskTracker.page';
import pluginPage from '../../../Plugin.page';

describe('Task tracker', () => {
  before(() => {
    cy.intercept(
      {
        method: 'POST', // Route all GET requests
        url: '/api/backend-configuration-pn/task-tracker/*', // that have a URL that matches
      },
      {fixture: 'backend-configuration-task-tracker-index.json'} // and force the response
    ).as('taskTrackerIndex'); // and assign an alias
    cy.visit('http://localhost:4200');
    loginPage.login();
    pluginPage.Navbar.goToPluginsPage();
    backendConfigurationTaskTrackerPage.goToTaskTracker('@taskTrackerIndex');
  });
  it('should go to task tracker', () => {
    backendConfigurationTaskTrackerPage.rowNum()
      .should('not.eq', 0)
      .should('eq', 49);
    const tasks = backendConfigurationTaskTrackerPage.getRowObjects(36);
    cy.wait(500);
    expect(tasks.length).eq(36);
    const rows = tasks.map(x => x.row);
    rows.forEach(x => {
      x.find('.tasks.white-yellow.task-day-1').should('exist');
    })
  });
});
