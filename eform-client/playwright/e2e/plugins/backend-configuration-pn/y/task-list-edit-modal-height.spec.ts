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
import { fillInDialog } from '../dialog-helpers';
import { API_TIMEOUT, UI_TIMEOUT, ignoreUnhandledRejections, waitForApiResponse } from '../wait-helpers';

/**
 * Task list EDIT MODAL HEIGHT suite (shard y).
 *
 * The task list opens the calendar's `TaskCreateEditModalComponent` as a
 * MatDialog. Only the calendar's popover mode used to cap the modal's height
 * and give `.gcal-scroll-body` room to scroll; as a dialog it grew with its
 * content, so on a laptop-sized screen the overlay pane ran past the bottom
 * of the viewport and the Save button in `.gcal-actions` could not be
 * reached (the overlay is fixed, so the page cannot be scrolled to it).
 *
 * EH1 shrinks the viewport, opens the edit modal from the task list with the
 * eForm details expanded (the tallest state of the form), and asserts that
 * the dialog surface fits inside the viewport, that Save is fully in view,
 * and that a save made through it persists.
 *
 * Seed: one property + one worker + one calendar-created task (eForm and
 * report headline set, no repeat — so Save needs no scope dialog).
 */

const property: PropertyCreateUpdate = {
  name: `teh-${generateRandmString(5)}`,
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

const task = `teh-task-${generateRandmString(6)}`;
const editedTask = `${task}-ed`;

// A laptop screen, well below the modal's natural height with every row shown.
const SMALL_VIEWPORT = { width: 1280, height: 600 };

test.describe.serial('Task list — edit modal fits the viewport', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    await page.waitForTimeout(1500);
  });

  test.afterAll(async ({ browser }) => {
    // browser.newPage() can itself reject — a browser that crashed or got
    // disconnected during a long run — and an exception thrown here escapes the
    // hook and fails the job, which is exactly what this non-fatal teardown
    // exists to prevent. Record it and give up on cleanup instead.
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

      // Arm the list loads before navigating, so clearTable() counts the rows
      // only once the grid has its data (an empty list after a partial seed
      // still resolves both waits).
      const workersPage = new BackendConfigurationPropertyWorkersPage(page);
      const deviceUsers = waitForApiResponse(
        page,
        'POST /api/backend-configuration-pn/properties/assignment/index-device-user (device user list)',
        r => r.url().includes('/api/backend-configuration-pn/properties/assignment/index-device-user')
          && r.request().method() === 'POST',
        API_TIMEOUT,
      );
      const assignments = waitForApiResponse(
        page,
        'GET /api/backend-configuration-pn/properties/assignment (worker property assignments)',
        r => r.url().includes('/api/backend-configuration-pn/properties/assignment')
          && r.request().method() === 'GET',
        API_TIMEOUT,
      );
      ignoreUnhandledRejections(deviceUsers, assignments);
      await workersPage.goToPropertyWorkers();
      await Promise.all([deviceUsers, assignments]);
      await workersPage.newDeviceUserBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
      await workersPage.clearTable();

      const propertiesPage = new BackendConfigurationPropertiesPage(page);
      const properties = waitForApiResponse(
        page,
        'POST /api/backend-configuration-pn/properties/index (property list)',
        r => r.url().includes('/api/backend-configuration-pn/properties/index')
          && r.request().method() === 'POST',
        API_TIMEOUT,
      );
      ignoreUnhandledRejections(properties);
      await propertiesPage.goToProperties();
      await properties;
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

  test('seed: create property + worker + task', async ({ page }) => {
    // Property + worker creation go through the SDK and are the slow part.
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
    await calendarPage.openCreateModalAtSlot(0, 9);
    await calendarPage.fillAndSaveEvent(task);
  });

  test('EH1: on a small screen the edit modal fits the viewport and Save works', async ({ page }) => {
    await page.setViewportSize(SMALL_VIEWPORT);

    const taskListPage = new TaskListPage(page);
    await taskListPage.goto();
    await taskListPage.selectProperty(property.name);
    await expect(taskListPage.row(task)).toBeVisible({ timeout: UI_TIMEOUT });

    await taskListPage.openEditModal(task);
    await expect(page.locator('#calendarEventTitle')).toHaveValue(task, { timeout: UI_TIMEOUT });

    // The tallest state of the form: report headline on (seeded) and the
    // eForm's field list expanded.
    await expect(page.locator('#calendarEventPlanningTag')).toBeVisible({ timeout: UI_TIMEOUT });
    const eformDetailsToggle = page.locator('#calendarEformPreviewToggle');
    await expect(eformDetailsToggle).toBeVisible({ timeout: UI_TIMEOUT });
    await eformDetailsToggle.click();
    await expect(eformDetailsToggle).toHaveAttribute('aria-expanded', 'true', { timeout: UI_TIMEOUT });
    await expect(page.locator('#calendarEformPreviewBody')).toBeVisible({ timeout: UI_TIMEOUT });

    const surface = page.locator('mat-dialog-container .mat-mdc-dialog-surface');
    const box = await surface.boundingBox();
    expect(box, 'the dialog surface has no bounding box').not.toBeNull();
    expect(box!.y, 'the dialog surface starts above the viewport').toBeGreaterThanOrEqual(0);
    expect(box!.y + box!.height, 'the dialog surface runs past the bottom of the viewport')
      .toBeLessThanOrEqual(SMALL_VIEWPORT.height);

    // Pinned below the scrolling body, so it is in view without any scrolling.
    const saveBtn = page.locator('mat-dialog-container .gcal-actions #calendarEventSaveBtn');
    await expect(saveBtn).toBeInViewport({ ratio: 1, timeout: UI_TIMEOUT });

    await fillInDialog(page, page.locator('#calendarEventTitle'), editedTask);

    const putResp = waitForApiResponse(
      page,
      'PUT /api/backend-configuration-pn/calendar/tasks',
      r => r.url().endsWith('/api/backend-configuration-pn/calendar/tasks')
        && r.request().method() === 'PUT',
      API_TIMEOUT,
    );
    const reload = waitForApiResponse(
      page,
      'POST /api/backend-configuration-pn/calendar/tasks/index after the save',
      r => r.url().includes('/api/backend-configuration-pn/calendar/tasks/index'),
      API_TIMEOUT,
    );
    ignoreUnhandledRejections(putResp, reload);
    await saveBtn.click();

    const body = await (await putResp).json();
    expect(body.success, `updateTask returned success=false: ${body.message}`).toBe(true);
    await expect(page.locator('mat-dialog-container')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await reload;

    await expect(taskListPage.titleText(editedTask)).toBeVisible({ timeout: UI_TIMEOUT });
  });
});
