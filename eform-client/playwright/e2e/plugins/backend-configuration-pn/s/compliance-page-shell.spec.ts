import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import { generateRandmString, selectDateRangeOnNewDatePicker } from '../../../helper-functions';
import { waitForApiResponse, API_TIMEOUT } from '../wait-helpers';

/**
 * Standalone Compliance page — SHELL suite (#1160 / #1163).
 *
 * Covers only what the shell owns: the route, the nine always-present filter
 * controls (plus `Opdater periode`, which exists ONLY while "Sæt periode" is
 * selected — #1185), the mode toggle, the auto-fetch-on-change state machine,
 * the Oversigt reset and the pagination chrome.
 *
 * #1185 rewrote the fetch contract. Every filter change now re-queries the
 * ACTIVE mode after a ~300 ms debounce and never blanks the result; the one
 * exception is "Sæt periode", whose range is staged until `Opdater periode`
 * commits it. Clicking `#complianceMode-overview` — from anywhere, including
 * while it is already active — resets all six filters to their defaults and
 * renders Oversigt. Request COUNTS are never asserted across a filter change:
 * `=== 1` races the debounce, so the specs wait for the response instead.
 * The rows themselves belong to #1164 (Oversigt), #1165 (Detaljer) and #1167
 * (Rapport) and are asserted by their own suites — Oversigt's is
 * `compliance-overview.spec.ts`, in this same shard. Where a child has not
 * landed the result container is deliberately empty after a fetch and the
 * pagination reads "Ingen resultater".
 *
 * One shell guarantee is genuinely #1164's and is asserted HERE because the
 * template that carries it is the shell's: the pagination <nav> exists in
 * Detaljer ONLY. Oversigt is one row per property and never pages; Rapport
 * renders whole sub-reports and does not page either (the prototype empties the
 * same container in both — compliance.js:1460 and :1820-1821).
 *
 * Structural notes, each of them a trap this repo has already been bitten by:
 *
 *  - `page.goto` BEFORE `LoginPage.login()`: the login page object does not
 *    navigate.
 *  - The page is reached BY URL, never by clicking the sidebar. The plugin's
 *    menu seeding only inserts `MenuItem` rows on a fresh install, so the
 *    "Compliance" nav entry is missing on every existing database — asserting
 *    the sidebar here would encode that core bug as a requirement. The route
 *    must be deep-linkable, and that is what is asserted.
 *  - mtx-select text is read from `.ng-value-label`, never `.ng-value`: the
 *    latter's innerText includes the × clear-icon glyph.
 *  - Dropdown options are picked BY LABEL, never by nth() index.
 *  - Shard `s` does not seed SQL, so nothing here depends on a seeded
 *    property; every assertion holds against an empty installation. Where a
 *    test needs data (two planning tags, a non-admin account) it creates it
 *    through the admin API rather than skipping.
 *
 * A second, serial describe block at the bottom covers NON-ADMIN access —
 * #1160 decision 6, the one acceptance criterion the admin-only
 * `goToCompliancePage` helper cannot reach.
 */
const BASE_URL = 'http://localhost:4200';
const PAGE_URL = `${BASE_URL}/plugins/backend-configuration-pn/compliance-report`;
const ADMIN_EMAIL = 'admin@admin.com';
const ADMIN_PASSWORD = 'secretpassword';
const USER_PASSWORD = 'Secret_password_2026!';
const rand = generateRandmString(8).toLowerCase();

async function goToCompliancePage(page: Page): Promise<void> {
  await page.goto(BASE_URL);
  await new LoginPage(page).login();
  await page.waitForTimeout(2000);
  await page.goto(PAGE_URL);
  await page.locator('#complianceFilterProperty').waitFor({ state: 'visible', timeout: 60000 });
}

/**
 * Picks an mtx-select option BY LABEL, never by nth(). Regex `hasText` matches
 * the RAW option text, hence the tolerated padding.
 */
