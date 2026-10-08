import { expect, Page, test } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';
import { ignoreUnhandledRejections, SLOW_API_TIMEOUT, UI_TIMEOUT, waitForApiResponse } from '../wait-helpers';

// The "Managing tags" field of a manager in the property-worker dialog:
// - a tag that has been picked disappears from the list, so the list gets shorter
//   with every pick;
// - "Select all" at the top of the list adds every tag the list shows, i.e. only
//   the ones matching the search when a search is typed, and then closes the list
//   so no stale search text is left behind;
// - a managing tag that was removed can be given back and saved. That save used to
//   fail with a duplicate key on the server (500), which left the dialog spinning.

const TIME_REGISTRATION_TAB = 'Timeregistrering';

const rand = generateRandmString(4);
const tagA = `MgrA-${rand}`;
const tagB = `MgrB-${rand}`;
const tagC = `MgrC-${rand}`;

const property: PropertyCreateUpdate = {
  name: `MgrTags ${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const worker: PropertyWorker = {
  name: `Mgr${rand}`,
  surname: 'Tags',
  language: 'Dansk',
  properties: [property.name],
  workerEmail: `${generateRandmString(5)}@test.com`,
  timeRegistrationEnabled: true,
};
const workerFullName = `${worker.name} ${worker.surname}`;

function dialog(page: Page) {
  return page.locator('mat-dialog-container');
}

function managingTagsSelect(page: Page) {
  return dialog(page).locator('mtx-select[formControlName="managingTagIds"]');
}

function managingTagSearchInput(page: Page) {
  return managingTagsSelect(page).locator('input');
}

function dropdownPanel(page: Page) {
  return page.locator('ng-dropdown-panel');
}

function selectAllButton(page: Page) {
  return page.locator('#managingTagsSelectAll');
}

/** Options the open list offers; ng-select's "No items found" row is a disabled option. */
function listedOptions(page: Page) {
  return page.locator('ng-dropdown-panel .ng-option:not(.ng-option-disabled)');
}

async function openTimeRegistrationTab(page: Page): Promise<void> {
  const tab = dialog(page).locator('.mat-mdc-tab').filter({ hasText: TIME_REGISTRATION_TAB });
  await tab.click({ timeout: UI_TIMEOUT });
  await expect(tab).toHaveAttribute('aria-selected', 'true', { timeout: UI_TIMEOUT });
  await expect(dialog(page).locator('#isManager')).toBeVisible({ timeout: UI_TIMEOUT });
}

async function openManagingTagsList(page: Page): Promise<void> {
  await managingTagsSelect(page).locator('.ng-select-container').click({ timeout: UI_TIMEOUT });
  await expect(dropdownPanel(page)).toBeVisible({ timeout: UI_TIMEOUT });
}

async function searchManagingTags(page: Page, term: string): Promise<void> {
  await managingTagSearchInput(page).fill(term, { timeout: UI_TIMEOUT });
  await expect(listedOptions(page).first()).toBeVisible({ timeout: UI_TIMEOUT });
}

function chosenTag(page: Page, name: string) {
  return managingTagsSelect(page).locator('.ng-value').filter({ hasText: name });
}

/** "Select all" closes the list and clears the search, so nothing stale is left. */
async function clickSelectAll(page: Page): Promise<void> {
  await selectAllButton(page).click({ timeout: UI_TIMEOUT });
  await expect(dropdownPanel(page), '"Select all" must close the list').toHaveCount(0, { timeout: UI_TIMEOUT });
  await expect(managingTagSearchInput(page), '"Select all" must clear the search').toHaveValue('');
}

async function cancelEditModal(workersPage: BackendConfigurationPropertyWorkersPage): Promise<void> {
  const cancelBtn = workersPage.cancelEditBtn();
  await cancelBtn.click({ timeout: UI_TIMEOUT });
  await cancelBtn.waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
}

/** Saves the edit dialog, asserts the managing-tags PUT succeeded and waits for the list to reload. */
async function saveEdit(page: Page, context: string): Promise<void> {
  const put = waitForApiResponse(
    page,
    `PUT /api/time-planning-pn/settings/assigned-site (${context})`,
    r =>
      new URL(r.url()).pathname.endsWith('/api/time-planning-pn/settings/assigned-site') &&
      r.request().method() === 'PUT',
    SLOW_API_TIMEOUT
  );
  // After a save the dialog closes and the worker list reloads; reopening a row
  // before that reload lands would click a row that is about to be replaced.
  const listRefresh = waitForApiResponse(
    page,
    `POST /api/backend-configuration-pn/properties/assignment/index-device-user (list refresh after ${context})`,
    r =>
      r.url().includes('/api/backend-configuration-pn/properties/assignment/index-device-user') &&
      r.request().method() === 'POST',
    SLOW_API_TIMEOUT
  );
  ignoreUnhandledRejections(put, listRefresh);
  const saveBtn = dialog(page).locator('#saveEditBtn');
  await expect(saveBtn).toBeEnabled({ timeout: UI_TIMEOUT });
  await saveBtn.click({ timeout: UI_TIMEOUT });
  const response = await put;
  const result = await response.json().catch(() => null);
  expect(response.status(), `${context}: assigned-site PUT status (${JSON.stringify(result)})`).toBe(200);
  expect(result?.success, `${context}: assigned-site PUT success (${result?.message ?? ''})`).toBe(true);
  await saveBtn.waitFor({ state: 'hidden', timeout: UI_TIMEOUT });
  await listRefresh;
}

test.describe.serial('Property-worker dialog: managing tags', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
  });

  test('picked tags leave the list and "Select all" adds what the list shows', async ({ page }) => {
    // 5 min: login, one property, three tags and one device user created, then one edit save.
    test.setTimeout(300000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    for (const tag of [tagA, tagB, tagC]) {
      await workersPage.createTag(tag);
    }
    await workersPage.create(worker);
    await expect(page.locator('.mat-mdc-row').filter({ hasText: workerFullName })).toHaveCount(1, {
      timeout: UI_TIMEOUT,
    });

    await workersPage.openEditModalFor(workerFullName);
    await openTimeRegistrationTab(page);
    await dialog(page).locator('#isManager').click({ timeout: UI_TIMEOUT });
    await expect(dialog(page).locator('#isManager input[type="checkbox"]')).toBeChecked({ timeout: UI_TIMEOUT });

    // A picked tag leaves the list, and the list stays open for the next pick.
    await openManagingTagsList(page);
    await searchManagingTags(page, tagA);
    await listedOptions(page).filter({ hasText: tagA }).click({ timeout: UI_TIMEOUT });
    await expect(chosenTag(page, tagA), 'the picked tag must show as chosen').toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(dropdownPanel(page), 'the list must stay open after a pick').toBeVisible();
    await managingTagSearchInput(page).fill('', { timeout: UI_TIMEOUT });
    await expect(listedOptions(page).filter({ hasText: tagC })).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(listedOptions(page).filter({ hasText: tagA }), 'a picked tag must leave the list').toHaveCount(0);

    // With a search typed, "Select all" adds only the matching tags.
    await searchManagingTags(page, tagB);
    await clickSelectAll(page);
    await expect(chosenTag(page, tagB)).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(chosenTag(page, tagC), 'a tag outside the search must not be added').toHaveCount(0);

    // Without a search, "Select all" adds every remaining tag.
    await openManagingTagsList(page);
    await expect(listedOptions(page).filter({ hasText: tagC })).toHaveCount(1, { timeout: UI_TIMEOUT });
    await clickSelectAll(page);
    await expect(chosenTag(page, tagC)).toHaveCount(1, { timeout: UI_TIMEOUT });
    // Every tag is chosen now, so there is nothing left to list: the list does not open.
    await managingTagsSelect(page).locator('.ng-select-container').click({ timeout: UI_TIMEOUT });
    await expect(dropdownPanel(page), 'with every tag chosen the list has nothing to show').toHaveCount(0, {
      timeout: UI_TIMEOUT,
    });

    await saveEdit(page, 'first save with all tags');
  });

  test('a removed managing tag can be given back and saved', async ({ page }) => {
    // 4 min: login and three edit-dialog round trips, two of them saving.
    test.setTimeout(240000);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();

    // The first save stored all three tags.
    await workersPage.openEditModalFor(workerFullName);
    await openTimeRegistrationTab(page);
    for (const tag of [tagA, tagB, tagC]) {
      await expect(chosenTag(page, tag), `${tag} must be saved as managing tag`).toHaveCount(1, { timeout: UI_TIMEOUT });
    }

    // Remove tag A and save.
    await chosenTag(page, tagA).locator('.ng-value-icon').click({ timeout: UI_TIMEOUT });
    await expect(chosenTag(page, tagA)).toHaveCount(0, { timeout: UI_TIMEOUT });
    await saveEdit(page, 'save without tag A');

    // Give tag A back. The server used to answer 500 (duplicate key) here.
    await workersPage.openEditModalFor(workerFullName);
    await openTimeRegistrationTab(page);
    await expect(chosenTag(page, tagA)).toHaveCount(0, { timeout: UI_TIMEOUT });
    await openManagingTagsList(page);
    await searchManagingTags(page, tagA);
    await clickSelectAll(page);
    await expect(chosenTag(page, tagA)).toHaveCount(1, { timeout: UI_TIMEOUT });
    await saveEdit(page, 'save giving tag A back');

    await workersPage.openEditModalFor(workerFullName);
    await openTimeRegistrationTab(page);
    await expect(chosenTag(page, tagA), 'tag A must be a managing tag again after reopening').toHaveCount(1, {
      timeout: UI_TIMEOUT,
    });
    await cancelEditModal(workersPage);
  });
});
