import loginPage from './Login.page';
import pluginPage from './Plugin.page';
import {beforeEach} from 'mocha';

describe('Enable Backend Config plugin', () => {
  beforeEach(() => {
    cy.visit('http://localhost:4200');
    loginPage.login();
    pluginPage.Navbar.goToPluginsPage();
    pluginPage.rowNum()
      .should('not.eq', 0) // we have plugins list
      .should('eq', 2); // we have only 2 plugins: items planning and backend config
  });
  it('should enabled Items Planning plugin', () => {
    const pluginName = 'Microting Items Planning Plugin';
    pluginPage.enablePluginByName(pluginName);
    const row = cy.contains('.mat-row', pluginName).first();
    row.find('.mat-column-actions button')
      .should('contain.text', 'toggle_on'); // plugin is enabled
  });
  it('should enabled Backend Config plugin', () => {
    const pluginName = 'Microting Backend Configuration Plugin';
    pluginPage.enablePluginByName(pluginName);
    const row = cy.contains('.mat-row', pluginName).first();
    row.find('.mat-column-actions button')
      .should('contain.text', 'toggle_on'); // plugin is enabled
  });
});
