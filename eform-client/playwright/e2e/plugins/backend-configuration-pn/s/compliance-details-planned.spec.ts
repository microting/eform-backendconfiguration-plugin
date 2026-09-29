import { test, expect, Page, Response } from '@playwright/test';
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
import {
  API_TIMEOUT,
  UI_TIMEOUT,
  ignoreUnhandledRejections,
  waitForApiResponse,
} from '../wait-helpers';

/**
 * Compliance → Detaljer lists PLANNED occurrences (#1332).
 *
 * A future occurrence only gets a Compliance row once something deploys it (the
 * mobile app's ~30-day watch window, or the scheduler's current cycle), so with the
 * "År til dato + 1 år" period Detaljer used to stop about a month ahead. It now
 * projects the rest of the period from the task's rule and shows those rows
 * read-only with the status "Planlagt".
 *
 * Seeded through the UI, like every other `s/` spec (this shard seeds no SQL): a
 * property, a worker, and a WEEKLY task created on next week's Monday. No device ever
 * syncs in CI, so every occurrence after the scheduler's first cycle is planned — the
 * test looks at the first Monday at least 61 days ahead, far past any watch window.
 *
 * The date/filter rules themselves (dedup against compliance rows, occurrence
 * exceptions, repeat end, filters, row cap) are pinned server-side by
 * `ComplianceReportProjectionTests`; the read-only row by the Jest spec of
 * `compliance-details-view`. What only a browser can show is asserted here: the row
 * is really there on screen, says "Planlagt", and offers no action.
 */
const BASE_URL = 'http://localhost:4200';
const PAGE_URL = `${BASE_URL}/plugins/backend-configuration-pn/compliance-report`;
const INDEX_URL = '/api/backend-configuration-pn/compliance-report/index';

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

const taskTitle = `Planned task ${generateRandmString(6)}`;

let seeded = false;

function isoDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** The first Monday at least `days` days after today. */
function firstMondayAtLeastDaysAhead(days: number): string {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  d.setDate(d.getDate() + days);
  while (d.getDay() !== 1) {
    d.setDate(d.getDate() + 1);
  }
  return isoDate(d);
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** Picks an mtx-select option BY LABEL, never by index. */
async function selectOptionByLabel(page: Page, selectId: string, label: string): Promise<void> {
  await page.locator(`#${selectId}`).click({ timeout: UI_TIMEOUT });
  await page
    .locator('.ng-dropdown-panel .ng-option', { hasText: new RegExp(`^\\s*${escapeRegExp(label)}\\s*$`) })
    .first()
    .click({ timeout: UI_TIMEOUT });
}

function isIndexPost(r: Response): boolean {
  return r.url().includes(INDEX_URL) && r.request().method() === 'POST';
}

/**
 * Runs one gesture and waits for the Detaljer fetch it causes. Filter changes are
 * debounced, so the waiter is armed BEFORE the gesture and awaited by response,
 * never counted.
 */
async function withIndexResponse(page: Page, description: string, gesture: () => Promise<void>): Promise<Response> {
  const response = waitForApiResponse(page, `compliance index after ${description}`, isIndexPost, API_TIMEOUT);
  ignoreUnhandledRejections(response);
  await gesture();
  const answered = await response;
  expect(answered.ok(), `compliance index after ${description} must succeed`).toBeTruthy();
  return answered;
}

test.describe.serial('Compliance Detaljer — planned occurrences (#1332)', () => {
  /**
   * Guarded teardown, mirroring `b/task-list-inline-rename.spec.ts`: each phase runs
   * against one shared budget, a phase that stalls is REPORTED by name instead of the
   * race quietly resolving as success, and a final phase verifies nothing is left for
   * the next spec in this shard. Never throws — cleanup must not fail the run.
   */
  test.afterAll(async ({ browser }) => {
    const CLEANUP_BUDGET_MS = 60000;
    const deadline = Date.now() + CLEANUP_BUDGET_MS;
    const problems: string[] = [];
    let aborted = false;

    // Never throws. Returns whether the phase actually completed.
    const phase = async (label: string, fn: () => Promise<void>): Promise<boolean> => {
      if (aborted) {
        problems.push(`${label}: skipped, an earlier phase did not complete`);
        return false;
      }
      const budget = deadline - Date.now();
      if (budget <= 0) {
        problems.push(`${label}: skipped, cleanup budget exhausted`);
        aborted = true;
        return false;
      }
      let timer: ReturnType<typeof setTimeout> | undefined;
      try {
        const outcome = await Promise.race([
          fn().then(() => 'done' as const),
          new Promise<'timeout'>(resolve => {
            timer = setTimeout(() => resolve('timeout'), budget);
          }),
        ]);
        if (outcome === 'timeout') {
          aborted = true;
          problems.push(`${label}: timed out after ${budget}ms`);
          return false;
        }
        return true;
      } catch (err: any) {
        problems.push(`${label}: ${err?.message ?? err}`);
        return false;
      } finally {
        if (timer) {
          clearTimeout(timer);
        }
      }
    };

    const page = await browser.newPage().catch((err: any) => {
      problems.push(`cleanup harness: browser.newPage() failed: ${err?.message ?? err}`);
      return undefined;
    });
    try {
      if (!page) {
        return;
      }
      const workersPage = new BackendConfigurationPropertyWorkersPage(page);
      const propertiesPage = new BackendConfigurationPropertiesPage(page);

      const loggedIn = await phase('login', async () => {
        await page.goto(BASE_URL);
        await new LoginPage(page).login();
      });
      if (!loggedIn) {
        // Nothing below can work unauthenticated.
        aborted = true;
      }

      // Workers first: the property cannot go while a worker is assigned to it. The
      // grid host is awaited so clearTable() never counts an unrendered table as empty
      // — and the verify phase reports anything it still missed.
      await phase('clear workers', async () => {
        await workersPage.goToPropertyWorkers();
        await workersPage.newDeviceUserBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
        await workersPage.clearTable();
      });

      await phase('clear properties', async () => {
        await propertiesPage.goToProperties();
        await page.locator('app-properties-table').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
        await propertiesPage.clearTable();
      });

      await phase('verify', async () => {
        await propertiesPage.goToProperties();
        await page.locator('app-properties-table').waitFor({ state: 'visible', timeout: UI_TIMEOUT });
        const propertiesLeft = await page.locator('app-properties-table .mat-mdc-row').count();
        if (propertiesLeft > 0) {
          problems.push(`verify: ${propertiesLeft} property row(s) still present`);
        }
        await workersPage.goToPropertyWorkers();
        await workersPage.newDeviceUserBtn().waitFor({ state: 'visible', timeout: UI_TIMEOUT });
        const workersLeft = await workersPage.rowNum();
        if (workersLeft > 0) {
          problems.push(`verify: ${workersLeft} worker row(s) still present`);
        }
      });
    } catch (err: any) {
      problems.push(`cleanup harness: ${err?.message ?? err}`);
    } finally {
      if (problems.length > 0) {
        console.log(
          '[compliance-details-planned] afterAll cleanup INCOMPLETE (non-fatal) — ' +
          `may have left property "${property.name}" / worker ` +
          `"${worker.name} ${worker.surname}" for the next spec in this shard: ` +
          problems.join(' | '),
        );
      }
      if (page) {
        try { await page.close(); } catch {}
      }
    }
  });

  test('seed: property, worker and a weekly task', async ({ page }) => {
    // Property + worker creation provisions through the SDK (several minutes on a
    // cold CI box, as in the other `s/` seed tests); the task itself is one POST.
    test.setTimeout(600000);

    await page.goto(BASE_URL);
    await new LoginPage(page).login();

    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    const workersPage = new BackendConfigurationPropertyWorkersPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
    await workersPage.goToPropertyWorkers();
    await workersPage.create(worker);

    const calendarPage = new CalendarUiEnhancementsPage(page);
    await calendarPage.goToCalendar();
    await calendarPage.selectProperty(property.name);
    // Next week's Monday 09:00, repeating weekly with no end.
    await calendarPage.openCreateModalAtSlot(0, 9);
    await calendarPage.selectRepeatPreset('weeklyOne');
    await calendarPage.fillAndSaveEvent(taskTitle);

    seeded = true;
  });

  test('a weekly occurrence two months ahead is listed as "Planlagt" with no actions', async ({ page }) => {
    expect(seeded, 'the seed test must have completed').toBe(true);
    // Login + five sequential Detaljer fetches, each bounded by API_TIMEOUT.
    test.setTimeout(300000);

    await page.goto(BASE_URL);
    await new LoginPage(page).login();
    await page.goto(PAGE_URL);
    await page.locator('#complianceFilterProperty').waitFor({ state: 'visible', timeout: 60000 });

    await withIndexResponse(page, 'switching to Detaljer', () =>
      page.locator('#complianceMode-details').click({ timeout: UI_TIMEOUT }));
    await withIndexResponse(page, 'choosing the property', () =>
      selectOptionByLabel(page, 'complianceFilterProperty', property.name));
    await withIndexResponse(page, 'choosing "År til dato + 1 år"', () =>
      selectOptionByLabel(page, 'complianceFilterPeriod', 'År til dato + 1 år'));
    await withIndexResponse(page, 'choosing "Alle opgaver"', () =>
      selectOptionByLabel(page, 'complianceFilterStatus', 'Alle opgaver'));
    const showAll = await withIndexResponse(page, '"Vis alle"', () =>
      page.locator('#compliancePageShowAll').click({ timeout: UI_TIMEOUT }));

    const body = showAll.request().postDataJSON();
    expect(body.pageSize, '"Vis alle" asks for the unpaged list').toBe(0);
    expect(body.includeProjected, 'Detaljer asks for the planned occurrences').toBe(true);

    const targetDate = firstMondayAtLeastDaysAhead(61);
    const model = (await showAll.json())?.model;
    const plannedFromApi = (model?.entities ?? []).filter(
      (e: any) => e.isProjected && e.title === taskTitle && e.taskDate === targetDate);
    expect(plannedFromApi, `the API lists ${taskTitle} on ${targetDate} as planned`).toHaveLength(1);
    expect(plannedFromApi[0].complianceId).toBe(0);
    expect(plannedFromApi[0].completed).toBe(false);

    const row = page
      .locator('app-compliance-details-view tr.compliance-details__row[data-planned]')
      .filter({ hasText: taskTitle })
      .and(page.locator(`[data-task-date="${targetDate}"]`));
    await expect(row).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(row.locator('.compliance-details__planned')).toHaveText(/^\s*Planlagt\s*$/, { timeout: UI_TIMEOUT });
    // Read-only: no delete action, not a click target, not focusable.
    await expect(row.locator('.compliance-details__delete')).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(row).not.toHaveClass(/is-clickable/, { timeout: UI_TIMEOUT });
    await expect(row).not.toHaveAttribute('tabindex', /.*/, { timeout: UI_TIMEOUT });
    await expect(row).not.toHaveAttribute('data-compliance-id', /.*/, { timeout: UI_TIMEOUT });
  });
});
