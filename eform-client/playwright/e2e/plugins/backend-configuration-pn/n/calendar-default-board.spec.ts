import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';

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

async function createBoard(page: import('@playwright/test').Page, name: string) {
  // The "Create board" sidebar link renders as "Opret tavle" in the Danish e2e
  // locale (key 'Create board' → da 'Opret tavle'); it's the only board link.
  await page.locator('a.sidebar-action-link', { hasText: 'Opret tavle' }).click();
  const dialog = page.locator('mat-dialog-container');
  await dialog.locator('input[formcontrolname="name"]').fill(name);
  // The board-create dialog has a single primary button — match by class, not
  // by localized text, so key/translation changes don't break this.
  await dialog.locator('button.btn-primary').click();
  await dialog.waitFor({ state: 'detached', timeout: 10000 });
  await expect(page.locator('.board-list .board-name', { hasText: name })).toBeVisible({ timeout: 10000 });
}

function boardItem(page: import('@playwright/test').Page, name: string) {
  return page.locator('.board-item', { has: page.locator('.board-name', { hasText: name }) });
}

async function activateBoard(page: import('@playwright/test').Page, name: string) {
  await boardItem(page, name).locator('.board-name').click();
  await expect(boardItem(page, name).locator('.board-checkbox.active')).toBeVisible({ timeout: 5000 });
}

async function deactivateBoard(page: import('@playwright/test').Page, name: string) {
  await boardItem(page, name).locator('.board-name').click();
  await expect(boardItem(page, name).locator('.board-checkbox.active')).toHaveCount(0, { timeout: 5000 });
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
    await expect(boardItem(page, boardA).locator('.board-checkbox.active')).toBeVisible();
    await expect(boardItem(page, boardB).locator('.board-checkbox.active')).toBeVisible();
  });

  test('create modal defaults to the last-activated board (B), not the lowest-id board (A)', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.selectProperty(property.name as string);

    // Re-activate A then B (a fresh page load reset the in-memory filter).
    await activateBoard(page, boardA);
    await activateBoard(page, boardB);

    // Advance to next week so the clicked slot is in the future (a current-week
    // morning slot is in the past once CI runs after that hour) and reset the
    // grid's scrollToNow auto-scroll, so the create modal reliably opens.
    await calendarPage.openCreateModalAtSlot(1, 9);
    await expect(page.locator('#calendarEventTitle')).toBeVisible({ timeout: 10000 });

    expect(await selectedBoardLabel(page)).toBe(boardB);
  });

  test('falls back when the last-activated board is deactivated', async ({ page }) => {
    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.selectProperty(property.name as string);

    await activateBoard(page, boardA);
    await activateBoard(page, boardB);
    await deactivateBoard(page, boardB); // B (the last-activated) is no longer active

    await calendarPage.openCreateModalAtSlot(1, 9); // next-week future slot
    await expect(page.locator('#calendarEventTitle')).toBeVisible({ timeout: 10000 });

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