async function selectOptionByLabel(page: Page, selectId: string, label: string): Promise<void> {
  await page.locator(`#${selectId}`).click();
  await page
    .locator('.ng-dropdown-panel .ng-option', { hasText: new RegExp(`^\\s*${escapeRegExp(label)}\\s*$`) })
    .first()
    .click();
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * The auto-fetch of one mode, awaited by RESPONSE rather than counted: a
 * filter change fetches after a debounce, and `requests === 1` taken at any
 * fixed moment races it (#1185 pitfall 6). `endpoint` is the controller
 * action — `overview` for Oversigt, `index` for Detaljer, `eform-columns`
 * for Rapport.
 */
function complianceResponse(page: Page, endpoint: 'overview' | 'index' | 'eform-columns') {
  return waitForApiResponse(
    page,
    `compliance ${endpoint}`,
    (r) => r.url().includes(`/api/backend-configuration-pn/compliance-report/${endpoint}`)
      && r.request().method() === 'POST',
    API_TIMEOUT,
  );
}

function addDays(d: Date, n: number): Date {
  const out = new Date(d);
  out.setDate(out.getDate() + n);
  return out;
}

/**
 * Asserts the six filters read their defaults — the PDF's list, verbatim:
 * Alle ejendomme / Alle kalendere / Alle tags / Ikke udførte opgaver /
 * Alle medarbejdere / År til dato. The four CLEARABLE selects have NO value
 * when reset (their default is `null` / `[]`), so what shows is ng-select's
 * `.ng-placeholder`, and `.ng-value-label` must be ABSENT; status and period
 * are non-clearable and carry a real selected label.
 */
async function expectDefaultFilters(page: Page): Promise<void> {
  for (const [id, placeholder] of [
    ['complianceFilterProperty', 'Alle ejendomme'],
    ['complianceFilterBoard', 'Alle kalendere'],
    ['complianceTagFilter', 'Alle tags'],
    ['complianceFilterEmployee', 'Alle medarbejdere'],
  ] as const) {
    await expect(page.locator(`#${id} .ng-value-label`)).toHaveCount(0);
    await expect(page.locator(`#${id} .ng-placeholder`)).toHaveText(new RegExp(`^\\s*${placeholder}\\s*$`));
  }
  await expect(page.locator('#complianceFilterStatus .ng-value-label')).toHaveText(/^\s*Ikke udførte opgaver\s*$/);
  await expect(page.locator('#complianceFilterPeriod .ng-value-label')).toHaveText(/^\s*År til dato\s*$/);
  // No custom range survives a reset — and with it goes the commit button.
  await expect(page.locator('.compliance-filters__custom-range')).toHaveCount(0);
  await expect(page.locator('#complianceShowReportBtn')).toHaveCount(0);
}

async function loginViaApi(page: Page, email: string, password: string): Promise<string> {
  const res = await page.request.post(`${BASE_URL}/api/auth/token`, {
    form: { username: email, password: password, grant_type: 'password' },
  });
  const json = await res.json();
  return json?.model?.accessToken || '';
}

/**
 * UI login for an arbitrary account. `LoginPage.login()` only knows the
 * default admin, so the non-admin suite below needs its own.
 */
async function loginAs(page: Page, email: string, password: string): Promise<void> {
  const loginBtn = page.locator('#loginBtn');
  await loginBtn.waitFor({ state: 'visible', timeout: 60000 });
  await page.locator('#username').fill(email);
  await page.locator('#password').fill(password);
  const loginResponsePromise = page.waitForResponse(
    (r) => r.url().includes('/api/auth/token'),
    { timeout: 30000 },
  ).catch(() => null);
  await loginBtn.click();
  await loginResponsePromise;
  await page.waitForURL((url) => !url.pathname.startsWith('/auth'), { timeout: 20000 })
    .catch(() => console.log(`loginAs ${email}: still on ${page.url()}`));
  await page.waitForTimeout(1000);
}

/**
 * Creates a `user`-role account carrying ONLY
 * `backend_configuration_plugin_access` — the plugin-wide gate the
 * `compliance-report` route's PARENT enforces — and nothing else. Adapted from
 * `z/adhoc-nonadmin-access.spec.ts` `setupNonAdminUser` (lines 126-229), which
 * itself copies `r/property-workers-nonadmin-no-logout.spec.ts`; only the
 * claim list and the name strings are suite-local.
 *
 * The claim list is deliberately MINIMAL. Decision 6 in #1160 says the page is
 * open to every authenticated plugin user: the calendar view mode it replaces
 * sits behind four client-side admin guards, and `compliance-report` is
 * `canActivate: [AuthGuard]` with no `requiredPermission`. Granting anything
 * more here would let the suite pass even if `IsAdminGuard`/`PermissionGuard`
 * came back.
 *
 * `isDeviceUser: false`, so the account has no SDK worker/site and no
 * `PropertyWorkers` row — nothing about this fixture needs seeded SQL, which
 * is why it works on shard `s`.
 */
async function setupNonAdminUser(page: Page): Promise<string> {
  const token = await loginViaApi(page, ADMIN_EMAIL, ADMIN_PASSWORD);
  expect(token).not.toBe('');
  const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
  const groupName = `compliance-nonadmin-${rand}`;
  const userEmail = `complianceuser-${rand}@test.com`;

  await page.request.post(`${BASE_URL}/api/security/groups`, {
    headers, data: { userIds: [], name: groupName },
  });
  const indexRes = await page.request.post(`${BASE_URL}/api/security/groups/index`, {
    headers,
    data: { sort: 'Id', nameFilter: groupName, pageIndex: 0, pageSize: 100, isSortDsc: false, offset: 0 },
  });
  const groups = (await indexRes.json())?.model?.entities || [];
  const groupId = groups.find((g: any) => g.groupName === groupName)?.id || 0;
  expect(groupId).toBeGreaterThan(0);

  // Land the non-admin somewhere it is allowed, so the positive app-shell
  // assertion can be taken BEFORE the guarded navigation: after a
  // guard-cancelled INITIAL navigation no route activates at all and even the
  // claim-independent footer is absent.
  await page.request.put(`${BASE_URL}/api/security/groups/settings`, {
    headers,
    data: { id: groupId, redirectLink: '/plugins/backend-configuration-pn/property-workers' },
  });

  const pluginsRes = await page.request.get(
    `${BASE_URL}/api/plugins-management/installed?sort=id&isSortDsc=true&pageSize=1000&pageIndex=0&offset=0`,
    { headers },
  );
  const plugins = (await pluginsRes.json())?.model?.pluginsList || [];
  const wantedPluginClaims: Record<string, string[]> = {
    'eform-backend-configuration-plugin': ['backend_configuration_plugin_access'],
  };
  for (const plugin of plugins) {
    const wanted = wantedPluginClaims[plugin.pluginId];
    if (!wanted) continue;
    const permUrl = `${BASE_URL}/api/plugins-permissions/group-permissions/${plugin.id}`;
    const currentPerms = (await (await page.request.get(permUrl, { headers })).json())?.model || [];
    const permIdMap: Record<string, number> = {};
    for (const gp of currentPerms) {
      for (const perm of gp.permissions || []) {
        permIdMap[perm.claimName] = perm.permissionId;
      }
    }
    const pluginPerms = wanted.map((claimName, i) => ({
      isEnabled: true,
      claimName,
      permissionId: permIdMap[claimName] || i + 1,
      permissionName: claimName,
    }));
    await page.request.put(permUrl, { headers, data: [{ permissions: pluginPerms, groupId }] });
  }

  const createUserRes = await page.request.post(`${BASE_URL}/api/admin/create-user`, {
    headers,
    data: {
      id: 0,
      firstName: `ComplianceUser${rand}`,
      lastName: 'NonAdmin',
      userName: userEmail,
      email: userEmail,
      password: USER_PASSWORD,
      passwordConfimation: USER_PASSWORD,
      role: 'user',
      groupId,
      isDeviceUser: false,
    },
  });
  const createUserJson = await createUserRes.json().catch(() => null);
  console.log(`create-user ${userEmail}: status=${createUserRes.status()} success=${createUserJson?.success}`);
  expect(createUserJson?.success).toBe(true);

  return userEmail;
}

// ---------------------------------------------------------------------------
// Export helpers (#1189). Shard `s` seeds no SQL and CI has no soffice, so
// BOTH halves of the export path are intercepted: the Oversigt aggregation is
// answered with one row (that is what makes `canDownload` — `reportVisible &&
// total > 0` — true on an empty installation), and the export endpoint is
// answered with a small file. What is under test is the CLIENT wiring: the
// request body, the file name taken from `Content-Disposition`, the preview
// dialog and the busy/error handling of the Download button.
// ---------------------------------------------------------------------------
const EXPORT_ROUTE = '**/api/backend-configuration-pn/compliance-report/export';
const OVERVIEW_ROUTE = '**/api/backend-configuration-pn/compliance-report/overview';
// A property AND a board name with `ø` — the ascii `filename=` fallback would
// mangle the board to `Milj_tilsyn`, which is exactly what must NOT be saved.
const CSV_FILE_NAME = 'Oversigt-Alle-Miljøtilsyn-01.01.2026-05.09.2026.csv';
const PDF_FILE_NAME = 'Oversigt-Alle-Miljøtilsyn-01.01.2026-05.09.2026.pdf';

/** Mirrors `ComplianceExportFileNaming.BuildContentDisposition`: lossy ascii first, RFC 5987 second. */
function contentDisposition(fileName: string): string {
  // eslint-disable-next-line no-control-regex
  const ascii = fileName.replace(/[^\x20-\x7e]/g, '_');
  return `attachment; filename="${ascii}"; filename*=UTF-8''${encodeURIComponent(fileName)}`;
}

async function routeOverviewWithOneRow(page: Page): Promise<void> {
  const row = {
    propertyId: 9, propertyName: 'Ejendom 9',
    total: 1, done: 1, overdue: 0, dueTotal: 1, dueDone: 1, compliancePct: 100,
  };
  const totals = { ...row, propertyId: 0, propertyName: null };
  await page.route(OVERVIEW_ROUTE, route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ success: true, message: '', model: { rows: [row], totals } }),
  }));
}

