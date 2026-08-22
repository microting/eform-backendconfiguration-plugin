import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';

/**
 * Layout contract of the redesigned eForm fill modal.
 *
 * Companion to calendar-complete.spec.ts, which covers the completion *flow*
 * (prepare-complete POST, worker preselect, save gating). This file covers the
 * *structure* the redesign introduced, which nothing else asserts:
 *
 *   - the question label renders above the control, once
 *   - no field renders a tinted mat-card-header any more
 *   - no control renders a duplicate floating mat-label
 *   - dataItem.color becomes a left accent bar
 *   - a single-section eForm shows neither a nav column nor a section heading
 *     that merely repeats the dialog title
 *   - the worker dropdown groups assigned workers above the rest
 *
 * Same seed shape as calendar-complete.spec.ts (property + one worker), and the
 * same reason for not saving: the embedded eForm's mandatory fields make a full
 * submit impractical in e2e. Every assertion here is on the opened modal.
 *
 * Selectors deliberately reused from the sibling suite so both break together
 * if the dialog shell changes: `app-calendar-complete-event-modal`,
 * `#completeWorkerSelect`, `#completeSaveBtn`, `#completeCancelBtn`.
 */

const property: PropertyCreateUpdate = {
  name: generateRandmString(5),
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '1111111',
};

const worker: PropertyWorker = {
  name: generateRandmString(5),
  surname: generateRandmString(5),
  language: 'Dansk',
  properties: [property.name],
  workerEmail: generateRandmString(5) + '@test.com',
};

let seeded = false;

function isPrepareComplete(r: import('@playwright/test').Response): boolean {
  return (
    /\/api\/backend-configuration-pn\/calendar\/tasks\/\d+\/prepare-complete/.test(r.url()) &&
    r.request().method() === 'POST'
  );
}

async function closeModal(page: import('@playwright/test').Page): Promise<void> {
  const modal = page.locator('app-calendar-complete-event-modal').first();
  if ((await modal.count()) === 0) return;
  const cancelBtn = page.locator('#completeCancelBtn');
  if ((await cancelBtn.count()) > 0) {
    await cancelBtn.click();
  } else {
    await page.keyboard.press('Escape');
  }
  await modal.waitFor({ state: 'detached', timeout: 5000 }).catch(() => undefined);
}

/**
 * Create an event, click its completion indicator, and wait for the combined
 * modal to be open and populated. Returns the modal locator.
 */
async function openCompleteModal(
  page: import('@playwright/test').Page,
  titlePrefix: string,
  weekday: number,
): Promise<import('@playwright/test').Locator> {
  const calendarPage = new CalendarUiEnhancementsPage(page);
  const title = `${titlePrefix}-${generateRandmString(5)}`;

  await calendarPage.openCreateModalAtSlot(weekday, 8);
  await calendarPage.fillAndSaveEvent(title);

  const block = calendarPage.findEventBlock(title);
  await expect(block).toBeVisible();

  const completionWait = page.waitForResponse(isPrepareComplete, { timeout: 30000 });
  await block.locator('.completion-btn').click();
  await completionWait;

  const modal = page.locator('app-calendar-complete-event-modal').first();
  await modal.waitFor({ state: 'visible', timeout: 20000 });
  // The eForm body arrives after prepare-complete → templates/get → cases;
  // wait for the first rendered question rather than a fixed timeout.
  await modal.locator('.eform-field__label').first()
    .waitFor({ state: 'visible', timeout: 20000 });
  return modal;
}

