import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import {
  ignoreUnhandledRejections,
  SLOW_API_TIMEOUT,
  UI_TIMEOUT,
  waitForApiResponse,
} from '../wait-helpers';

/**
 * Compliance — completed cases of "no repeat" (always-available) tasks (#5467).
 *
 * Such a task gets no Compliance row, so the server returns its completed cases with
 * `complianceId` 0, `isOnDemand` true and their own `sdkCaseId`. Several of them share
 * `complianceId` 0, which used to collide in both views' trackBy (Angular throws on a
 * duplicate key) and would have sent Rapport's `Slet` to `compliances/delete/0`.
 *
 * Both responses are MOCKED, like `compliance-report-view.spec.ts`: shard `s` seeds no
 * SQL, and a completed on-demand case needs a device submission no browser path can
 * make. What the server returns is pinned by the backend's integration tests; what only
 * a browser can prove is asserted here — both rows render, Rapport keeps `Rediger` but
 * offers no `Slet` for them (while a normal log keeps it), and Detaljer shows them done
 * and read-only. All names are fictional.
 */
const BASE_URL = 'http://localhost:4200';
const PAGE_URL = `${BASE_URL}/plugins/backend-configuration-pn/compliance-report`;
const INDEX_ROUTE = '**/api/backend-configuration-pn/compliance-report/index';
const EFORM_COLUMNS_ROUTE = '**/api/backend-configuration-pn/compliance-report/eform-columns';

const WORKER = { id: 94501, name: 'Jane Doe' };
const PROPERTY = { id: 94601, name: 'Example Property' };

/** Two on-demand cases (no compliance) and one ordinary log, all completed. */
const LOGS = [
  { complianceId: 0, sdkCaseId: 7001, title: 'On demand A', isOnDemand: true },
  { complianceId: 0, sdkCaseId: 7002, title: 'On demand B', isOnDemand: true },
  { complianceId: 55, sdkCaseId: 7055, title: 'Regular log', isOnDemand: false },
];
const ON_DEMAND_LOGS = LOGS.filter((l) => l.isOnDemand);

function json(model: unknown) {
  return {
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ success: true, message: '', model }),
  };
}

async function routeRapport(page: Page): Promise<void> {
  const cases = LOGS.map((log) => ({
    complianceId: log.complianceId,
    sdkCaseId: log.sdkCaseId,
    isOnDemand: log.isOnDemand,
    checkListId: 509,
    tags: [],
    propertyId: PROPERTY.id,
    propertyName: PROPERTY.name,
    title: log.title,
    taskDate: '2026-05-13',
    completed: true,
    doneAt: '2026-05-13T10:00:00',
    workerNames: [WORKER.name],
    completedBySiteId: WORKER.id,
    areaRulePlanningId: 650,
    workerSiteIds: [WORKER.id],
    teamAssigneeIds: [],
    cells: { f12: log.title },
    // One fetchable image each, so every row renders a gallery button and its DOM id.
    imagesCount: 1,
    images: [
      {
        fieldValueId: log.sdkCaseId,
        uploadedDataId: log.sdkCaseId,
        fileName: `${log.sdkCaseId}_700_abc.jpg`,
        thumbnailFileName: `${log.sdkCaseId}_300_abc.jpg`,
        geoLink: null,
      },
    ],
  }));
  await page.route(EFORM_COLUMNS_ROUTE, (route) =>
    route.fulfill(
      json([
        {
          headlineTagId: 9,
          headlineName: 'Headline 9',
          tagsCaption: '',
          templates: [
            {
              checkListId: 509,
              checkListName: 'Flydelag',
              schemaUnavailable: false,
              columns: [{ key: 'f12', fieldId: 12, label: 'KOMMENTAR', fieldType: 'Comment' }],
              cases,
            },
          ],
        },
      ]),
    ),
  );
}

async function routeDetaljer(page: Page): Promise<void> {
  const entities = LOGS.map((log) => ({
    complianceId: log.complianceId,
    sdkCaseId: log.sdkCaseId,
    isOnDemand: log.isOnDemand,
    taskDate: '2026-05-13',
    startHour: 9,
    duration: 60,
    isAllDay: false,
    title: log.title,
    propertyId: PROPERTY.id,
    propertyName: PROPERTY.name,
    boardId: null,
    boardName: '',
    tags: [],
    workerNames: [WORKER.name],
    workerSiteIds: [WORKER.id],
    teamAssigneeIds: [],
    completed: true,
    doneAt: '2026-05-13T10:00:00',
    eformId: 509,
    planningId: 640,
    areaRulePlanningId: 650,
    checkListId: 509,
  }));
  await page.route(INDEX_ROUTE, (route) => route.fulfill(json({ total: entities.length, entities })));
}

