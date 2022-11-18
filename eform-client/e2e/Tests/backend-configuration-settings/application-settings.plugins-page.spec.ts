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
    await (await $('#plugin-name0')).waitForDisplayed({ timeout: 50000 });

    let plugin = await pluginPage.getFirstPluginRowObj();
    expect(plugin.version).equal('1.0.0.0');
  });

  it('should activate the plugin', async () => {
    let plugin = await pluginPage.getFirstPluginRowObj();
    if (plugin.name === 'Microting Items Planning Plugin') {
      await plugin.enableOrDisablePlugin();
    } else {
      plugin = await pluginPage.getPluginRowObjByIndex(2);
      if (plugin.name === 'Microting Items Planning Plugin') {
        await plugin.enableOrDisablePlugin();
      } else {
        plugin = await pluginPage.getPluginRowObjByIndex(3);
        await plugin.enableOrDisablePlugin();
      }
    }

    plugin = await pluginPage.getFirstPluginRowObj();
    if (plugin.name === 'Microting Backend Configuration Plugin') {
      await plugin.enableOrDisablePlugin();
    } else {
      plugin = await pluginPage.getPluginRowObjByIndex(2);
      if (plugin.name === 'Microting Backend Configuration Plugin') {
        await plugin.enableOrDisablePlugin();
      } else {
        plugin = await pluginPage.getPluginRowObjByIndex(3);
        await plugin.enableOrDisablePlugin();
      }
    }

    plugin = await pluginPage.getFirstPluginRowObj();
    if (plugin.name === 'Microting Time Planning Plugin') {
      await plugin.enableOrDisablePlugin();
    } else {
      plugin = await pluginPage.getPluginRowObjByIndex(2);
      if (plugin.name === 'Microting Time Planning Plugin') {
        await plugin.enableOrDisablePlugin();
      } else {
        plugin = await pluginPage.getPluginRowObjByIndex(3);
        await plugin.enableOrDisablePlugin();
      }
    }

    plugin = await pluginPage.getFirstPluginRowObj();
    if (plugin.name === 'Microting Items Planning Plugin') {
      expect(plugin.name).equal('Microting Items Planning Plugin');
      expect(plugin.status, 'Microting Items Planning Plugin is not enabled').eq('toggle_on');
    } else {
      if (plugin.name === 'Microting Backend Configuration Plugin') {
        expect(plugin.name).equal('Microting Backend Configuration Plugin');
        expect(plugin.status, 'Microting Backend Configuration Plugin is not enabled').eq('toggle_on');
      } else {
        expect(plugin.name).equal('Microting Time Planning Plugin');
        expect(plugin.status, 'Microting Time Planning Plugin is not enabled').eq('toggle_on');
      }
    }

    plugin = await pluginPage.getPluginRowObjByIndex(2);
    if (plugin.name === 'Microting Items Planning Plugin') {
      expect(plugin.name).equal('Microting Items Planning Plugin');
      expect(plugin.status, 'Microting Items Planning Plugin is not enabled').eq('toggle_on');
    } else {
      if (plugin.name === 'Microting Backend Configuration Plugin') {
        expect(plugin.name).equal('Microting Backend Configuration Plugin');
        expect(plugin.status, 'Microting Backend Configuration Plugin is not enabled').eq('toggle_on');
      } else {
        expect(plugin.name).equal('Microting Time Planning Plugin');
        expect(plugin.status, 'Microting Time Planning Plugin is not enabled').eq('toggle_on');
      }
    }

    plugin = await pluginPage.getPluginRowObjByIndex(3);
    if (plugin.name === 'Microting Items Planning Plugin') {
      expect(plugin.name).equal('Microting Items Planning Plugin');
      expect(plugin.status, 'Microting Items Planning Plugin is not enabled').eq('toggle_on');
    } else {
      if (plugin.name === 'Microting Backend Configuration Plugin') {
        expect(plugin.name).equal('Microting Backend Configuration Plugin');
        expect(plugin.status, 'Microting Backend Configuration Plugin is not enabled').eq('toggle_on');
      } else {
        expect(plugin.name).equal('Microting Time Planning Plugin');
        expect(plugin.status, 'Microting Time Planning Plugin is not enabled').eq('toggle_on');
      }
    }
  });
});
