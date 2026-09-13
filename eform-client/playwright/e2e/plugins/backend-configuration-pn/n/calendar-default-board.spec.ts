import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import { API_TIMEOUT, UI_TIMEOUT } from '../wait-helpers';

// Regression for the user report: the create-task modal pre-selected the
// lowest-id board (first created) whenever more than one board was active,
// ignoring the board the user had just activated. PropertyCreateUpdate fields:
// name?, chrNumber?, cvrNumber?, address?, workOrderFlow?.
const property: PropertyCreateUpdate = {
  name: 'cal-board-' + generateRandmString(5),
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

// Board A is created first (lower id, the "Miljøtilsyn" analogue); board B is
// created + activated last. With both active, the modal must default to B.
const boardA = 'A-' + generateRandmString(5);
const boardB = 'B-' + generateRandmString(5);

// --- helpers --------------------------------------------------------------

// #1209 moved the calendar list out of the retired sidebar and into the
// toolbar's "Kalendere" dropdown. The rows keep their `.board-item` /
// `.board-name` / `.board-checkbox` classes, but they now live in a mat-menu
// panel in `.cdk-overlay-container`, so every helper opens the dropdown first.
// The panel deliberately stays open across check/uncheck gestures.
//
// The locators themselves live on CalendarUiEnhancementsPage so this spec and
// `r/calendar-ui-enhancements.spec.ts` cannot drift apart; the page object is
// a thin wrapper over `page`, so constructing one per call is free.
function ui(page: import('@playwright/test').Page): CalendarUiEnhancementsPage {
  return new CalendarUiEnhancementsPage(page);
}

async function openBoardMenu(page: import('@playwright/test').Page) {
  await ui(page).openBoardMenu();
}

// NB: Escape reaches the TOP-MOST overlay only, so it closes a row's `⋮`
// actions menu before the panel behind it. No helper here opens that menu.
async function closeBoardMenu(page: import('@playwright/test').Page) {
  await ui(page).closeBoardMenu();
}

function boardItem(page: import('@playwright/test').Page, name: string) {
  return ui(page).boardItem(name);
}

// #1210 turned the create dialog into a shared create/EDIT dialog, so its
// field and its primary button are matched by id rather than by
// `formcontrolname` / `.btn-primary`: the same button reads "Opret" here and
// "Gem" when the dialog is opened from a row's Rediger action.
async function createBoard(page: import('@playwright/test').Page, name: string) {
  await ui(page).createBoard(name);
}

async function activateBoard(page: import('@playwright/test').Page, name: string) {
  await openBoardMenu(page);
  await boardItem(page, name).locator('.board-name').click();
  await expect(boardItem(page, name).locator('.board-checkbox.active')).toBeVisible({ timeout: API_TIMEOUT });
}

async function deactivateBoard(page: import('@playwright/test').Page, name: string) {
  await openBoardMenu(page);
  await boardItem(page, name).locator('.board-name').click();
  await expect(boardItem(page, name).locator('.board-checkbox.active')).toHaveCount(0, { timeout: API_TIMEOUT });
}

// The mtx-select displays the selected board's name as its value label.
async function selectedBoardLabel(page: import('@playwright/test').Page): Promise<string> {
  return (await page.locator('#calendarEventBoard .mtx-select__value, #calendarEventBoard .ng-value-label')
    .first().innerText()).trim();
}

// --- tests ----------------------------------------------------------------

test.describe.serial('Calendar new-task default board', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
  });

  test('seed: property + two boards, activate A then B', async ({ page }) => {
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.selectProperty(property.name as string);

    await createBoard(page, boardA);
    await createBoard(page, boardB);

    // Activate A first, then B — both stay checked, B is the last activated.
    await activateBoard(page, boardA);
    await activateBoard(page, boardB);
    await expect(boardItem(page, boardA).locator('.board-checkbox.active')).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(boardItem(page, boardB).locator('.board-checkbox.active')).toBeVisible({ timeout: UI_TIMEOUT });
    await closeBoardMenu(page);
  });

  test('create modal defaults to the last-activated board (B), not the lowest-id board (A)', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.selectProperty(property.name as string);

    // Re-activate A then B (a fresh page load reset the in-memory filter).
    await activateBoard(page, boardA);
    await activateBoard(page, boardB);
    // The dropdown stays open across check gestures and its overlay backdrop
    // would swallow the grid click below, so close it before touching the grid.
    await closeBoardMenu(page);

    // Advance to next week so the clicked slot is in the future (a current-week
    // morning slot is in the past once CI runs after that hour) and reset the
    // grid's scrollToNow auto-scroll, so the create modal reliably opens.
    await calendarPage.openCreateModalAtSlot(1, 9);
    await expect(page.locator('#calendarEventTitle')).toBeVisible({ timeout: UI_TIMEOUT });

    expect(await selectedBoardLabel(page)).toBe(boardB);
  });

  test('falls back when the last-activated board is deactivated', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.selectProperty(property.name as string);

    await activateBoard(page, boardA);
    await activateBoard(page, boardB);
    await deactivateBoard(page, boardB); // B (the last-activated) is no longer active
    await closeBoardMenu(page);

    await calendarPage.openCreateModalAtSlot(1, 9); // next-week future slot
    await expect(page.locator('#calendarEventTitle')).toBeVisible({ timeout: UI_TIMEOUT });

    // The guard drops the no-longer-active last-activated board: the modal must
    // NOT default to B. It falls back to the existing behavior (the lowest-id
    // board — here the property's auto-created "Default" board, which stays
    // active alongside A). The board select is [clearable]="false", so a real
    // board is always shown — assert it's non-empty and specifically not B.
    const label = await selectedBoardLabel(page);
    expect(label.length).toBeGreaterThan(0);
    expect(label).not.toBe(boardB);
  });
});