async function openCompliancePage(page: Page): Promise<void> {
  await page.goto(BASE_URL);
  await new LoginPage(page).login();
  await page.goto(PAGE_URL);
  await page.locator('#complianceFilterProperty').waitFor({ state: 'visible', timeout: SLOW_API_TIMEOUT });
}

/** Switches mode and waits for the query the recreated child makes on its own. */
async function switchMode(page: Page, mode: 'report' | 'details', endpoint: string): Promise<void> {
  const response = waitForApiResponse(
    page,
    `the ${mode} ${endpoint} query`,
    (r) => r.url().includes(`/compliance-report/${endpoint}`),
    SLOW_API_TIMEOUT,
  );
  ignoreUnhandledRejections(response);
  await page.locator(`#complianceMode-${mode}`).click({ timeout: UI_TIMEOUT });
  await expect(page.locator(`#complianceMode-${mode}-button`)).toHaveAttribute('aria-checked', 'true', {
    timeout: UI_TIMEOUT,
  });
  await response;
  await expect(page.locator('#complianceCasesRoot')).toHaveAttribute('aria-busy', 'false', {
    timeout: SLOW_API_TIMEOUT,
  });
}

test.describe.configure({ mode: 'serial' });

test.describe('Compliance — on-demand (no repeat) completions (#5467)', () => {
  test('Rapport renders every on-demand row, with Rediger but without Slet', async ({ page }) => {
    await routeRapport(page);
    await openCompliancePage(page);
    await switchMode(page, 'report', 'eform-columns');

    const table = page.locator('.compliance-report__table[data-table-key="h9-c509"]');
    await expect(table.locator('tbody tr')).toHaveCount(LOGS.length, { timeout: UI_TIMEOUT });

    for (const log of ON_DEMAND_LOGS) {
      const row = table.locator('tbody tr', { hasText: log.title });
      await expect(row).toHaveCount(1, { timeout: UI_TIMEOUT });
      await expect(row.locator('.compliance-report__edit')).toBeEnabled({ timeout: UI_TIMEOUT });
      await expect(row.locator('.compliance-report__delete')).toHaveCount(0, { timeout: UI_TIMEOUT });
      // A DOM id of its own, from the case — not a shared `complianceReportImages-0`.
      await expect(row.locator(`#complianceReportImages-case-${log.sdkCaseId}`)).toHaveCount(1, { timeout: UI_TIMEOUT });
    }
    await expect(page.locator('#complianceReportImages-0')).toHaveCount(0, { timeout: UI_TIMEOUT });

    // An ordinary log is unchanged: it keeps both actions and its compliance-id DOM id.
    const regular = table.locator('tbody tr', { hasText: 'Regular log' });
    await expect(regular.locator('.compliance-report__edit')).toBeEnabled({ timeout: UI_TIMEOUT });
    await expect(regular.locator('.compliance-report__delete')).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(regular.locator('#complianceReportImages-55')).toHaveCount(1, { timeout: UI_TIMEOUT });
  });

  test('Detaljer renders every on-demand row as completed and read-only', async ({ page }) => {
    await routeDetaljer(page);
    await openCompliancePage(page);
    await switchMode(page, 'details', 'index');

    const rows = page.locator('app-compliance-details-view tr.compliance-details__row');
    await expect(rows).toHaveCount(LOGS.length, { timeout: UI_TIMEOUT });

    const onDemand = page.locator('app-compliance-details-view tr.compliance-details__row[data-on-demand]');
    await expect(onDemand).toHaveCount(ON_DEMAND_LOGS.length, { timeout: UI_TIMEOUT });
    for (const log of ON_DEMAND_LOGS) {
      const row = onDemand.filter({ hasText: log.title });
      await expect(row).toHaveCount(1, { timeout: UI_TIMEOUT });
      await expect(row).toHaveClass(/\bis-done\b/, { timeout: UI_TIMEOUT });
      await expect(row.locator('.compliance-details__check')).toHaveClass(/\bis-visible\b/, { timeout: UI_TIMEOUT });
      // Completed: not a click target, not focusable, no delete, and no compliance id.
      await expect(row).not.toHaveClass(/\bis-clickable\b/, { timeout: UI_TIMEOUT });
      await expect(row).not.toHaveAttribute('tabindex', /.*/, { timeout: UI_TIMEOUT });
      await expect(row.locator('.compliance-details__delete')).toHaveCount(0, { timeout: UI_TIMEOUT });
      await expect(row).not.toHaveAttribute('data-compliance-id', /.*/, { timeout: UI_TIMEOUT });
    }
  });
});
