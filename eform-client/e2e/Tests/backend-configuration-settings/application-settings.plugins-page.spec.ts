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

    const backendPlugin = await pluginPage.getFirstPluginRowObj();
    expect(backendPlugin.id).equal(1);
    expect(backendPlugin.name).equal('Microting Items Planning Plugin');
    expect(backendPlugin.version).equal('1.0.0.0');
    expect(backendPlugin.status, 'status is not equal').eq(false);

    const itemsPlanningPlugin = await pluginPage.getPluginRowObjByIndex(2);
    expect(
      itemsPlanningPlugin,
      'Items Planning plugin not found or not load'
    ).not.equal(undefined);
    expect(itemsPlanningPlugin.id, 'id is not equal').equal(2);
    expect(itemsPlanningPlugin.name, 'name is not equal').equal('Microting Backend Configuration Plugin');
    expect(itemsPlanningPlugin.version, 'version is not equal').equal('1.0.0.0');
    expect(itemsPlanningPlugin.status, 'status is not equal').eq(false);
  });

  it('should activate the plugin', async () => {
    let backendPlugin = await pluginPage.getFirstPluginRowObj();
    await backendPlugin.enableOrDisablePlugin();

    backendPlugin = await pluginPage.getFirstPluginRowObj();
    expect(backendPlugin.id).equal(1);
    expect(backendPlugin.name).equal('Microting Items Planning Plugin');
    expect(backendPlugin.version).equal('1.0.0.0');
    expect(
      backendPlugin.status,
      'backendConfigurationPlugin is not enabled'
    ).eq(true);

    let itemsPlanningPlugin = await pluginPage.getPluginRowObjByIndex(2);
    await itemsPlanningPlugin.enableOrDisablePlugin();

    itemsPlanningPlugin = await pluginPage.getPluginRowObjByIndex(2);
    expect(itemsPlanningPlugin.id).equal(2);
    expect(itemsPlanningPlugin.name).equal('Microting Backend Configuration Plugin');
    expect(itemsPlanningPlugin.version).equal('1.0.0.0');
    expect(itemsPlanningPlugin.status, 'itemsPlanningPlugin is not enabled').eq(
      true
    );
  });
});