async function selectExportFormat(page: Page, label: 'PDF' | 'CSV'): Promise<void> {
  await page.locator('#complianceExportFormat').click();
  // By label, never nth(); regex `hasText` matches the RAW text, hence `\s*`.
  await page.locator('.ng-dropdown-panel .ng-option', { hasText: new RegExp(`^\\s*${label}\\s*$`) }).first().click();
  await expect(page.locator('#complianceExportFormat .ng-value-label')).toHaveText(label);
}

/** The smallest PDF pdf.js will open: one empty A4-landscape page, correct xref. */
function minimalPdf(): string {
  const objects = [
    '<</Type/Catalog/Pages 2 0 R>>',
    '<</Type/Pages/Kids[3 0 R]/Count 1>>',
    '<</Type/Page/Parent 2 0 R/MediaBox[0 0 842 595]>>',
  ];
  let out = '%PDF-1.4\n';
  const offsets: number[] = [];
  objects.forEach((body, i) => {
    offsets.push(out.length);
    out += `${i + 1} 0 obj\n${body}\nendobj\n`;
  });
  const xref = out.length;
  out += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  for (const o of offsets) {
    out += `${String(o).padStart(10, '0')} 00000 n \n`;
  }
  out += `trailer\n<</Size ${objects.length + 1}/Root 1 0 R>>\nstartxref\n${xref}\n%%EOF\n`;
  return out;
}

