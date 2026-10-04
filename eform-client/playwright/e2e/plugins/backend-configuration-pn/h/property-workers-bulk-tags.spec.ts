import { test, expect, Page, Locator } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString, selectValueInNgSelector } from '../../../helper-functions';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';
import { API_TIMEOUT, UI_TIMEOUT, ignoreUnhandledRejections, waitForApiResponse } from '../wait-helpers';

/**
 * #1380 — Medarbejdere: assign tags to several workers at once.
 *
 * Seeds one property, three workers and one worker tag. BT1 ticks two of the
 * three rows, adds the tag through "Tildel tags" and asserts both rows show it
 * while the third does not. BT2 ticks one of them and removes the tag again:
 * only that row loses it.
 *
 * Tags are global in the shared CI DB and `clearTable()` never touches them,
 * so `afterAll` deletes this run's tag (its name carries a random suffix) as
 * well as the workers and the property, best effort.
 */

const property: PropertyCreateUpdate = {
  name: `bt-${generateRandmString(5)}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const makeWorker = (): PropertyWorker => ({
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
});
const workerA = makeWorker();
const workerB = makeWorker();
const workerC = makeWorker();
const fullName = (w: PropertyWorker) => `${w.name} ${w.surname}`;

const tagName = `bt-tag-${generateRandmString(6)}`;
let tagCreated = false;

function rowCheckbox(row: Locator): Locator {
  return row.locator('.mat-column-MtxGridCheckboxColumnDef mat-checkbox');
}

function tagsCell(row: Locator): Locator {
  return row.locator('.mat-column-tags');
}

function bulkBtn(page: Page): Locator {
  return page.locator('#bulkWorkerTagsBtn');
}

/** Opens "Tildel tags", picks the mode and the tag, saves, and waits for the bulk PUT and the list reload. */
async function applyBulkTags(page: Page, mode: 'add' | 'remove'): Promise<void> {
  await bulkBtn(page).click();
  const dialog = page.locator('mat-dialog-container');
  await dialog.locator('#bulkTagsSaveBtn').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await dialog.locator(mode === 'add' ? '#bulkTagsModeAdd' : '#bulkTagsModeRemove').click();
  await selectValueInNgSelector(page, '#bulkTagsSelect', tagName);

  const bulkPromise = waitForApiResponse(
    page,
    'PUT /api/backend-configuration-pn/properties/assignment/bulk-tags',
    (r) => r.url().includes('/properties/assignment/bulk-tags') && r.request().method() === 'PUT',
    API_TIMEOUT,
  );
  const indexPromise = waitForApiResponse(
    page,
    'POST properties/assignment/index-device-user (list reload after bulk tags)',
    (r) => r.url().includes('/properties/assignment/index-device-user') && r.request().method() === 'POST',
    API_TIMEOUT,
  );
  ignoreUnhandledRejections(bulkPromise, indexPromise);

  await dialog.locator('#bulkTagsSaveBtn').click();
  const bulk = await bulkPromise;
  const body = await bulk.json();
  expect(body?.success, `bulk-tags failed: ${JSON.stringify(body)}`).toBe(true);
  await indexPromise;
  await expect(dialog).toBeHidden({ timeout: UI_TIMEOUT });
}

test.describe.serial('Medarbejdere — bulk assign tags (#1380)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
  });

  test.afterAll(async ({ browser }) => {
    // One tag delete plus three worker deletes and one property delete.
    test.setTimeout(180000);
    const CLEANUP_BUDGET_MS = 150000;
    const page = await browser.newPage().catch((err: any) => {
      console.log(`afterAll cleanup failed (non-fatal): could not open a cleanup page: ${err?.message ?? err}`);
      return undefined;
    });
    if (!page) {
      return;
    }
    const cleanup = async () => {
      await page.goto('http://localhost:4200');
      await new LoginPage(page).login();

      const workersPage = new BackendConfigurationPropertyWorkersPage(page);
      await workersPage.goToPropertyWorkers();
      if (tagCreated) {
        try {
          await workersPage.deleteTag(tagName);
        } catch (err: any) {
          console.log(`afterAll: could not delete tag "${tagName}" (non-fatal): ${err?.message ?? err}`);
        }
      }
      await workersPage.clearTable();

      const propertiesPage = new BackendConfigurationPropertiesPage(page);
      await propertiesPage.goToProperties();
      await propertiesPage.clearTable();
    };
    try {
      await Promise.race([cleanup(), new Promise((resolve) => setTimeout(resolve, CLEANUP_BUDGET_MS))]);
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    }
    await page.close().catch(() => undefined);
  });

  test('seed: property + three workers + one tag', async ({ page }) => {
    // One property create and three SDK-backed device-user creates (SLOW_API_TIMEOUT each).
    test.setTimeout(360000);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    for (const worker of [workerA, workerB, workerC]) {
      await workersPage.create(worker);
      await expect(workersPage.workerRow(fullName(worker))).toBeVisible({ timeout: UI_TIMEOUT });
    }
    tagCreated = true;
    await workersPage.createTag(tagName);
  });

  test('BT1: add a tag to two selected workers', async ({ page }) => {
    test.setTimeout(120000);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    const rowA = workersPage.workerRow(fullName(workerA));
    const rowB = workersPage.workerRow(fullName(workerB));
    const rowC = workersPage.workerRow(fullName(workerC));
    await expect(rowA).toBeVisible({ timeout: UI_TIMEOUT });

    await expect(bulkBtn(page)).toBeDisabled({ timeout: UI_TIMEOUT });
    await rowCheckbox(rowA).click();
    await rowCheckbox(rowB).click();
    await expect(bulkBtn(page)).toBeEnabled({ timeout: UI_TIMEOUT });
    await expect(bulkBtn(page)).toContainText('(2)', { timeout: UI_TIMEOUT });

    await applyBulkTags(page, 'add');

    await expect(tagsCell(rowA)).toContainText(tagName, { timeout: UI_TIMEOUT });
    await expect(tagsCell(rowB)).toContainText(tagName, { timeout: UI_TIMEOUT });
    await expect(tagsCell(rowC)).not.toContainText(tagName, { timeout: UI_TIMEOUT });
    // The reload starts a fresh selection.
    await expect(bulkBtn(page)).toBeDisabled({ timeout: UI_TIMEOUT });
  });

  test('BT2: remove the tag from one selected worker only', async ({ page }) => {
    test.setTimeout(120000);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await workersPage.goToPropertyWorkers();
    const rowA = workersPage.workerRow(fullName(workerA));
    const rowB = workersPage.workerRow(fullName(workerB));
    await expect(tagsCell(rowA)).toContainText(tagName, { timeout: UI_TIMEOUT });

    await rowCheckbox(rowA).click();
    await expect(bulkBtn(page)).toContainText('(1)', { timeout: UI_TIMEOUT });

    await applyBulkTags(page, 'remove');

    await expect(tagsCell(rowA)).not.toContainText(tagName, { timeout: UI_TIMEOUT });
    await expect(tagsCell(rowB)).toContainText(tagName, { timeout: UI_TIMEOUT });
  });
});
