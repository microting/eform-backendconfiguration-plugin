import { test, expect, Page, Response } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString } from '../../../helper-functions';
import { BackendConfigurationPropertiesPage } from '../BackendConfigurationProperties.page';
import { BackendConfigurationAdhocPage } from '../BackendConfigurationAdhoc.page';
import { API_TIMEOUT, UI_TIMEOUT, ignoreUnhandledRejections, waitForApiResponse } from '../wait-helpers';

/**
 * Ad-hoc tasks — refresh button (#1379).
 *
 * `#backend-configuration-pn-adhoc-refresh` re-fetches the active view with
 * its current filters. A task created behind the page's back (straight
 * through the API, standing in for the mobile app) is absent until the
 * button is clicked and present afterwards, while the search filter is kept
 * (it is still in the input and still sent with the index request). The
 * button is disabled while the request is in flight: the index call is held
 * by a route handler until that has been asserted. The Historik view gets the
 * same check with a completed task.
 *
 * Seeds its own property via the UI; the tasks are created, completed and
 * deleted through the admin API.
 */
const BASE_URL = 'http://localhost:4200';
const API = `${BASE_URL}/api/backend-configuration-pn/adhoc`;
const INDEX_ROUTE = '**/api/backend-configuration-pn/adhoc/index';
const rand = generateRandmString(8).toLowerCase();

const property = {
  name: `adhoc-rf-${rand}`,
  chrNumber: generateRandmString(5),
  address: generateRandmString(5),
  cvrNumber: '5555555',
};
const listTaskTitle = `Adhoc-Refresh-List-${rand}`;
const historyTaskTitle = `Adhoc-Refresh-History-${rand}`;

const createdTaskIds: number[] = [];

async function login(page: Page): Promise<void> {
  await page.goto(BASE_URL);
  await new LoginPage(page).login();
}