test.describe('Compliance page shell (#1163)', () => {
  test('renders at its own URL with all nine filter controls, and no Opdater periode outside custom mode', async ({ page }) => {
    await goToCompliancePage(page);

    // The route resolved rather than being swallowed by compliances/:propertyId.
    expect(page.url()).toContain('/plugins/backend-configuration-pn/compliance-report');

    await expect(page.locator('#complianceFilterProperty')).toBeVisible();
    await expect(page.locator('#complianceFilterBoard')).toBeVisible();
    await expect(page.locator('#complianceTagFilter')).toBeVisible();
    await expect(page.locator('#complianceFilterStatus')).toBeVisible();
    await expect(page.locator('#complianceFilterEmployee')).toBeVisible();
    await expect(page.locator('#complianceFilterPeriod')).toBeVisible();
    await expect(page.locator('#compliancePeriodDisplay')).toBeVisible();
    await expect(page.locator('#complianceExportFormat')).toBeVisible();
    await expect(page.locator('#complianceDownloadBtn')).toBeVisible();
    // #1185: the fetch button exists ONLY while "Sæt periode" is selected. On a
    // fresh load the preset is "År til dato", every change auto-fetches, and a
    // permanently-disabled button would only invite clicks — so it is NOT in
    // the DOM (`toHaveCount(0)`, not `toBeHidden()`).
    await expect(page.locator('#complianceShowReportBtn')).toHaveCount(0);
  });

  test('opens in Oversigt with the three mode buttons and their pressed state', async ({ page }) => {
    await goToCompliancePage(page);

    await expect(page.locator('#complianceMode-overview')).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('#complianceMode-details')).toHaveAttribute('aria-pressed', 'false');
    await expect(page.locator('#complianceMode-report')).toHaveAttribute('aria-pressed', 'false');

    await page.locator('#complianceMode-details').click();

    await expect(page.locator('#complianceMode-overview')).toHaveAttribute('aria-pressed', 'false');
    await expect(page.locator('#complianceMode-details')).toHaveAttribute('aria-pressed', 'true');
  });

  test('disables the status filter in Oversigt and enables it elsewhere', async ({ page }) => {
    await goToCompliancePage(page);

    // ng-select marks a disabled control on the container, and the wrapper
    // carries the explanation because a disabled select swallows hover.
    await expect(page.locator('#complianceFilterStatus .ng-select-disabled')).toBeVisible();

    await page.locator('#complianceMode-details').click();
    await expect(page.locator('#complianceFilterStatus .ng-select-disabled')).toHaveCount(0);

    // Deliberately also enabled in Rapport — nobody should have to detour
    // through Detaljer to change status.
    await page.locator('#complianceMode-report').click();
    await expect(page.locator('#complianceFilterStatus .ng-select-disabled')).toHaveCount(0);
  });

  test('a filter change auto-fetches the active mode and never blanks the result (#1185)', async ({ page }) => {
    // The entry auto-fetch is armed BEFORE navigation and awaited first:
    // `goToCompliancePage` only waits for a filter control, so a waiter armed
    // after it could be satisfied by the entry response still in flight and
    // the preset change below would then go unobserved.
    await page.goto(BASE_URL);
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);
    const entry = complianceResponse(page, 'overview');
    await page.goto(PAGE_URL);
    await page.locator('#complianceFilterProperty').waitFor({ state: 'visible', timeout: 60000 });
    expect((await entry).ok()).toBeTruthy();

    // The page auto-fetches Oversigt once on load, so `reportVisible` is true
    // and the placeholder is gone to begin with. The pagination chrome is NOT:
    // #1164 hides the whole <nav> outside Detaljer (Oversigt is one row per
    // property, and the aggregation endpoint has no paging parameters to
    // honour), so it does not exist until the mode is Detaljer.
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);
    await expect(page.locator('#compliancePagination')).toHaveCount(0);

    // --- Oversigt: a fixed-preset change re-queries the aggregation ---------
    // Awaited by RESPONSE, armed BEFORE the gesture and AFTER the entry
    // response above, so it can only be met by the refetch. The request
    // itself fires only after the ~300 ms debounce, so a counter read at any
    // fixed moment would race it; the response is the one signal that cannot.
    const overviewRefetch = complianceResponse(page, 'overview');
    await selectOptionByLabel(page, 'complianceFilterPeriod', '3 mdr.');
    expect((await overviewRefetch).ok()).toBeTruthy();
    await expect(page.locator('#complianceFilterPeriod .ng-value-label')).toHaveText(/^\s*3 mdr\.\s*$/);
    // The result was REPLACED, never blanked: the placeholder that the old
    // invalidate-on-change contract showed here must not appear.
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);
    await expect(page.locator('#complianceCasesRoot')).toHaveAttribute('aria-busy', 'false', { timeout: API_TIMEOUT });

    // --- Detaljer: the same rule, for the one control Oversigt disables ---
    // The mode switch itself replays the trigger to the recreated child (one
    // `/index` request — the design, not a defect); wait it out so the status
    // change below is the only thing in flight.
    const detailsEntry = complianceResponse(page, 'index');
    await page.locator('#complianceMode-details').click();
    await detailsEntry;
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);
    // Detaljer is the ONE mode that pages, so only here does the nav exist.
    await expect(page.locator('#compliancePagination')).toBeVisible();

    const detailsRefetch = complianceResponse(page, 'index');
    await selectOptionByLabel(page, 'complianceFilterStatus', 'Alle opgaver');
    expect((await detailsRefetch).ok()).toBeTruthy();
    await expect(page.locator('#complianceFilterStatus .ng-value-label')).toHaveText(/^\s*Alle opgaver\s*$/);

    // Still rendered, still paged: nothing was torn down between the change
    // and the re-render.
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);
    await expect(page.locator('#compliancePagination')).toBeVisible();
    await expect(page.locator('#complianceCasesRoot')).toHaveAttribute('aria-busy', 'false', { timeout: API_TIMEOUT });
  });

  test('the result survives a mode switch, and the pagination chrome is Detaljer-only', async ({ page }) => {
    await goToCompliancePage(page);

    // Oversigt already fetched on entry — no button to press (#1185): the
    // mode switch replays that trigger to each recreated child.
    const detailsEntry = complianceResponse(page, 'index');
    await page.locator('#complianceMode-details').click();
    await detailsEntry;
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);
    await expect(page.locator('#compliancePagination')).toBeVisible();

    // A mode switch never re-blanks.
    const reportEntry = complianceResponse(page, 'eform-columns');
    await page.locator('#complianceMode-report').click();
    await reportEntry;
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);
    // ...but the pagination chrome is Detaljer-only, so leaving Detaljer takes
    // it away again — in Rapport as well as in Oversigt. The prototype empties
    // this container in both (compliance.js:1820-1821 and :1460).
    await expect(page.locator('#compliancePagination')).toHaveCount(0);

    // Back to Oversigt. Under #1185 this click is the RESET path (defaults +
    // one overview fetch, asserted in its own test below); what is asserted
    // here is only that the container is never blanked on the way.
    const overviewEntry = complianceResponse(page, 'overview');
    await page.locator('#complianceMode-overview').click();
    await overviewEntry;
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);
    await expect(page.locator('#compliancePagination')).toHaveCount(0);
  });

  test('"Sæt periode" stages the range behind Opdater periode: placeholder, disabled-with-reason, then one fetch (#1185)', async ({ page }) => {
    // The entry auto-fetch is armed BEFORE navigation and awaited, so the
    // request counter attached afterwards can never see it — attaching the
    // counter after `goToCompliancePage` alone leaves that to event timing.
    await page.goto(BASE_URL);
    await new LoginPage(page).login();
    await page.waitForTimeout(2000);
    const entry = complianceResponse(page, 'overview');
    await page.goto(PAGE_URL);
    await page.locator('#complianceFilterProperty').waitFor({ state: 'visible', timeout: 60000 });
    expect((await entry).ok()).toBeTruthy();
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);

    // Custom mode is the ONE filter change that does not auto-fetch, and the
    // ONE state in which the fetch button exists at all. Before it: no button.
    await expect(page.locator('#complianceShowReportBtn')).toHaveCount(0);

    // Every Oversigt POST from here on is counted: selecting "Sæt periode"
    // and picking the dates must issue NONE — the range is staged until the
    // button commits it. Counting is safe on THIS side of the contract (zero
    // is zero however long the debounce), and the count is read after the
    // button click's response, i.e. well past any debounce.
    let requests = 0;
    page.on('request', r => {
      if (r.method() === 'POST'
        && r.url().includes('/api/backend-configuration-pn/compliance-report/overview')) {
        requests++;
      }
    });

    await selectOptionByLabel(page, 'complianceFilterPeriod', 'Sæt periode');

    // The uncommitted state is the placeholder, in its #1185 CUSTOM-mode
    // wording (it points at the button that exists only here) — not the
    // previous result and not a spinner.
    const placeholder = page.locator('#complianceEmptyState');
    await expect(placeholder).toBeVisible();
    await expect(placeholder).toHaveText(/^\s*Vælg en periode og klik Opdater periode\.\s*$/);

    // Both bounds still empty: the button exists now, reads "Opdater periode"
    // (renamed from "Opdater tabel" — it commits a PERIOD, nothing else
    // waits for it), is dead, and the reason is on screen rather than being
    // a silent no-op the way the prototype's modal is.
    const button = page.locator('#complianceShowReportBtn');
    await expect(button).toBeVisible();
    await expect(button).toHaveText(/^\s*Opdater periode\s*$/);
    await expect(button).toBeDisabled();
    await expect(page.locator('#compliancePeriodError')).toBeVisible();

    // The range control stays present and editable for as long as "Sæt
    // periode" is selected — the prototype's modal cannot be reopened at all
    // once a range has been set. The from > to case is asserted in the jest
    // spec instead: typing a date here depends on the active locale's input
    // format (the app uses a date-fns adapter), which the shard cannot pin.
    await expect(page.locator('#complianceCustomFrom')).toBeVisible();
    await expect(page.locator('#complianceCustomTo')).toBeVisible();

    // A valid range, picked through the calendar popup (locale-independent):
    // the button comes alive, the reason goes away — and STILL nothing fetched.
    const from = addDays(new Date(), -10);
    const to = addDays(new Date(), -1);
    await page.locator('.compliance-filters__custom-range mat-datepicker-toggle button').click();
    await selectDateRangeOnNewDatePicker(
      page,
      from.getFullYear(), from.getMonth() + 1, from.getDate(),
      to.getFullYear(), to.getMonth() + 1, to.getDate(),
    );
    await expect(button).toBeEnabled();
    await expect(page.locator('#compliancePeriodError')).toHaveCount(0);
    await expect(placeholder).toBeVisible();
    expect(requests).toBe(0);

    // The commit: exactly one Oversigt fetch, and the placeholder is gone.
    const committed = complianceResponse(page, 'overview');
    await button.click();
    expect((await committed).ok()).toBeTruthy();
    await expect(placeholder).toHaveCount(0);
    await expect(page.locator('#complianceCasesRoot')).toHaveAttribute('aria-busy', 'false', { timeout: API_TIMEOUT });
    expect(requests).toBe(1);
    // The button stays for as long as "Sæt periode" is selected — a committed
    // range can be re-edited and re-committed.
    await expect(button).toBeVisible();
    await expect(button).toBeEnabled();
  });

  test('clicking Oversigt resets all six filters to their defaults and renders the overview (#1185)', async ({ page }) => {
    // A tag to select, created up front through the admin API: shard `s`
    // seeds no SQL, and a reset that has nothing to undo proves nothing.
    await page.goto(BASE_URL);
    const token = await loginViaApi(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    expect(token).not.toBe('');
    const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
    const tagName = `zz-compl-reset-${rand}`;
    const tagRes = await page.request.post(`${BASE_URL}/api/items-planning-pn/tags`, {
      headers, data: { name: tagName },
    });
    expect(tagRes.ok()).toBeTruthy();

    await goToCompliancePage(page);
    await expectDefaultFilters(page);

    // --- Into Detaljer, and away from every default the shard can reach ----
    // Property/calendar/employee need seeded rows this shard does not have;
    // tag, status (enabled only outside Oversigt) and period are the three
    // that can be moved deterministically. Each change auto-fetches — waited
    // for by response, so nothing is still debouncing when Oversigt is
    // clicked and the request count below is not racing anything.
    const detailsEntry = complianceResponse(page, 'index');
    await page.locator('#complianceMode-details').click();
    await detailsEntry;

    const tagRefetch = complianceResponse(page, 'index');
    await selectOptionByLabel(page, 'complianceTagFilter', tagName);
    await page.keyboard.press('Escape');
    await tagRefetch;
    await expect(page.locator('#complianceTagFilter .ng-value-label')).toHaveText(new RegExp(`^\\s*${escapeRegExp(tagName)}\\s*$`));

    const statusRefetch = complianceResponse(page, 'index');
    await selectOptionByLabel(page, 'complianceFilterStatus', 'Alle opgaver');
    await statusRefetch;
    await expect(page.locator('#complianceFilterStatus .ng-value-label')).toHaveText(/^\s*Alle opgaver\s*$/);

    // "Sæt periode" last: it does NOT fetch (the range is staged), it swaps
    // the result for the placeholder, and it is the one state with a custom
    // range and a fetch button for the reset to clear.
    await selectOptionByLabel(page, 'complianceFilterPeriod', 'Sæt periode');
    await expect(page.locator('#complianceEmptyState')).toBeVisible();
    await expect(page.locator('.compliance-filters__custom-range')).toBeVisible();
    await expect(page.locator('#complianceShowReportBtn')).toBeVisible();

    // --- The reset -------------------------------------------------------
    // ONE `/overview` POST. Scoped to the aggregation endpoint on purpose: the
    // Detaljer child may fire (and cancel) a last `/index` while the ngSwitch
    // swaps it out (#1185 pitfall 3), and the reset also reloads the
    // property-scoped reference lists — neither is what is being counted.
    let overviewRequests = 0;
    page.on('request', r => {
      if (r.url().includes('/api/backend-configuration-pn/compliance-report/overview')
        && r.method() === 'POST') {
        overviewRequests++;
      }
    });
    const reset = complianceResponse(page, 'overview');
    await page.locator('#complianceMode-overview').click();
    expect((await reset).ok()).toBeTruthy();

    await expect(page.locator('#complianceMode-overview')).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('#complianceMode-details')).toHaveAttribute('aria-pressed', 'false');
    await expectDefaultFilters(page);
    // The overview rendered automatically — no placeholder, no second gesture.
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);
    await expect(page.locator('#complianceCasesRoot')).toHaveAttribute('aria-busy', 'false', { timeout: API_TIMEOUT });
    await expect(page.locator('#compliancePagination')).toHaveCount(0);
    expect(overviewRequests).toBe(1);

    // --- Pressing Oversigt while ALREADY in Oversigt resets too -----------
    // "Uanset hvor der trykkes på Oversigt": the active button is not inert.
    const presetRefetch = complianceResponse(page, 'overview');
    await selectOptionByLabel(page, 'complianceFilterPeriod', '3 mdr.');
    await presetRefetch;
    await expect(page.locator('#complianceFilterPeriod .ng-value-label')).toHaveText(/^\s*3 mdr\.\s*$/);

    const resetAgain = complianceResponse(page, 'overview');
    await page.locator('#complianceMode-overview').click();
    expect((await resetAgain).ok()).toBeTruthy();
    await expect(page.locator('#complianceMode-overview')).toHaveAttribute('aria-pressed', 'true');
    await expectDefaultFilters(page);
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);
  });

  test('the tag filter is multi-select and labels the selection "{first} +{n-1}"', async ({ page }) => {
    // Deterministic instead of `test.skip(optionCount < 2)`: shard `s` seeds no
    // SQL, so on an empty installation the old form silently asserted NOTHING.
    // Two planning tags are created up front through the admin API
    // (`POST api/items-planning-pn/tags`, bare `[Authorize]`), so the control
    // always has something to select. The options are still picked BY LABEL —
    // the old `nth(0)`/`nth(1)` sat directly under the comment forbidding it.
    await page.goto(BASE_URL);
    const token = await loginViaApi(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    expect(token).not.toBe('');
    const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
    const tagA = `zz-compl-tag-a-${rand}`;
    const tagB = `zz-compl-tag-b-${rand}`;
    for (const name of [tagA, tagB]) {
      const res = await page.request.post(`${BASE_URL}/api/items-planning-pn/tags`, {
        headers, data: { name },
      });
      expect(res.ok()).toBeTruthy();
    }

    await goToCompliancePage(page);

    await page.locator('#complianceTagFilter').click();
    const options = page.locator('.ng-dropdown-panel .ng-option');
    await expect(options.filter({ hasText: tagA })).toHaveCount(1);
    await expect(options.filter({ hasText: tagB })).toHaveCount(1);

    // "First" is TAG-LIST order, never click order (compliance.js:2079-2089),
    // and ng-select renders `[items]` in array order — so DOM order IS list
    // order. Read it rather than assuming which of the two the server returns
    // first; the installation may already hold other tags.
    const labels = (await options.allInnerTexts()).map(t => t.trim());
    const idxA = labels.indexOf(tagA);
    const idxB = labels.indexOf(tagB);
    expect(idxA).toBeGreaterThanOrEqual(0);
    expect(idxB).toBeGreaterThanOrEqual(0);
    const first = idxA < idxB ? tagA : tagB;

    await options.filter({ hasText: tagA }).first().click();
    await options.filter({ hasText: tagB }).first().click();
    await page.keyboard.press('Escape');

    // .ng-value innerText would include the × clear-icon glyph.
    await expect(page.locator('#complianceTagFilter .ng-value-label')).toHaveText(`${first} +1`);
  });

  test('the Download button stays inert until a format is chosen and rows exist', async ({ page }) => {
    await goToCompliancePage(page);

    // No export format selected yet.
    await expect(page.locator('#complianceDownloadBtn')).toBeDisabled();
  });

  test('"Hent som" offers exactly PDF and CSV, nothing else (#1189)', async ({ page }) => {
    await goToCompliancePage(page);

    await page.locator('#complianceExportFormat').click();
    const options = page.locator('.ng-dropdown-panel .ng-option');
    await expect(options).toHaveCount(2);
    const labels = (await options.allInnerTexts()).map(t => t.trim());
    expect(labels).toEqual(['PDF', 'CSV']);
    await page.keyboard.press('Escape');
  });

  test('selecting CSV enables Download after a fetch, and the download carries the server file name (#1189)', async ({ page }) => {
    await routeOverviewWithOneRow(page);
    let exportRequests = 0;
    let exportBody: any = null;
    await page.route(EXPORT_ROUTE, route => {
      exportRequests++;
      exportBody = route.request().postDataJSON();
      return route.fulfill({
        status: 200,
        headers: {
          'content-type': 'text/csv; charset=utf-8',
          // Exactly the shape `ComplianceExportFileNaming.BuildContentDisposition`
          // emits: a LOSSY ascii `filename=` first, then the RFC 5987 form. The
          // client must prefer the latter, or the user gets `Milj_tilsyn`.
          'content-disposition': contentDisposition(CSV_FILE_NAME),
        },
        body: 'Ejendom;Compliance\nEjendom 9;100\n',
      });
    });

    await goToCompliancePage(page);
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);
    await expect(page.locator('#complianceDownloadBtn')).toBeDisabled();

    await selectExportFormat(page, 'CSV');
    await expect(page.locator('#complianceDownloadBtn')).toBeEnabled();

    const downloadPromise = page.waitForEvent('download', { timeout: 30000 });
    await page.locator('#complianceDownloadBtn').click();
    const download = await downloadPromise;

    // `ø` survived: the name came from `filename*=`, not the ascii fallback.
    expect(download.suggestedFilename()).toBe(CSV_FILE_NAME);
    expect(exportRequests).toBe(1);
    // The body is the page's request model plus the three export fields.
    expect(exportBody.format).toBe('csv');
    expect(exportBody.viewMode).toBe('overview');
    expect(exportBody.includeImageAppendix).toBe(false);
    expect(exportBody.propertyId).toBeNull();
    expect(typeof exportBody.dateFrom).toBe('string');
    expect(typeof exportBody.dateTo).toBe('string');
    // CSV never opens the preview, and the button is live again afterwards.
    await expect(page.locator('#compliancePdfPreviewTitle')).toHaveCount(0);
    await expect(page.locator('#complianceDownloadBtn')).toBeEnabled();
  });

  test('selecting PDF opens the PDF-forhåndsvisning dialog; Annuller closes, Gem saves without a second request (#1189)', async ({ page }) => {
    await routeOverviewWithOneRow(page);
    let exportRequests = 0;
    await page.route(EXPORT_ROUTE, route => {
      exportRequests++;
      return route.fulfill({
        status: 200,
        headers: {
          'content-type': 'application/pdf',
          'content-disposition': contentDisposition(PDF_FILE_NAME),
        },
        body: Buffer.from(minimalPdf(), 'latin1'),
      });
    });

    await goToCompliancePage(page);
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);
    await selectExportFormat(page, 'PDF');
    await expect(page.locator('#complianceDownloadBtn')).toBeEnabled();

    // --- Annuller: dialog closes, nothing downloads --------------------
    await page.locator('#complianceDownloadBtn').click();
    const title = page.locator('#compliancePdfPreviewTitle');
    await expect(title).toBeVisible({ timeout: 30000 });
    await expect(title).toHaveText(/^\s*PDF-forhåndsvisning\s*$/);
    await expect(page.locator('#compliancePdfPreviewFileName')).toHaveText(/^\s*Oversigt-Alle-Miljøtilsyn-01\.01\.2026-05\.09\.2026\.pdf\s*$/);

    const cancelBtn = page.locator('#compliancePdfPreviewCancelBtn');
    const saveBtn = page.locator('#compliancePdfPreviewSaveBtn');
    await expect(cancelBtn).toHaveText(/^\s*Annuller\s*$/);
    await expect(saveBtn).toHaveText(/^\s*Gem\s*$/);
    // Cancel first, the confirming action rightmost (check-button-conventions).
    const actionTexts = (await page.locator('mat-dialog-actions button, [mat-dialog-actions] button').allInnerTexts())
      .map(t => t.trim());
    expect(actionTexts).toEqual(['Annuller', 'Gem']);

    let downloads = 0;
    page.on('download', () => downloads++);
    await cancelBtn.click();
    await expect(title).toHaveCount(0);
    expect(downloads).toBe(0);
    expect(exportRequests).toBe(1);
    await expect(page.locator('#complianceDownloadBtn')).toBeEnabled();

    // --- Gem: saves the bytes already received, no second POST ----------
    await page.locator('#complianceDownloadBtn').click();
    await expect(title).toBeVisible({ timeout: 30000 });
    expect(exportRequests).toBe(2);

    const downloadPromise = page.waitForEvent('download', { timeout: 30000 });
    await saveBtn.click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toBe(PDF_FILE_NAME);
    await expect(title).toHaveCount(0);
    // Exactly one download across the whole test: none from Annuller, one from Gem.
    expect(downloads).toBe(1);
    // Still 2: `Gem` reused the preview's bytes rather than re-exporting.
    expect(exportRequests).toBe(2);
  });

  test('a failed export toasts and re-enables Download (#1189)', async ({ page }) => {
    await routeOverviewWithOneRow(page);
    // A REAL server 400, not a fulfilled one: the request is let through with
    // its `format` rewritten to `xlsx`, which `ComplianceExportService.Export`
    // rejects as `InvalidExportRequest` (400, text/plain) before it touches
    // any data or renders anything — deterministic on shard `s`'s empty
    // database. Counting the requests is the point: the global interceptor
    // chain carries TWO `HttpErrorInterceptor`s, and a 400 that reaches it is
    // re-issued immediately and then every 15 s before completing silently
    // ~75 s later with no error and no toast. `export()` bypasses that chain;
    // exactly one POST, and a toast within the timeout, is what proves it.
    let exportRequests = 0;
    await page.route(EXPORT_ROUTE, route => {
      exportRequests++;
      return route.continue({
        postData: JSON.stringify({ ...route.request().postDataJSON(), format: 'xlsx' }),
      });
    });

    await goToCompliancePage(page);
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);
    await selectExportFormat(page, 'CSV');
    await expect(page.locator('#complianceDownloadBtn')).toBeEnabled();

    const exportResponsePromise = waitForApiResponse(
      page,
      'compliance export',
      (r) => r.url().includes('/compliance-report/export'),
      API_TIMEOUT,
    );
    await page.locator('#complianceDownloadBtn').click();
    expect((await exportResponsePromise).status()).toBe(400);

    // The plugin service toasts itself — nothing is silently swallowed.
    await expect(page.locator('#toast-container .toast-error', { hasText: /Eksporten mislykkedes/ })).toBeVisible({ timeout: API_TIMEOUT });
    await expect(page.locator('#compliancePdfPreviewTitle')).toHaveCount(0);
    await expect(page.locator('#complianceDownloadBtn')).toBeEnabled();
    // One POST: the failure surfaced on the first answer, no retry storm.
    expect(exportRequests).toBe(1);
  });
});

