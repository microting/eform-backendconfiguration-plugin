import { test, expect } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { CalendarUiEnhancementsPage } from '../calendar-ui-enhancements.page';
import { TaskListPage } from '../task-list.page';
import {
  BackendConfigurationPropertiesPage,
  PropertyCreateUpdate,
} from '../BackendConfigurationProperties.page';
import {
  BackendConfigurationPropertyWorkersPage,
  PropertyWorker,
} from '../BackendConfigurationPropertyWorkers.page';

/**
 * Task list — batch CHANGE START DATE re-derives the "Nth weekday" ordinal
 * (#1289, shard i).
 *
 * The customer: a task "monthly on the 2nd Thursday" moved with "Skift
 * startdato" to Monday 7 Sept became "monthly on the 2nd Monday" — the weekday
 * followed the new date but the ordinal did not, although 7 Sept is the FIRST
 * Monday.
 *
 * OR1: a monthly "Nth weekday" task is created on a day >= 8 of its month, so
 *      its ordinal is 2..5 (the premise is asserted on the grid's Gentag cell,
 *      otherwise the test would pass vacuously on the old code). The batch
 *      action then re-anchors it to the 1st of a past month — always the 1st
 *      occurrence of its weekday — and the Gentag cell must read "1." (da) /
 *      "1st" (en) followed by THAT date's weekday.
 *
 * The ordinal is matched with a whitespace-tolerant regex and the weekday name
 * case-insensitively in either Danish or English, so the assertion does not
 * depend on the admin's UI language. Dates are computed from the real clock;
 * nothing is hard-coded.
 */

const property: PropertyCreateUpdate = {
  name: `tso-${generateRandmString(5)}`,
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

const rand = generateRandmString(6);
const task = `ord-task-${rand}`;

// See i/task-list-batch-start-date.spec.ts for why the dropdown entry is the
// one place matched by (Danish) label.
const LABEL_CHANGE_START_DATE = 'Skift startdato';
const MONTHS_BACK = 2;

/**
 * The next week the calendar can create in (openCreateModalAtSlot always
 * advances one week) that contains a day >= 8 of its month, and the Mon-based
 * day offset of that day. Only a week that starts on the 1st has no such day;
 * then the week after is used.
 */
function pickSlot(): { extraWeeks: number; dayOffset: number; date: Date } {
  const today = new Date();
  const monday = new Date(today.getFullYear(), today.getMonth(), today.getDate() - ((today.getDay() + 6) % 7));
  for (let extraWeeks = 0; extraWeeks < 2; extraWeeks++) {
    for (let dayOffset = 0; dayOffset < 7; dayOffset++) {
      const date = new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + 7 * (1 + extraWeeks) + dayOffset);
      if (date.getDate() >= 8) {
        return { extraWeeks, dayOffset, date };
      }
    }
  }
  throw new Error('unreachable: two consecutive weeks always contain a day >= 8');
}

function weekdayNames(date: Date): string[] {
  return [
    date.toLocaleDateString('da-DK', { weekday: 'long' }),
    date.toLocaleDateString('en-GB', { weekday: 'long' }),
  ];
}

let seeded = false;

test.describe.serial('Task list — batch change start date re-derives the ordinal', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(1500);
  });

  test.afterAll(async ({ browser }) => {
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
      await page.waitForTimeout(1000);
      await workersPage.clearTable();

      const propertiesPage = new BackendConfigurationPropertiesPage(page);
      await propertiesPage.goToProperties();
      await page.waitForTimeout(1000);
      await propertiesPage.clearTable();
    };
    try {
      await Promise.race([
        cleanup(),
        new Promise(resolve => setTimeout(resolve, 60000)),
      ]);
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    }
    try { await page.close(); } catch {}
  });

  test('seed: property + worker + a monthly "Nth weekday" task with N >= 2', async ({ page }) => {
    test.setTimeout(600000);

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);

    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.selectProperty(property.name);
    await page.waitForTimeout(1000);

    const slot = pickSlot();
    for (let i = 0; i < slot.extraWeeks; i++) {
      await calendarPage.navigateToNextWeek();
    }
    await calendarPage.openCreateModalAtSlot(slot.dayOffset, 9);
    // "Monthly on the Nth <weekday>" — N = ceil(day / 7) of the slot's date.
    await calendarPage.selectRepeatPreset('monthlyByDay');
    await calendarPage.fillAndSaveEvent(task);

    seeded = true;
  });

  test('OR1: re-anchoring to the 1st of a past month makes the Gentag rule "1." + that weekday', async ({ page }) => {
    expect(seeded).toBe(true);
    const taskListPage = new TaskListPage(page);

    await taskListPage.goto();
    await taskListPage.search(rand);
    await expect(taskListPage.row(task)).toBeVisible();

    // Premise: the seeded rule is NOT already "1." — otherwise a stale ordinal
    // would be indistinguishable from a re-derived one.
    const repeatCell = taskListPage.columnCell(task, 'repeat');
    await expect(repeatCell).toHaveText(/(^|\s)[2-5](\.|nd|rd|th)\s/);

    await taskListPage.selectRow(task);
    await taskListPage.pickBatchAction(new RegExp(LABEL_CHANGE_START_DATE));
    const expectedDate = await taskListPage.pickPastStartDate(MONTHS_BACK);
    await taskListPage.waitForStartDatePreviewResolved();
    await expect(taskListPage.batchModalSubmitButton()).toBeEnabled();
    await taskListPage.submitModal();
    await taskListPage.waitForModalClosed();

    await expect(taskListPage.row(task)).toBeVisible();
    await expect(taskListPage.columnCell(task, 'taskDate')).toHaveText(expectedDate);

    const now = new Date();
    const picked = new Date(now.getFullYear(), now.getMonth() - MONTHS_BACK, 1);
    const [da, en] = weekdayNames(picked);
    await expect(repeatCell).toHaveText(/(^|\s)1(\.|st)\s/);
    await expect(repeatCell).toHaveText(new RegExp(`(${da}|${en})`, 'i'));
    await expect(repeatCell).not.toHaveText(/(^|\s)[2-5](\.|nd|rd|th)\s/);
  });
});
