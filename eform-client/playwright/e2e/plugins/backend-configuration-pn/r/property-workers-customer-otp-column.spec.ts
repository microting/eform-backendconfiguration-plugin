import { expect, test } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { BackendConfigurationPropertyWorkersPage } from '../BackendConfigurationPropertyWorkers.page';
import { API_TIMEOUT, UI_TIMEOUT, waitForApiResponse } from '../wait-helpers';

// "Customer no & OTP" on Medarbejdere is hidden when the SDK setting
// skipCloudDeploy is on, as on core's device-users and units pages
// (eform-angular-frontend #8193). The hidden case is covered by
// property-worker-table.component.spec.ts.
//
// WHAT THIS PROTECTS: the page asks core for the setting and still shows the
// column when cloud deploy is not skipped, which is the CI default. The test
// checks that premise from the response itself, so a CI change of the setting
// fails here with a clear message instead of a missing column.

test.describe('Property-worker customer no & OTP column', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    // login() already waits for #newEFormBtn, so the app is loaded when it returns.
    await new LoginPage(page).login();
  });

  test('is shown when cloud deploy is not skipped', async ({ page }) => {
    const skipCloudDeployResponse = waitForApiResponse(
      page,
      'GET api/device-users/skip-cloud-deploy',
      (r) => r.url().includes('/api/device-users/skip-cloud-deploy') && r.request().method() === 'GET',
      API_TIMEOUT,
    );
    await new BackendConfigurationPropertyWorkersPage(page).goToPropertyWorkers();
    const body = await (await skipCloudDeployResponse).json();
    expect(body.success, 'the skipCloudDeploy setting must be readable').toBe(true);
    expect(body.model, 'CI runs with cloud deploy not skipped').toBe(false);

    await expect(page.locator('mtx-grid th.mat-column-customerOtp'), 'the customer no & OTP column header').toBeVisible({
      timeout: UI_TIMEOUT,
    });
  });
});
