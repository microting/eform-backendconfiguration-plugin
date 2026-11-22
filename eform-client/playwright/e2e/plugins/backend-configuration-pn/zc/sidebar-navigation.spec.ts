import { expect, test } from '@playwright/test';
import { ADMIN_EMAIL, ADMIN_PASSWORD, loginAs } from '../archive-api-helpers';
import { BackendConfigurationPropertiesPage } from '../BackendConfigurationProperties.page';
import { holdApiGetRequests, ignoreUnhandledRejections, UI_TIMEOUT } from '../wait-helpers';

/** The hold is armed before the login, so its budget covers a cold one: the page, the login button, the sign-in. */
const MENU_REQUEST_TIMEOUT = 150000;

/**
 * The landing page does not wait for the sidebar's menu request. Navigating in that gap used to hang archive-leader's
 * beforeAll: "not rendered yet" was read as "collapsed", and the parent that was then clicked is not in a flat menu.
 */
test('goToProperties waits for a sidebar that renders after the landing page', async ({ page }) => {
  // A cold login and one page load.
  test.setTimeout(180000);
  const menu = await holdApiGetRequests(page, 'GET /api/menu/current-user', '/api/menu/current-user',
    MENU_REQUEST_TIMEOUT);
  try {
    await loginAs(page, ADMIN_EMAIL, ADMIN_PASSWORD, { landing: '#newEFormBtn' });
    await menu.held;
    const properties = new BackendConfigurationPropertiesPage(page);
    await expect(properties.backendConfigurationPnPropertiesButton()).toHaveCount(0, { timeout: UI_TIMEOUT });

    // Started while the menu is held, so its first look at the sidebar finds it empty.
    const navigated = properties.goToProperties();
    ignoreUnhandledRejections(navigated);
    await menu.release();
    await navigated;
  } finally {
    await menu.release();
  }
});
