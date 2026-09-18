import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';
import {
  ignoreUnhandledRejections,
  SLOW_API_TIMEOUT,
  UI_TIMEOUT,
  waitForApiResponse,
} from '../wait-helpers';

/**
 * Standalone Compliance page — RAPPORT view (#1167, grouped by report
 * headline since #1188, one table per eForm under the headline since #1276).
 *
 * SCOPE, stated plainly. Rapport's eForm tables need COMPLETED cases whose
 * eForm answers have been submitted: the endpoint projects
 * `Case.CheckListId` → column schema → keyed cell bag, and a compliance row
 * that was never answered never reaches a headline group. Seeding that from a
 * spec means creating a property, an area rule, a planning, deploying it and
 * submitting the eForm — an order of magnitude more setup than
 * `compliance-overview.spec.ts` needs, and it would still assert nothing the
 * unit spec does not already pin harder. `compliance-report-sections.spec.ts`
 * owns the row-level rules (a missing cell key renders the en dash IN PLACE, a
 * named-but-unnamed headline is `#{id}` and not "Uden rapportoverskrift", a
 * section whose templates all lack a schema keeps its cases) as pure
 * functions, with no database behind them.
 *
 * What is asserted HERE is exactly what needs a browser and holds on an EMPTY
 * installation: the meta line, its `dd.MM.yyyy` period format, the empty-result
 * wording, and — for whatever sections the installation happens to render —
 * the caption-above-heading section structure, an eForm sub-heading over every
 * table, and the headline-less fallback section last.
 *
 * The #1276 layout — a headline answered on TWO eForms is one heading over two
 * tables, and a CheckBox / Date answer is a tick / `dd.MM.yyyy` — is asserted
 * against a MOCKED `eform-columns` response, the same technique
 * `compliance-page-shell.spec.ts` uses for its Rapport export test. Producing
 * that shape for real needs two eForms submitted against one headline, with a
 * ticked checkbox, over the SDK's device channel — no browser path exists. The
 * grouping itself is the SERVER's and is pinned by the backend's integration
 * tests; what only a browser can prove is how the view renders it: the
 * sub-headings, the separate grids with their own columns, and the tick in a
 * real mtx-grid cell with no `checked` / `unchecked` text left in it.
 *
 * Structural notes, each a trap this repo has already paid for:
 *
 *  - `page.goto` BEFORE `LoginPage.login()` — the login page object does not
 *    navigate.
 *  - The page is reached BY URL, never through the sidebar: the plugin's menu
 *    seeding only inserts `MenuItem` rows on a fresh install, so the nav entry
 *    is missing on every existing database.
 *  - Shard `s` seeds no SQL. Nothing below depends on seeded data.
 *  - A regex `hasText` matches RAW text, so the separator is matched with a
 *    tolerant `\s*` rather than an anchored literal.
 */

const BASE_URL = 'http://localhost:4200';
const PAGE_URL = `${BASE_URL}/plugins/backend-configuration-pn/compliance-report`;

/**
 * Lands in Rapport with the report ALREADY fetched. The default preset is
 * "År til dato", and under #1185 there is no fetch button outside "Sæt
 * periode": the page fetched Oversigt on entry, and the switch to Rapport
 * replays that trigger to the recreated child, which queries
 * `eform-columns` on its own. The response is awaited on the mode click.
 */
async function goToRapport(page: Page): Promise<void> {
  await page.goto(BASE_URL);
  // `login()` returns once `#newEFormBtn` is visible: no fixed sleep needed.
  await new LoginPage(page).login();
  await page.goto(PAGE_URL);
  await page.locator('#complianceFilterProperty').waitFor({ state: 'visible', timeout: SLOW_API_TIMEOUT });
  const response = waitForApiResponse(
    page,
    'the Rapport eform-columns query',
    (r) => r.url().includes('/compliance-report/eform-columns'),
    SLOW_API_TIMEOUT,
  );
  // Awaited after the click, which can throw first.
  ignoreUnhandledRejections(response);
  await page.locator('#complianceMode-report').click();
  await expect(page.locator('#complianceMode-report')).toHaveAttribute('aria-pressed', 'true', {
    timeout: UI_TIMEOUT,
  });
  await response;
}