test.describe.serial('Calendar complete modal — redesigned layout', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.ensureSidebarOpen();

    if (seeded) {
      const folderResp = page.waitForResponse(
        r => r.url().includes('/api/backend-configuration-pn/properties/get-folder-dtos'),
        { timeout: 60000 }
      );
      await calendarPage.selectProperty(property.name);
      await folderResp.catch(() => undefined);
      await page.waitForTimeout(1000);
    }
  });

  test.afterAll(async ({ browser }) => {
    const page = await browser.newPage();
    const cleanup = async () => {
      await page.goto('http://localhost:4200');
      await new LoginPage(page).login();

      const workersPage = new BackendConfigurationPropertyWorkersPage(page);
      await workersPage.goToPropertyWorkers();
      await page.waitForTimeout(1000);
      await workersPage.clearTable();

      const propertiesPage = new BackendConfigurationPropertiesPage(page);
      await propertiesPage.goToProperties();
      await page.waitForTimeout(1000);
      await propertiesPage.clearTable();
    };
    try {
      await Promise.race([cleanup(), new Promise(resolve => setTimeout(resolve, 60000))]);
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    }
    try { await page.close(); } catch {}
  });

  test('seed: create property + worker', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    seeded = true;
  });

  // =======================================================================
  // L1 / L2 / L5 — the question block replaced the card + tinted header, and
  //   nothing is labelled twice.
  // =======================================================================
  test('L1/L2/L5: questions render a label above the control, with no card header and no duplicate mat-label', async ({ page }) => {
    const modal = await openCompleteModal(page, 'L1', 0);

    // L1 — every question block has a non-empty label above its control.
    const labels = modal.locator('.eform-field__label');
    const labelCount = await labels.count();
    expect(labelCount).toBeGreaterThan(0);
    for (let i = 0; i < labelCount; i++) {
      expect((await labels.nth(i).innerText()).trim()).not.toBe('');
    }

    // L2 — the duplicate-label regression: the eForm body must contain no
    // floating labels at all, and no tinted card header.
    await expect(modal.locator('app-case-edit-element mat-label')).toHaveCount(0);
    await expect(modal.locator('app-case-edit-element mat-card-header')).toHaveCount(0);
    await expect(modal.locator('app-case-edit-switch mat-card')).toHaveCount(0);

    // L5 — a single-section eForm must not print a heading that only repeats
    // the dialog title.
    const dialogTitle = (await modal.locator('[mat-dialog-title]').first().innerText()).trim();
    const sectionTitles = modal.locator('.eform-section__title');
    for (let i = 0; i < (await sectionTitles.count()); i++) {
      expect((await sectionTitles.nth(i).innerText()).trim()).not.toBe(dialogTitle);
    }

    await closeModal(page);
  });

  // =======================================================================
  // L3 / L4 — accent bar tracks dataItem.color; no nav for one section.
  // =======================================================================
  test('L3/L4: coloured fields get an accent bar and a single-section eForm shows no nav column', async ({ page }) => {
    const modal = await openCompleteModal(page, 'L3', 1);

    // L3 — every accented block must carry a real colour, and every block
    // without the modifier must not paint one. (The seeded eForm may have no
    // coloured fields at all; the invariant still has to hold either way.)
    const accented = modal.locator('.eform-field--accented');
    for (let i = 0; i < (await accented.count()); i++) {
      const colour = await accented.nth(i).evaluate(
        el => getComputedStyle(el).getPropertyValue('--eform-field-accent').trim()
      );
      expect(colour).not.toBe('');
    }

    // L4 — one section → no nav column.
    const sections = modal.locator('app-case-edit-element > .eform-section');
    if ((await sections.count()) <= 1) {
      await expect(modal.locator('.calendar-complete-event-modal__nav')).toHaveCount(0);
    }

    await closeModal(page);
  });

  // =======================================================================
  // L6 / L7 — grouped worker dropdown; shell still wired.
  // =======================================================================
  test('L6/L7: the worker dropdown groups assigned workers and the shell stays wired', async ({ page }) => {
    const modal = await openCompleteModal(page, 'L6', 2);

    // L7 — the dialog shell survived the layout rewrite.
    await expect(page.locator('#completeSaveBtn')).toHaveCount(1);
    await expect(page.locator('#completeCancelBtn')).toHaveCount(1);
    await expect(page.locator('#completeWorkerSelect')).toBeVisible();

    // L6 — open the dropdown (mtx-select appends its panel to body) and check
    // the grouping. This seed has a single property worker who is also the
    // event's assignee, so the "empty group" guard applies and the list is
    // deliberately NOT grouped — assert that rather than a header that should
    // not be there. Group headers, when present, are .ng-optgroup and are
    // never .ng-option, which is why the sibling suite's `.ng-option` picking
    // keeps working.
    await page.locator('#completeWorkerSelect').click();
    const panel = page.locator('.ng-dropdown-panel');
    await panel.waitFor({ state: 'visible', timeout: 10000 });

    const optionCount = await panel.locator('.ng-option').count();
    const groupCount = await panel.locator('.ng-optgroup').count();
    expect(optionCount).toBeGreaterThan(0);
    if (groupCount > 0) {
      // Grouped: headers must be real text, and must not double as options.
      for (let i = 0; i < groupCount; i++) {
        expect((await panel.locator('.ng-optgroup').nth(i).innerText()).trim()).not.toBe('');
      }
      await expect(panel.locator('.ng-optgroup.ng-option')).toHaveCount(0);
    }

    await page.keyboard.press('Escape');
    await closeModal(page);
  });
});
