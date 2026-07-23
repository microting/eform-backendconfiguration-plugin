import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationPropertiesPage } from '../BackendConfigurationProperties.page';
import { BackendConfigurationPropertyWorkersPage, PropertyWorker } from '../BackendConfigurationPropertyWorkers.page';
import { BackendConfigurationAdhocPage } from '../BackendConfigurationAdhoc.page';

/**
 * Adhoc overblik — "Ny opgave" drawer suite (M5/T1, flow 2): fills all
 * three drawer sections ("Ejendom og opgave", "Tildel til personer",
 * "Udfør senest og visning første gang") and asserts the resulting row's
 * content (Haster badge, tag chip, "Tildelt til" worker, "Sidste frist").
 *
 * Worker seeding reuses `BackendConfigurationPropertyWorkersPage.create()`
 * (device user + property assignment via the "Ejendomme" tab) - the same
 * flow `n/calendar-default-board.spec.ts` and others rely on elsewhere in
 * this suite. That page object's own comments note a known CI flakiness
 * (an occasional 500 from `create-device-user`, tolerated by closing the
 * dialog manually); the seed test below asserts the row actually landed in
 * the property-workers table before proceeding, so a failed seed fails
 * fast here rather than surfacing as an unrelated drawer assertion.
 */
const BASE_URL = 'http://localhost:4200';
const rand = generateRandmString(8).toLowerCase();

const property = {
  name: `adhoc-drw-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '3333333',
};
const worker: PropertyWorker = {
  name: `AdhocWorker${rand}`,
  surname: 'Testperson',
  workerEmail: `adhoc-worker-${rand}@example.com`,
  properties: [property.name],
};
const tagName = `adhoc-tag-${rand}`;
const taskTitle = `Adhoc-Drawer-Task-${rand}`;

async function login(page: import('@playwright/test').Page): Promise<void> {
  await page.goto(BASE_URL);
  await new LoginPage(page).login();
}

// A deadline ~20 days out — safely inside the "6+ days" (green) deadline
// band, and outside the current month so the datepicker's year/month
// navigation (`selectDateOnNewDatePicker`) is exercised meaningfully.
const deadline = new Date();
deadline.setDate(deadline.getDate() + 20);

test.describe.serial('Adhoc overblik — new task drawer (all sections)', () => {
  test('seed: property + assigned worker + tag', async ({ page }) => {
    test.setTimeout(180000);
    await login(page);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);
    await expect(
      page.locator('.mat-mdc-row').filter({ hasText: worker.name as string }),
    ).toBeVisible({ timeout: 15000 });
  });

  test('fill property/title/urgent/tag/worker/executionRule/deadline and save', async ({ page }) => {
    test.setTimeout(180000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();

    await adhocPage.openNewTask();

    // "Ejendom og opgave" (expanded by default).
    await adhocPage.selectDrawerProperty(property.name);
    await adhocPage.drawerTitleInput().fill(taskTitle);
    await adhocPage.drawerUrgentCheckbox().click();
    await adhocPage.createDrawerTag(tagName);
    await expect(adhocPage.drawerSelectedTagChips().filter({ hasText: tagName })).toBeVisible({ timeout: 10000 });

    // "Tildel til personer" (collapsed by default - expandDrawerSection handles it).
    await adhocPage.setExecutionRule('assignedOnly');
    await adhocPage.assignDrawerWorker(worker.name as string);

    // "Udfør senest og visning første gang" (collapsed by default).
    await adhocPage.pickDrawerDeadline(deadline.getFullYear(), deadline.getMonth() + 1, deadline.getDate());

    await adhocPage.saveDrawer(true);
    await expect(adhocPage.row(taskTitle)).toBeVisible({ timeout: 15000 });
  });

  test('the saved row shows Haster, the tag, the assigned worker and the deadline', async ({ page }) => {
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();

    const row = adhocPage.row(taskTitle);
    await expect(row).toBeVisible();

    // Haster pill (adhoc-table.component.html titleTpl - `*ngIf="row.urgent"`).
    await expect(adhocPage.columnCell(taskTitle, 'title').locator('.haster-pill')).toContainText('Haster');

    // Selected tag rendered as a chip in the tags column.
    await expect(adhocPage.columnCell(taskTitle, 'tags')).toContainText(tagName);

    // "Tildelt til" (assignedTo column) shows the assigned worker's display name.
    await expect(adhocPage.columnCell(taskTitle, 'assignedTo')).toContainText(worker.name as string);

    // "Sidste frist" (deadline column) is no longer "Ingen sidste frist" -
    // the deadline bar + a day-count/overdue/today label render instead.
    await expect(adhocPage.columnCell(taskTitle, 'deadline').locator('.cell-muted')).toHaveCount(0);
    await expect(adhocPage.columnCell(taskTitle, 'deadline').locator('.deadline-bar')).toBeVisible();
  });
});
