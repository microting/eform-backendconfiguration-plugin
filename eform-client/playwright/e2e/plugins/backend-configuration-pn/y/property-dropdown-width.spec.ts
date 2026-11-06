import { test, expect, Locator, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import { TaskListPage } from '../task-list.page';
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';

/**
 * Property dropdowns show the whole property name, up to 80 characters.
 *
 * The property filter on the Compliance page was pinned to 190px, and the
 * open list of an mtx-select is exactly as wide as its field, so a long
 * property name (name + CHR number) was cut off with an ellipsis both in the
 * list and in the field once picked. The task list's property filter had the
 * same problem: a quarter of the filter grid is far narrower than an
 * 80-character name.
 *
 * PW1: on the Compliance page `#complianceFilterProperty` is wider than the
 *      old 190px, the open list is too, the 80-character option is not
 *      truncated, and once picked the field shows it untruncated as well.
 * PW2: the same for the task list's `#taskListPropertyFilter` (a
 *      multi-select, so the picked value is a chip and not asserted).
 *
 * "Not truncated" is `scrollWidth <= clientWidth`: ng-select lays options and
 * the single value out with `white-space: nowrap; overflow: hidden;
 * text-overflow: ellipsis`, so a cut-off text overflows its box.
 */

const BASE_URL = 'http://localhost:4200';
const COMPLIANCE_URL = `${BASE_URL}/plugins/backend-configuration-pn/compliance-report`;
const OLD_FIELD_WIDTH = 190;
const rand = generateRandmString(5).toLowerCase();

// Exactly 80 characters: the longest name the dropdowns have to show in full.
// Heavy on wide capitals (W, M), so a field sized by character count rather
// than by the rendered text would still cut it off.
const longName = `pdw-${rand} `.padEnd(80, 'WM').slice(0, 80);

const property: PropertyCreateUpdate = {
  name: longName,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

/** Logs in as the default admin; returns once the app has left the login page. */
async function login(page: Page): Promise<void> {
  await page.goto(BASE_URL);
  await new LoginPage(page).login();
  await page.waitForURL(url => !url.pathname.startsWith('/auth'), { timeout: UI_TIMEOUT });
}

function fieldOf(page: Page, selectId: string): Locator {
  return page.locator(`mat-form-field:has(#${selectId})`);
}

async function widthOf(locator: Locator, what: string): Promise<number> {
  const box = await locator.boundingBox();
  expect(box, `${what} has no layout box`).not.toBeNull();
  return box!.width;
}

async function overflowOf(locator: Locator): Promise<{ scrollWidth: number; clientWidth: number }> {
  return locator.evaluate((el: HTMLElement) => ({ scrollWidth: el.scrollWidth, clientWidth: el.clientWidth }));
}

/**
 * Opens the property select and asserts the field, the open list and the
 * long option are all wide enough. Leaves the list open.
 */
async function expectOpenListShowsWholeName(page: Page, selectId: string): Promise<Locator> {
  const field = fieldOf(page, selectId);
  await field.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  await page.locator(`#${selectId}`).click();
  const panel = page.locator(`#${selectId} .ng-dropdown-panel`);
  await panel.waitFor({ state: 'visible', timeout: UI_TIMEOUT });
  const option = panel.locator('.ng-option', { hasText: longName });
  await option.waitFor({ state: 'visible', timeout: API_TIMEOUT });

  // Measured once the list holds the long name, so the field has had its
  // chance to size to it: it must fit the whole name, not just beat 190px.
  const fieldWidth = await widthOf(field, `#${selectId} form field`);
  expect(fieldWidth, `#${selectId} field is still pinned to ${OLD_FIELD_WIDTH}px`).toBeGreaterThan(OLD_FIELD_WIDTH);
  const nameWidth = (await overflowOf(option)).scrollWidth;
  expect(
    fieldWidth,
    `#${selectId} field (${fieldWidth}px) is narrower than the 80-character name (${nameWidth}px)`,
  ).toBeGreaterThanOrEqual(nameWidth);

  const panelWidth = await widthOf(panel, `#${selectId} dropdown panel`);
  expect(panelWidth, `#${selectId} open list is no wider than ${OLD_FIELD_WIDTH}px`).toBeGreaterThan(OLD_FIELD_WIDTH);

  const { scrollWidth, clientWidth } = await overflowOf(option);
  expect(
    scrollWidth,
    `#${selectId}: the 80-character option is truncated (text ${scrollWidth}px, box ${clientWidth}px)`,
  ).toBeLessThanOrEqual(clientWidth);
  return option;
}

test.describe.serial('Property dropdowns show the whole property name', () => {
  test.afterAll(async ({ browser }) => {
    // Remove the long-named property so the later specs of this shard do not
    // see it in their property lists, the way the sibling specs of this shard
    // clean up (UI login, clear the properties table). Non-fatal and bounded:
    // a failed cleanup is logged, never fails the run.
    const page = await browser.newPage().catch((err: any) => {
      console.log(`afterAll cleanup failed (non-fatal): could not open a cleanup page: ${err?.message ?? err}`);
      return undefined;
    });
    if (!page) {
      return;
    }
    const cleanup = async () => {
      await login(page);
      const propertiesPage = new BackendConfigurationPropertiesPage(page);
      await propertiesPage.goToProperties();
      await propertiesPage.clearTable();
    };
    let timer: ReturnType<typeof setTimeout> | undefined;
    try {
      await Promise.race([
        cleanup(),
        new Promise((_, reject) => {
          timer = setTimeout(() => reject(new Error('cleanup took over 60s')), 60000);
        }),
      ]);
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    } finally {
      clearTimeout(timer);
    }
    try { await page.close(); } catch {}
  });

  test('seed: a property with an 80-character name', async ({ page }) => {
    // One UI property create (SDK folder provisioning).
    test.setTimeout(180000);
    expect(longName.length).toBe(80);
    await login(page);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
  });

  test('PW1: the Compliance property filter shows the whole name', async ({ page }) => {
    // One login, one page, one pick.
    test.setTimeout(120000);
    await login(page);
    await page.goto(COMPLIANCE_URL);
    await page.locator('#complianceFilterProperty').waitFor({ state: 'visible', timeout: 60000 });

    const option = await expectOpenListShowsWholeName(page, 'complianceFilterProperty');
    await option.click();

    const value = page.locator('#complianceFilterProperty .ng-value');
    await expect(value.locator('.ng-value-label')).toHaveText(longName, { timeout: UI_TIMEOUT });
    const { scrollWidth, clientWidth } = await overflowOf(value);
    expect(
      scrollWidth,
      `the picked 80-character name is truncated in the field (text ${scrollWidth}px, box ${clientWidth}px)`,
    ).toBeLessThanOrEqual(clientWidth);
  });

  test('PW2: the task list property filter shows the whole name', async ({ page }) => {
    // One login, one page.
    test.setTimeout(120000);
    await login(page);
    await new TaskListPage(page).goto();

    await expectOpenListShowsWholeName(page, 'taskListPropertyFilter');
    await page.keyboard.press('Escape');
    await expect(page.locator('#taskListPropertyFilter .ng-dropdown-panel')).toBeHidden({ timeout: UI_TIMEOUT });
  });
});