async function apiHeaders(page: Page): Promise<Record<string, string>> {
  const res = await page.request.post(`${BASE_URL}/api/auth/token`, {
    form: { username: 'admin@admin.com', password: 'secretpassword', grant_type: 'password' },
    timeout: API_TIMEOUT,
  });
  expect(res.ok(), `admin token request failed: HTTP ${res.status()}`).toBe(true);
  const token = (await res.json())?.model?.accessToken;
  expect(token, 'admin token missing from /api/auth/token response').toBeTruthy();
  return { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
}

/** Creates an open task on the seeded property behind the page's back; returns its id. */
async function createTaskViaApi(page: Page, title: string): Promise<{ id: number; headers: Record<string, string> }> {
  const headers = await apiHeaders(page);
  const propsRes = await page.request.get(`${API}/properties`, { headers, timeout: API_TIMEOUT });
  const propertyId = (await propsRes.json())?.model?.find((p: { name: string }) => p.name === property.name)?.id;
  expect(propertyId, `seeded property ${property.name} not listed by GET adhoc/properties`).toBeTruthy();

  const createRes = await page.request.post(`${API}/`, {
    headers,
    data: { title, propertyId },
    timeout: API_TIMEOUT,
  });
  const created = await createRes.json();
  expect(created?.success, `POST adhoc/ failed: ${JSON.stringify(created)}`).toBe(true);
  createdTaskIds.push(created.model.id);
  return { id: created.model.id, headers };
}

function refreshBtn(page: Page) {
  return page.locator('#backend-configuration-pn-adhoc-refresh');
}

test.describe.serial('Adhoc overblik — refresh button (#1379)', () => {
  test.afterAll(async ({ browser }) => {
    if (createdTaskIds.length === 0) {
      return;
    }
    // Best-effort teardown: a failure here must not fail the run.
    const page = await browser.newPage().catch(() => undefined);
    if (!page) {
      return;
    }
    try {
      const headers = await apiHeaders(page);
      for (const id of createdTaskIds) {
        await page.request.delete(`${API}/${id}`, { headers, timeout: API_TIMEOUT });
      }
    } catch (err: any) {
      console.log(`afterAll cleanup failed (non-fatal): ${err?.message ?? err}`);
    }
    await page.close().catch(() => undefined);
  });

  test('seed: property', async ({ page }) => {
    // Login + one property create through the UI.
    test.setTimeout(120000);
    await login(page);
    const propertiesPage = new BackendConfigurationPropertiesPage(page);
    await propertiesPage.goToProperties();
    await propertiesPage.createProperty(property);
  });

  test('Overblik: refresh shows a task created elsewhere and keeps the search filter', async ({ page }) => {
    // Login, one debounced search, two API round-trips and one refresh.
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();

    const isIndex = (r: Response) =>
      r.url().endsWith('/api/backend-configuration-pn/adhoc/index') && r.request().method() === 'POST';

    // Filter on this run's token; the debounced filter change fires one index call.
    const searchedPromise = waitForApiResponse(
      page,
      'POST adhoc/index after the search filter',
      (r) => isIndex(r) && r.request().postDataJSON()?.searchText === rand,
      API_TIMEOUT,
    );
    await adhocPage.searchInput().fill(rand);
    await searchedPromise;

    await createTaskViaApi(page, listTaskTitle);
    // Nothing on the page re-fetches by itself.
    await expect(adhocPage.row(listTaskTitle)).toHaveCount(0, { timeout: UI_TIMEOUT });

    // Hold the refresh's index call so the in-flight state can be observed.
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    await page.route(INDEX_ROUTE, async (route) => {
      await gate;
      await route.continue();
    });
    const refreshedPromise = waitForApiResponse(
      page,
      'POST adhoc/index after the refresh click',
      isIndex,
      API_TIMEOUT,
    );
    ignoreUnhandledRejections(refreshedPromise);

    let refreshed: Response;
    try {
      await refreshBtn(page).click();
      await expect(refreshBtn(page)).toBeDisabled({ timeout: UI_TIMEOUT });
    } finally {
      // Never leave the held request (and the route) behind a failed assertion.
      release();
    }
    try {
      refreshed = await refreshedPromise;
    } finally {
      await page.unroute(INDEX_ROUTE);
    }

    expect(refreshed.request().postDataJSON()?.searchText, 'refresh must keep the search filter').toBe(rand);
    await expect(adhocPage.row(listTaskTitle)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(adhocPage.searchInput()).toHaveValue(rand, { timeout: UI_TIMEOUT });
    await expect(refreshBtn(page)).toBeEnabled({ timeout: UI_TIMEOUT });
  });

  test('Historik: refresh shows a task completed elsewhere', async ({ page }) => {
    // Login, the history tab load, three API round-trips and one refresh.
    test.setTimeout(120000);
    await login(page);
    const adhocPage = new BackendConfigurationAdhocPage(page);
    await adhocPage.goToAdhoc();
    await adhocPage.goToHistory();

    const { id, headers } = await createTaskViaApi(page, historyTaskTitle);
    const completeRes = await page.request.post(`${API}/${id}/completed`, {
      headers,
      data: { completed: true },
      timeout: API_TIMEOUT,
    });
    expect((await completeRes.json())?.success, 'POST adhoc/{id}/completed failed').toBe(true);
    await expect(adhocPage.historyRow(historyTaskTitle)).toHaveCount(0, { timeout: UI_TIMEOUT });

    const refreshedPromise = waitForApiResponse(
      page,
      'POST adhoc/history/index after the refresh click',
      (r) => r.url().includes('/api/backend-configuration-pn/adhoc/history/index') && r.request().method() === 'POST',
      API_TIMEOUT,
    );
    ignoreUnhandledRejections(refreshedPromise);
    await refreshBtn(page).click();
    await refreshedPromise;

    await expect(adhocPage.historyRow(historyTaskTitle)).toBeVisible({ timeout: UI_TIMEOUT });
    await expect(page).toHaveURL(/\/adhoc-tasks\/history(\?|$)/, { timeout: UI_TIMEOUT });
  });
});