/**
 * NON-ADMIN access — the acceptance criterion "the page loads for a non-admin"
 * (#1160 decision 6). This page is the only thing standing where the calendar
 * hid the same report behind four client-side admin guards, so a regression
 * that re-adds `IsAdminGuard`/`PermissionGuard` to `compliance-report` would
 * be invisible to every other test in this file: `goToCompliancePage` logs in
 * as the default admin.
 *
 * Serial, and seeded first, because the account is created through admin API
 * calls rather than a SQL dump — shard `s` loads none.
 */
test.describe.serial('Compliance page shell — non-admin access (#1160 decision 6)', () => {
  let userEmail = '';

  test('seed: create a non-admin user with plugin access only (as admin)', async ({ page }) => {
    test.setTimeout(300000);
    await page.goto(BASE_URL);
    await loginAs(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    await page.locator('#newEFormBtn').waitFor({ state: 'visible', timeout: 120000 });

    userEmail = await setupNonAdminUser(page);
    expect(userEmail).not.toBe('');
  });

  test('a non-admin loads the page and sees the filter bar and mode toggle', async ({ page }) => {
    test.setTimeout(300000);
    expect(userEmail).not.toBe('');

    await page.goto(BASE_URL);
    await loginAs(page, userEmail, USER_PASSWORD);
    expect(page.url()).not.toContain('/auth');

    // Positive app-shell assertion BEFORE the guarded navigation — a
    // guard-cancelled INITIAL navigation activates no route at all, so even
    // this claim-independent footer would be missing and the failure would
    // read as "the app is broken" rather than "the route is closed".
    await expect(page.locator('#sign-out-dropdown')).toBeVisible();

    // Direct URL: the route is `canActivate: [AuthGuard]`, exactly like
    // `q/calendar-admin-gating.spec.ts` navigates its non-admin. If a
    // PermissionGuard came back the navigation would be cancelled and the
    // waits below would fail.
    await page.goto(PAGE_URL);

    expect(page.url()).toContain('/plugins/backend-configuration-pn/compliance-report');

    // The filter bar mounted...
    await page.locator('#complianceFilterProperty')
      .waitFor({ state: 'visible', timeout: 60000 });
    await expect(page.locator('#complianceFilterStatus')).toBeVisible();
    await expect(page.locator('#complianceFilterPeriod')).toBeVisible();
    await expect(page.locator('#complianceDownloadBtn')).toBeVisible();
    // #1185: no fetch button outside "Sæt periode" — for a non-admin exactly
    // as for the admin (the button is a period commit, not a permission).
    await expect(page.locator('#complianceShowReportBtn')).toHaveCount(0);

    // ...and so did the mode toggle, in Oversigt, with no admin-only branch.
    await expect(page.locator('#complianceMode-overview')).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('#complianceMode-details')).toBeVisible();
    await expect(page.locator('#complianceMode-report')).toBeVisible();

    // The session survived the load: nothing on this page 403s a non-admin
    // into the HttpErrorInterceptor's refresh-then-logout path.
    expect(page.url()).not.toContain('/auth');
    await expect(page.locator('#sign-out-dropdown')).toBeVisible();
  });
});