/** The report has rendered: the shell's spinner replaces the view while `loading` is true. */
async function awaitRapportRendered(page: Page): Promise<void> {
  await expect(page.locator('#complianceCasesRoot')).toHaveAttribute('aria-busy', 'false', {
    timeout: SLOW_API_TIMEOUT,
  });
}

const EFORM_COLUMNS_ROUTE = '**/api/backend-configuration-pn/compliance-report/eform-columns';

/**
 * ONE report headline answered on TWO eForms — the #1276 shape, which #1188
 * rendered as one merged table with `KOMMENTAR` twice. Both eForms here have a
 * `KOMMENTAR` too, so a regression back to one merged grid shows up as two
 * such headers in one table.
 *
 * `Flydelag` carries the two field types #1276 formats: a CheckBox (`f10`,
 * ticked on Tank A, `unchecked` on Tank B) and a Date (`f11`, stored
 * `yyyy-MM-dd`, answered on Tank A only). The eForms are in the server's name
 * order, which the view must keep.
 */
async function routeHeadlineOnTwoEforms(page: Page): Promise<void> {
  const caseRow = (
    complianceId: number,
    checkListId: number,
    title: string,
    cells: Record<string, string>,
  ) => ({
    complianceId, sdkCaseId: 3000 + complianceId, checkListId,
    tags: ['Miljøtilsyn'], propertyId: 9, propertyName: 'Ejendom 9',
    title, taskDate: '2026-05-13', completed: true,
    doneAt: '2026-05-13T10:00:00', workerNames: ['Ann Andersen'],
    cells, imagesCount: 0, images: [],
  });
  const group = {
    headlineTagId: 7,
    headlineName: 'Headline 1',
    tagsCaption: 'Miljøtilsyn',
    templates: [
      {
        checkListId: 509,
        checkListName: 'Flydelag',
        schemaUnavailable: false,
        columns: [
          { key: 'f10', fieldId: 10, label: 'Flydelag OK', fieldType: 'CheckBox' },
          { key: 'f11', fieldId: 11, label: 'Kontroldato', fieldType: 'Date' },
          { key: 'f12', fieldId: 12, label: 'KOMMENTAR', fieldType: 'Comment' },
        ],
        cases: [
          caseRow(1, 509, 'Tank A', { f10: 'checked', f11: '2025-12-01', f12: 'Fin' }),
          caseRow(2, 509, 'Tank B', { f10: 'unchecked', f12: 'Revne' }),
        ],
      },
      {
        checkListId: 511,
        checkListName: 'Omrøring',
        schemaUnavailable: false,
        columns: [
          { key: 'f20', fieldId: 20, label: 'Minutter', fieldType: 'Number' },
          { key: 'f21', fieldId: 21, label: 'KOMMENTAR', fieldType: 'Comment' },
        ],
        cases: [caseRow(3, 511, 'Tank C', { f20: '15', f21: 'Ok' })],
      },
    ],
  };
  await page.route(EFORM_COLUMNS_ROUTE, (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ success: true, message: '', model: [group] }),
    }),
  );
}

const DELETE_ROUTE = '**/api/backend-configuration-pn/compliances/delete/*';

/**
 * #1290 — three COMPLETED logs of one eForm, Tank A / B / C in that order, and
 * a delete endpoint that removes the deleted one from every LATER
 * `eform-columns` response, the way the server's IsDeleted occurrence marker
 * does. Mocked for the same reason as `routeHeadlineOnTwoEforms`: shard `s`
 * seeds no SQL, and a completed, answered case has no browser path. What the
 * server does on delete is pinned by `ComplianceDeleteCompletedLogTests`; what
 * only a browser can prove is the view's side — the permanent-deletion wording,
 * the row gone after the refresh, and the landing on the NEXT row.
 *
 * Returns the compliance ids the delete endpoint was called with.
 */
