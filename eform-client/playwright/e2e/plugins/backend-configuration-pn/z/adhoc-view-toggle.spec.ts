import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { BackendConfigurationAdhocPage } from '../BackendConfigurationAdhoc.page';
import { UI_TIMEOUT } from '../wait-helpers';

/**
 * Ad-hoc tasks — segmented switchers as `mat-button-toggle-group` (#1331,
 * following the compliance page's #1296).
 *
 * The Overblik/Historik group is route-driven: its checked toggle is derived
 * from the URL, so it must follow a click, browser back/forward and the
 * group's own keyboard selection (arrow keys select without a DOM click).
 * The OG/ELLER tag-logic group in the tag popup moves `aria-checked` on
 * click. Needs no seeded data: both views and the tag popup render on an
 * empty database. The drawer's Kun tildelte/Alle group is covered in
 * `adhoc-create-task-drawer.spec.ts`, which seeds the task it needs.
 */
const BASE_URL = 'http://localhost:4200';
const LIST_URL = /\/adhoc-tasks(\?|$)/;
const HISTORY_URL = /\/adhoc-tasks\/history(\?|$)/;

async function login(page: Page): Promise<void> {
  await page.goto(BASE_URL);
  await new LoginPage(page).login();
}

async function expectListChecked(adhocPage: BackendConfigurationAdhocPage): Promise<void> {
  await expect(adhocPage.viewListBtn()).toHaveClass(/mat-button-toggle-checked/, { timeout: UI_TIMEOUT });
  await expect(adhocPage.viewListRadio()).toHaveAttribute('aria-checked', 'true');
  await expect(adhocPage.viewHistoryRadio()).toHaveAttribute('aria-checked', 'false');
}

async function expectHistoryChecked(adhocPage: BackendConfigurationAdhocPage): Promise<void> {
  await expect(adhocPage.viewHistoryBtn()).toHaveClass(/mat-button-toggle-checked/, { timeout: UI_TIMEOUT });
  await expect(adhocPage.viewHistoryRadio()).toHaveAttribute('aria-checked', 'true');
  await expect(adhocPage.viewListRadio()).toHaveAttribute('aria-checked', 'false');
}

test.describe('Adhoc overblik — toggle-group switchers (#1331)', () => {
  test('Overblik/Historik follows the route: click, back, forward and keyboard', async ({ page }) => {
    // Login + a handful of client-side navigations, each bounded by UI_TIMEOUT.
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();

    // A stock single-select group: radiogroup semantics, no aria-pressed.
    await expect(page.locator('mat-button-toggle-group.adhoc-view-toggle')).toHaveAttribute('role', 'radiogroup');
    await expect(adhocPage.viewListBtn()).toHaveJSProperty('tagName', 'MAT-BUTTON-TOGGLE');
    await expectListChecked(adhocPage);

    await adhocPage.goToHistory();
    await expect(page).toHaveURL(HISTORY_URL, { timeout: UI_TIMEOUT });
    await expectHistoryChecked(adhocPage);

    await page.goBack();
    await expect(page).toHaveURL(LIST_URL, { timeout: UI_TIMEOUT });
    await adhocPage.mainListView().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await expectListChecked(adhocPage);

    await page.goForward();
    await expect(page).toHaveURL(HISTORY_URL, { timeout: UI_TIMEOUT });
    await adhocPage.historyView().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await expectHistoryChecked(adhocPage);

    // Arrow keys select through the group's keydown handler, not a click, so
    // this proves the navigation hangs off the group's (change).
    await adhocPage.viewHistoryRadio().focus();
    await page.keyboard.press('ArrowLeft');
    await expect(page).toHaveURL(LIST_URL, { timeout: UI_TIMEOUT });
    await adhocPage.mainListView().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
    await expectListChecked(adhocPage);

    // Re-clicking the already-checked Overblik keeps the list view.
    await adhocPage.goToOverview();
    await expect(page).toHaveURL(LIST_URL, { timeout: UI_TIMEOUT });
    await expectListChecked(adhocPage);
  });

  test('OG/ELLER tag logic is a toggle group; a click moves aria-checked', async ({ page }) => {
    test.setTimeout(90000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();
    await adhocPage.openTagFilterPanel();

    // ELLER ('or') is the reducer default.
    await expect(adhocPage.tagLogicRadio('or')).toHaveAttribute('aria-checked', 'true', { timeout: UI_TIMEOUT });
    await expect(adhocPage.tagLogicRadio('and')).toHaveAttribute('aria-checked', 'false');

    await adhocPage.setTagLogic('and');
    await expect(adhocPage.tagLogicRadio('and')).toHaveAttribute('aria-checked', 'true', { timeout: UI_TIMEOUT });
    await expect(adhocPage.tagLogicRadio('or')).toHaveAttribute('aria-checked', 'false');

    await adhocPage.setTagLogic('or');
    await expect(adhocPage.tagLogicRadio('or')).toHaveAttribute('aria-checked', 'true', { timeout: UI_TIMEOUT });
    await expect(adhocPage.tagLogicRadio('and')).toHaveAttribute('aria-checked', 'false');
  });
});
