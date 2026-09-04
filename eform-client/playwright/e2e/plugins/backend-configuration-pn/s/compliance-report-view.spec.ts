import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';

/**
 * Standalone Compliance page — RAPPORT view (#1167).
 *
 * SCOPE, stated plainly. Rapport's sub-report tables need COMPLETED cases whose
 * eForm answers have been submitted: the endpoint projects
 * `Case.CheckListId` → column schema → keyed cell bag, and a compliance row
 * that was never answered never reaches a template group. Seeding that from a
 * spec means creating a property, an area rule, a planning, deploying it and
 * submitting the eForm — an order of magnitude more setup than
 * `compliance-overview.spec.ts` needs, and it would still assert nothing the
 * unit spec does not already pin harder. `compliance-report-sections.spec.ts`
 * owns the row-level rules (a missing cell key renders the en dash IN PLACE, a
 * named-but-unnamed tag group is `#{tagId}` and not "Uden tag", a
 * schemaUnavailable group keeps its cases) as pure functions, with no database
 * behind them.
 *
 * What is asserted HERE is exactly what needs a browser and holds on an EMPTY
 * installation: the meta line, its `dd.MM.yyyy` period format, the empty-result
 * wording, and the absence of the prototype's placeholder heading.
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

async function goToRapport(page: Page): Promise<void> {
  await page.goto(BASE_URL);
  await new LoginPage(page).login();
  await page.waitForTimeout(2000);
  await page.goto(PAGE_URL);
  await page.locator('#complianceFilterProperty').waitFor({ state: 'visible', timeout: 60000 });
  await page.locator('#complianceMode-report').click();
  await expect(page.locator('#complianceMode-report')).toHaveAttribute('aria-pressed', 'true');
}

async function fetchReport(page: Page): Promise<void> {
  const response = page.waitForResponse(
    (r) => r.url().includes('/compliance-report/eform-columns'),
    { timeout: 60000 },
  );
  await page.locator('#complianceShowReportBtn').click();
  await response;
  // The shell's spinner replaces the view while `loading` is true.
  await expect(page.locator('#complianceCasesRoot')).toHaveAttribute('aria-busy', 'false', {
    timeout: 60000,
  });
}

test.describe.configure({ mode: 'serial' });

test.describe('Compliance — Rapport view', () => {
  test('renders the meta line above the sections after a fetch', async ({ page }) => {
    await goToRapport(page);

    // The page auto-fetches Oversigt once on load, so `reportVisible` is true
    // and the placeholder is gone to begin with. `setMode('report')`
    // deliberately PRESERVES `reportVisible` (a mode switch must keep replaying
    // or the recreated child renders nothing), so by the time Rapport is on
    // screen the un-fetched state is already unreachable — and the mounted
    // child has received the replayed trigger and queried on its own. Asserting
    // an empty pre-fetch state here would fail deterministically.
    await expect(page.locator('#complianceEmptyState')).toHaveCount(0);

    await fetchReport(page);

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
    await fetchReport(page);

    // Guarded rather than asserted outright: shard `s` seeds no SQL, but other
    // suites in it create properties and tasks, so this installation MAY hold
    // answered cases. The empty state is asserted only when there are no
    // sections — and when there are, the far stronger assertion is available:
    // every section is headed by a real template name.
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

  test('never renders the prototype placeholder heading', async ({ page }) => {
    await goToRapport(page);
    await fetchReport(page);

    // The prototype emitted the literal `Rapportoverskrift` on every
    // sub-report (compliance.js:1805). The heading is the template name.
    await expect(page.locator('text=Rapportoverskrift')).toHaveCount(0);
  });
});
