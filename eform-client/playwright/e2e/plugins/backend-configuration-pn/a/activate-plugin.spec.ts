import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { PluginPage } from '../../../Page objects/Plugin.page';

test.describe('Backend Config plugin enabled', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await new PluginPage(page).Navbar.goToPluginsPage();
  });

  test('should have Backend Configuration plugin enabled', async ({ page }) => {
    test.setTimeout(60000);
    const pluginName = 'Microting Backend Configuration Plugin';

    const pluginRow = page.locator('.mat-mdc-row').filter({ hasText: pluginName }).first();
    await expect(pluginRow).toBeVisible();

    await pluginRow.locator('#actionMenu').click();
    await page.waitForTimeout(500);

    await expect(
      page.locator('[id^="plugin-status-button"]').first().locator('mat-icon')
    ).toContainText('toggle_on');

    await page.keyboard.press('Escape');
  });
});