async function routeThreeLogsWithDelete(page: Page): Promise<number[]> {
  const deleted: number[] = [];
  const caseRow = (complianceId: number, title: string) => ({
    complianceId, sdkCaseId: 4000 + complianceId, checkListId: 509,
    tags: [], propertyId: 9, propertyName: 'Ejendom 9',
    title, taskDate: '2026-05-13', completed: true,
    doneAt: '2026-05-13T10:00:00', workerNames: ['Ann Andersen'],
    cells: { f12: title }, imagesCount: 0, images: [],
  });
  const all = [caseRow(1, 'Tank A'), caseRow(2, 'Tank B'), caseRow(3, 'Tank C')];
  await page.route(EFORM_COLUMNS_ROUTE, (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: true,
        message: '',
        model: [
          {
            headlineTagId: 8,
            headlineName: 'Headline 8',
            tagsCaption: '',
            templates: [
              {
                checkListId: 509,
                checkListName: 'Flydelag',
                schemaUnavailable: false,
                columns: [{ key: 'f12', fieldId: 12, label: 'KOMMENTAR', fieldType: 'Comment' }],
                cases: all.filter((c) => !deleted.includes(c.complianceId)),
              },
            ],
          },
        ],
      }),
    }),
  );
  await page.route(DELETE_ROUTE, (route) => {
    deleted.push(Number(route.request().url().split('/').pop()));
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ success: true, message: 'Opgaven er blevet slettet' }),
    });
  });
  return deleted;
}

test.describe.configure({ mode: 'serial' });

