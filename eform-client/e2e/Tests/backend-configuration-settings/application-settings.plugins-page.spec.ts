import loginPage from '../../Page objects/Login.page';
import myEformsPage from '../../Page objects/MyEforms.page';
import pluginPage from '../../Page objects/Plugin.page';

import { expect } from 'chai';

describe('Application settings page - site header section', function () {
  before(async () => {
    await loginPage.open('/auth');
  });
  it('should go to plugin settings page', async () => {
    await loginPage.login();
    await myEformsPage.Navbar.goToPluginsPage();
    await (await $('#plugin-name')).waitForDisplayed({ timeout: 50000 });

    let plugin = await pluginPage.getFirstPluginRowObj();
    if (plugin.name === 'Microting Items Planning Plugin') {
      expect(plugin.name).equal('Microting Items Planning Plugin');
    } else {
      expect(plugin.name).equal('Microting Backend Configuration Plugin');
    }
    expect(plugin.version).equal('1.0.0.0');

    plugin = await pluginPage.getPluginRowObjByIndex(2);
    if (plugin.name === 'Microting Items Planning Plugin') {
      expect(plugin.name).equal('Microting Items Planning Plugin');
    } else {
      expect(plugin.name).equal('Microting Backend Configuration Plugin');
    }
    expect(plugin.version).equal('1.0.0.0');
  });

  it('should activate the plugin', async () => {
    let plugin = await pluginPage.getFirstPluginRowObj();
    await plugin.enableOrDisablePlugin();

    plugin = await pluginPage.getPluginRowObjByIndex(2);
    await plugin.enableOrDisablePlugin();

    plugin = await pluginPage.getFirstPluginRowObj();
    if (plugin.name === 'Microting Items Planning Plugin') {
      expect(plugin.name).equal('Microting Items Planning Plugin');
      expect(plugin.status, 'Microting Items Planning Plugin is not enabled').eq(true);
    } else {
      expect(plugin.name).equal('Microting Backend Configuration Plugin');
      expect(plugin.status, 'Microting Backend Configuration Plugin is not enabled').eq(true);
    }

    plugin = await pluginPage.getPluginRowObjByIndex(2);
    if (plugin.name === 'Microting Items Planning Plugin') {
      expect(plugin.name).equal('Microting Items Planning Plugin');
      expect(plugin.status, 'Microting Items Planning Plugin is not enabled').eq(true);
    } else {
      expect(plugin.name).equal('Microting Backend Configuration Plugin');
      expect(plugin.status, 'Microting Backend Configuration Plugin is not enabled').eq(true);
    }
  });
});
