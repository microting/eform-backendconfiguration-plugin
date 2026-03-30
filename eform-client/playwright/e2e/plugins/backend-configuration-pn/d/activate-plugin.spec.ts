import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { PluginPage } from '../../../Page objects/Plugin.page';

test.describe('Enable Backend Config plugin', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await new PluginPage(page).Navbar.goToPluginsPage();
  });

  test('should enable Backend Configuration plugin', async ({ page }) => {
    test.setTimeout(180000);
    const pluginName = 'Microting Backend Configuration Plugin';

    await page.locator('.mat-mdc-row').filter({ hasText: pluginName }).first()
      .locator('#actionMenu').click();
    await page.waitForTimeout(500);

    await page.locator('#plugin-status-button0').scrollIntoViewIfNeeded();
    await expect(page.locator('#plugin-status-button0')).toBeVisible();
    await page.locator('#plugin-status-button0').click();
    await page.waitForTimeout(500);

    if (await page.locator('#pluginOKBtn').count() > 0) {
      await page.locator('#pluginOKBtn').click();
      await page.waitForTimeout(100000);
    }

    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await new PluginPage(page).Navbar.goToPluginsPage();

    await page.locator('.mat-mdc-row').filter({ hasText: pluginName }).first()
      .locator('#actionMenu').click();
    await page.waitForTimeout(500);

    await expect(
      page.locator('#plugin-status-button0').locator('mat-icon')
    ).toContainText('toggle_on');
  });
});
