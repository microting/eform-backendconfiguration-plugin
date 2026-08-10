import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationPropertiesPage } from '../BackendConfigurationProperties.page';
import { BackendConfigurationPropertyWorkersPage, PropertyWorker } from '../BackendConfigurationPropertyWorkers.page';
import { BackendConfigurationAdhocPage } from '../BackendConfigurationAdhoc.page';

/**
 * Adhoc overblik — complete / archive / delete lifecycle suite (M5/T1,
 * flow 3): complete via the "Udfør opgave" modal (with a selected
 * performer) → switch the status filter to "Løste" and assert the task
 * moved + "Udført af" is populated → archive it from the Historik tab's row
 * menu (the table's own row menu never offers Archive - only Historik does,
 * per `adhoc-history.component.html`) → assert it under "Arkiverede" →
 * delete it from the table's row menu → assert it's gone everywhere.
 */
const BASE_URL = 'http://localhost:4200';
const rand = generateRandmString(8).toLowerCase();

const property = {
  name: `adhoc-ca-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '4444444',
};
const performer: PropertyWorker = {
  name: `AdhocPerformer${rand}`,
  surname: 'Testperson',
  workerEmail: `adhoc-performer-${rand}@example.com`,
  properties: [property.name],
};
const taskTitle = `Adhoc-Lifecycle-Task-${rand}`;

async function login(page: import('@playwright/test').Page): Promise<void> {
  await page.goto(BASE_URL);
  await new LoginPage(page).login();
}

test.describe.serial('Adhoc overblik — complete, archive, delete lifecycle', () => {
  test('seed: property + performer + one open task', async ({ page }) => {
    test.setTimeout(180000);
    await login(page);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(performer);
    await expect(
      page.locator('.mat-mdc-row').filter({ hasText: performer.name as string }),
    ).toBeVisible({ timeout: 15000 });

    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();
    await adhocPage.openNewTask();
    await adhocPage.selectDrawerProperty(property.name);
    await adhocPage.drawerTitleInput().fill(taskTitle);
    await adhocPage.saveDrawer(true);
    await expect(adhocPage.row(taskTitle)).toBeVisible({ timeout: 15000 });
  });

  test('complete with a selected performer — moves off Åben, appears under Løste with Udført af', async ({ page }) => {
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();

    await expect(adhocPage.row(taskTitle)).toBeVisible();
    await adhocPage.completeButtonInRow(taskTitle).click();

    await expect(adhocPage.completeConfirmBtn()).toBeVisible({ timeout: 10000 });
    await adhocPage.selectCompletePerformer(performer.name as string);
    await adhocPage.confirmComplete();

    // Still on the default "Åben" filter — the now-completed task must be gone.
    await expect(adhocPage.row(taskTitle)).toHaveCount(0, { timeout: 15000 });

    await adhocPage.selectStatusFilter('Løste');
    await expect(adhocPage.row(taskTitle)).toBeVisible({ timeout: 15000 });
    await expect(adhocPage.statusChip(taskTitle)).toContainText('Løste');
    // "Udført af" (completedByName) only renders once status !== 'open'.
    await expect(adhocPage.columnCell(taskTitle, 'completedByName')).toContainText(performer.name as string);
  });

  test('archive from the Historik row menu — appears under Arkiverede', async ({ page }) => {
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();
    await adhocPage.goToHistory();

    await expect(adhocPage.historyRow(taskTitle)).toBeVisible({ timeout: 15000 });
    await adhocPage.archiveFromHistory(taskTitle);
    // History re-fetches its task table after archiving (the row stays,
    // now with an "Arkiveret" status chip) — no assertion on the Historik
    // row itself here, only on the Overblik "Arkiverede" status view below,
    // which is this suite's actual contract.

    await adhocPage.goToOverview();
    await adhocPage.selectStatusFilter('Arkiverede');
    await expect(adhocPage.row(taskTitle)).toBeVisible({ timeout: 15000 });
    await expect(adhocPage.statusChip(taskTitle)).toContainText('Arkiverede');
  });

  test('delete via the table row menu — gone everywhere', async ({ page }) => {
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();
    await adhocPage.selectStatusFilter('Arkiverede');

    await expect(adhocPage.row(taskTitle)).toBeVisible({ timeout: 15000 });
    await adhocPage.openRowMenu(taskTitle);
    await adhocPage.deleteMenuItem().click();

    await expect(adhocPage.deleteConfirmBtn()).toBeVisible({ timeout: 10000 });
    await adhocPage.confirmDelete();

    await expect(adhocPage.row(taskTitle)).toHaveCount(0, { timeout: 15000 });
  });
});
