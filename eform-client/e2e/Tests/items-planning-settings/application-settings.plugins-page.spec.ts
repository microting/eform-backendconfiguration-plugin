import loginPage from '../../Page objects/Login.page';
import myEformsPage from '../../Page objects/MyEforms.page';
import pluginPage from '../../Page objects/Plugin.page';

import { expect } from 'chai';

describe('Application settings page - site header section', function () {
  before(function () {
    loginPage.open('/auth');
  });
  it('should go to plugin settings page', function () {
    loginPage.login();
    myEformsPage.Navbar.goToPluginsPage();
    $('#plugin-name').waitForDisplayed({ timeout: 50000 });

    const plugin = pluginPage.getFirstPluginRowObj();
    expect(plugin.id).equal(1);
    expect(plugin.name).equal('Microting Items Planning Plugin');
    expect(plugin.version).equal('1.0.0.0');
    expect(plugin.status, 'status is not equal').eq(false);
  });

  it('should activate the plugin', function () {
    let plugin = pluginPage.getFirstPluginRowObj();
    plugin.enableOrDisablePlugin();

    // $('Microting Items Planning Plugin').waitForDisplayed({timeout: 10000});
    plugin = pluginPage.getFirstPluginRowObj();
    expect(plugin.id).equal(1);
    expect(plugin.name).equal('Microting Items Planning Plugin');
    expect(plugin.version).equal('1.0.0.0');
    expect(plugin.status, 'status is not equal').eq(true);
  });
});