test.describe('Compliance — Rapport view', () => {
  test('renders the meta line above the sections after a fetch', async ({ page }) => {
    await goToRapport(page);

    // The page auto-fetches Oversigt once on load, so `reportVisible` is true
    // and the placeholder is gone to begin with. `setMode('report')`
    // deliberately PRESERVES `reportVisible` (a mode switch must keep replaying
    // or the recreated child renders nothing), so by the time Rapport is on
    // screen the un-fetched state is already unreachable — the mounted child
    // has received the replayed trigger and queried on its own (#1185: there
    // is no button to press outside "Sæt periode"). Asserting an empty
    // pre-fetch state here would fail deterministically.
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);

    await awaitRapportRendered(page);

    const meta = page.locator('#complianceReportMeta');
    await expect(meta).toBeVisible();
    // `Ejendom: Alle  Kalender: Alle  Periode: DD.MM.YYYY – DD.MM.YYYY`.
    // Both dimensions are unfiltered by default, so both read "Alle" — the
    // prototype's word, not "Alle ejendomme".
    await expect(meta).toContainText('Ejendom:');
    await expect(meta).toContainText('Kalender:');
    await expect(meta).toContainText('Periode:');
    // dd.MM.yyyy on both bounds, en-dash separated. The default period is
    // "År til dato", which always has both.
    await expect(meta).toHaveText(/\d{2}\.\d{2}\.\d{4}\s*–\s*\d{2}\.\d{2}\.\d{4}/);
  });

  test('an empty result keeps the meta line and says the filters matched nothing', async ({
    page,
  }) => {
    await goToRapport(page);
    await awaitRapportRendered(page);

    // Guarded rather than asserted outright: shard `s` seeds no SQL, but other
    // suites in it create properties and tasks, so this installation MAY hold
    // answered cases. The empty state is asserted only when there are no
    // sections — and when there are, the far stronger assertion is available:
    // every section is headed by a real report headline (or the fallback).
    const sections = page.locator('.compliance-report__section');
    if ((await sections.count()) === 0) {
      await expect(page.locator('#complianceReportEmpty')).toBeVisible();
      // #1167 §9: the meta line renders for an empty result too.
      await expect(page.locator('#complianceReportMeta')).toBeVisible();
    } else {
      await expect(page.locator('#complianceReportEmpty')).toHaveCount(0);
      await expect(sections.first().locator('.compliance-report__heading')).not.toBeEmpty();
    }
  });

  test('every section is a caption above a headline, with the headline-less section last', async ({
    page,
  }) => {
    await goToRapport(page);
    await awaitRapportRendered(page);

    // Shard `s` seeds no SQL and this spec creates no answered case, so the
    // section count is whatever the other suites in the shard left behind —
    // possibly zero. The structure is asserted for every section that IS
    // there; with none, the empty state is the only thing to check.
    //
    // Deliberately NOT `page.locator('text=Rapportoverskrift')` count 0 (the
    // pre-#1188 placeholder assertion): unquoted `text=` is a case-insensitive
    // substring match, so the fallback heading `Uden rapportoverskrift` — and
    // any real headline containing the word — would trip it.
    const sections = page.locator('.compliance-report__section');
    const count = await sections.count();
    if (count === 0) {
      await expect(page.locator('#complianceReportEmpty')).toBeVisible();
      return;
    }

    for (let i = 0; i < count; i++) {
      const section = sections.nth(i);
      // The caption line (the task's tags joined ` - `) sits ABOVE the heading.
      // It is allowed to be empty — a headline whose tasks carry no other tags
      // has nothing to say there — but the element is always rendered.
      await expect(section.locator('.compliance-report__tag')).toHaveCount(1);
      // The heading is the report headline, `#{id}` or the fallback — never
      // blank, and never the eForm template name (which is what #1167 rendered).
      // ONE per section, however many eForms were answered under it (#1276).
      await expect(section.locator('.compliance-report__heading')).toHaveCount(1);
      await expect(section.locator('.compliance-report__heading')).not.toBeEmpty();

      // Under it, one table per eForm, each titled with the eForm name (#1276).
      // A section is only rendered when it has at least one table.
      const tables = section.locator('.compliance-report__table');
      const tableCount = await tables.count();
      expect(tableCount).toBeGreaterThan(0);
      for (let t = 0; t < tableCount; t++) {
        const subheading = tables.nth(t).locator('.compliance-report__subheading');
        await expect(subheading).toHaveCount(1);
        await expect(subheading).not.toBeEmpty();
      }
    }

    // The fallback section — tasks without a report headline — is keyed
    // `hnone`, headed with the Danish fallback label, and ordered LAST by the
    // server. Present only when the installation holds a headline-less
    // answered case, so both assertions are conditional on its existence.
    const fallback = page.locator('.compliance-report__section[data-section-key="hnone"]');
    if ((await fallback.count()) > 0) {
      await expect(fallback).toHaveCount(1);
      await expect(fallback.locator('.compliance-report__heading')).toHaveText(
        /^\s*Uden rapportoverskrift\s*$/,
      );
      await expect(sections.last()).toHaveAttribute('data-section-key', 'hnone');
    }
  });

  test('a headline answered on two eForms is ONE heading over one table per eForm, with ticks and formatted dates (#1276)', async ({
    page,
  }) => {
    await routeHeadlineOnTwoEforms(page);
    await goToRapport(page);
    await awaitRapportRendered(page);

    // ONE section for the headline: the caption and the bold headline once.
    const section = page.locator('.compliance-report__section[data-section-key="h7"]');
    await expect(section).toHaveCount(1, { timeout: UI_TIMEOUT });
    await expect(section.locator('.compliance-report__tag')).toHaveText(/^\s*Miljøtilsyn\s*$/);
    await expect(section.locator('.compliance-report__heading')).toHaveCount(1);
    await expect(section.locator('.compliance-report__heading')).toHaveText(
      /^\s*Headline 1\s*$/,
    );

    // Under it, TWO tables in the server's order, each under its own eForm
    // name and keyed `h{headline}-c{checkListId}`.
    await expect(section.locator('.compliance-report__subheading')).toHaveText([
      /^\s*Flydelag\s*$/,
      /^\s*Omrøring\s*$/,
    ]);
    const flydelag = section.locator('.compliance-report__table[data-table-key="h7-c509"]');
    const omroering = section.locator('.compliance-report__table[data-table-key="h7-c511"]');
    await expect(flydelag.locator('mtx-grid')).toHaveCount(1);
    await expect(omroering.locator('mtx-grid')).toHaveCount(1);

    // Each grid has ONLY its own eForm's answer columns: one KOMMENTAR per
    // grid (the merged #1188 grid had two), and no column of the other eForm.
    await expect(flydelag.locator('th', { hasText: 'KOMMENTAR' })).toHaveCount(1);
    await expect(omroering.locator('th', { hasText: 'KOMMENTAR' })).toHaveCount(1);
    await expect(flydelag.locator('th', { hasText: 'Minutter' })).toHaveCount(0);
    await expect(omroering.locator('th', { hasText: 'Flydelag OK' })).toHaveCount(0);
    await expect(omroering.locator('tbody tr', { hasText: 'Tank C' })).toHaveCount(1);

    // CheckBox: `checked` is a tick whose accessible name is the translated
    // `Yes`; the answer column's cells are addressed by mtx-grid's
    // `mat-column-answer_{key}` class, inside the row found by its Område.
    const tankA = flydelag.locator('tbody tr', { hasText: 'Tank A' });
    const tankB = flydelag.locator('tbody tr', { hasText: 'Tank B' });
    const tickedCell = tankA.locator('td.mat-column-answer_f10');
    await expect(tickedCell.getByRole('img', { name: 'Ja' })).toBeVisible();
    await expect(tickedCell).not.toContainText('checked');
    // `unchecked` is not shown at all: no tick, no text.
    const uncheckedCell = tankB.locator('td.mat-column-answer_f10');
    await expect(uncheckedCell.getByRole('img')).toHaveCount(0);
    await expect(uncheckedCell).toHaveText(/^\s*$/);
    // And neither token survives anywhere in the table.
    await expect(flydelag.locator('tbody')).not.toContainText(/\b(un)?checked\b/);

    // Date: `yyyy-MM-dd` reads `dd.MM.yyyy`, the fixed `Udført dato`
    // column's format; an unanswered one is still the en dash in place.
    await expect(tankA.locator('td.mat-column-answer_f11')).toHaveText(/^\s*01\.12\.2025\s*$/);
    await expect(tankA.locator('td.mat-column-doneAt')).toHaveText(/^\s*13\.05\.2026\s*$/);
    await expect(tankB.locator('td.mat-column-answer_f11')).toHaveText(/^\s*–\s*$/);
  });

  test('Slet log on a completed log warns it is permanent, deletes it and the row is gone after the refresh (#1290)', async ({
    page,
  }) => {
    const deleted = await routeThreeLogsWithDelete(page);
    await goToRapport(page);
    await awaitRapportRendered(page);

    const table = page.locator('.compliance-report__table[data-table-key="h8-c509"]');
    const tankB = table.locator('tbody tr', { hasText: 'Tank B' });
    await expect(tankB).toHaveCount(1, { timeout: UI_TIMEOUT });

    await tankB.locator('.compliance-report__delete').click();
    // A completed log takes its answers and photos with it — the dialog says so.
    await expect(page.locator('#complianceReportDeleteConfirmText')).toContainText('svar og billeder', {
      timeout: UI_TIMEOUT,
    });

    const refresh = waitForApiResponse(
      page,
      'the Rapport refresh after the delete',
      (r) => r.url().includes('/compliance-report/eform-columns'),
      SLOW_API_TIMEOUT,
    );
    ignoreUnhandledRejections(refresh);
    await page.locator('#complianceReportDeleteConfirmBtn').click();
    await refresh;
    await awaitRapportRendered(page);

    expect(deleted).toEqual([2]);
    await expect(tankB).toHaveCount(0, { timeout: UI_TIMEOUT });
    await expect(table.locator('tbody tr', { hasText: 'Tank A' })).toHaveCount(1);
    await expect(table.locator('tbody tr', { hasText: 'Tank C' })).toHaveCount(1);
  });

  test('after Slet log the NEXT log is highlighted briefly (#1290)', async ({ page }) => {
    await routeThreeLogsWithDelete(page);
    await goToRapport(page);
    await awaitRapportRendered(page);

    const table = page.locator('.compliance-report__table[data-table-key="h8-c509"]');
    await table.locator('tbody tr', { hasText: 'Tank B' }).locator('.compliance-report__delete').click();
    await page.locator('#complianceReportDeleteConfirmBtn').click();

    // Tank C followed Tank B, so Tank C is landed on — and ONLY Tank C.
    const tankC = table.locator('tbody tr', { hasText: 'Tank C' });
    await expect(tankC).toHaveClass(/\brow-highlight-flash\b/, { timeout: UI_TIMEOUT });
    await expect(table.locator('tbody tr.row-highlight-flash')).toHaveCount(1);
    await expect(tankC).toBeInViewport();

    // ~3 s, then the highlight is gone again.
    await expect(tankC).not.toHaveClass(/\brow-highlight-flash\b/, { timeout: 10_000 });
  });

  test('cancelling Slet log deletes nothing and highlights nothing (#1290)', async ({ page }) => {
    const deleted = await routeThreeLogsWithDelete(page);
    await goToRapport(page);
    await awaitRapportRendered(page);

    const table = page.locator('.compliance-report__table[data-table-key="h8-c509"]');
    await table.locator('tbody tr', { hasText: 'Tank B' }).locator('.compliance-report__delete').click();
    await page.locator('#complianceReportDeleteCancelBtn').click();

    await expect(page.locator('#complianceReportDeleteConfirmBtn')).toHaveCount(0, { timeout: UI_TIMEOUT });
    expect(deleted).toEqual([]);
    await expect(table.locator('tbody tr')).toHaveCount(3);
    await expect(table.locator('tbody tr.row-highlight-flash')).toHaveCount(0);
  });
});
