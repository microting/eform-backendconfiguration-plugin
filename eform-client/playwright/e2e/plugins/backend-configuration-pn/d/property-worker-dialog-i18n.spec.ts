import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { BackendConfigurationPropertyWorkersPage } from '../BackendConfigurationPropertyWorkers.page';
import { UI_TIMEOUT } from '../wait-helpers';

/**
 * #1378 — the "Ny medarbejder" dialog showed English labels ("Phone",
 * "e-mail", "Backend user", the "Required" errors) to a Danish admin, because
 * the template used translate keys that no locale file defines, or hardcoded
 * English text, so ngx-translate fell back to the raw key.
 *
 * The seeded admin's default UI locale is Danish, so the dialog must render
 * the Danish values from the plugin / core da.ts. Nothing is saved: the dialog
 * is opened, inspected and cancelled.
 */
test.describe('Property worker dialog — Danish labels', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
  });

  test('create dialog labels and validation errors are translated', async ({ page }) => {
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.openCreateModal(null);

    const dialog = page.locator('mat-dialog-container');
    const fieldOf = (inputId: string) =>
      dialog.locator('mat-form-field').filter({ has: page.locator(`#${inputId}`) });

    // 'Phone number' (plugin da.ts), 'Email' (core da.ts).
    await expect(fieldOf('phoneNumber').locator('mat-label')).toHaveText('Telefonnummer', { timeout: UI_TIMEOUT });
    await expect(fieldOf('workerEmail').locator('mat-label')).toHaveText('E-mail', { timeout: UI_TIMEOUT });
    await expect(dialog.locator('#webAccessEnabled')).toContainText('Backend-bruger', { timeout: UI_TIMEOUT });

    // Touch the empty, required first name and leave it: its error must be Danish.
    await workersPage.createFirstNameInput().focus();
    await workersPage.createLastNameInput().focus();
    await expect(fieldOf('firstName').locator('mat-error')).toHaveText('Påkrævet', { timeout: UI_TIMEOUT });

    await workersPage.cancelCreateBtn().click();
    await expect(dialog).toBeHidden({ timeout: UI_TIMEOUT });
  });
});
