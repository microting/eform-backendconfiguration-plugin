import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';

/**
 * Standalone Compliance page — RAPPORT view (#1167, grouped by report
 * headline since #1188).
 *
 * SCOPE, stated plainly. Rapport's sub-report tables need COMPLETED cases whose
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
 * the caption-above-heading section structure with the headline-less
 * fallback section last.
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
  await new LoginPage(page).login();
  await page.waitForTimeout(2000);
  await page.goto(PAGE_URL);
  await page.locator('#complianceFilterProperty').waitFor({ state: 'visible', timeout: 60000 });
  const response = page.waitForResponse(
    (r) => r.url().includes('/compliance-report/eform-columns'),
    { timeout: 60000 },
  );
  await page.locator('#complianceMode-report').click();
  await expect(page.locator('#complianceMode-report')).toHaveAttribute('aria-pressed', 'true');
  await response;
}

/** The report has rendered: the shell's spinner replaces the view while `loading` is true. */
async function awaitRapportRendered(page: Page): Promise<void> {
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
      await expect(section.locator('.compliance-report__heading')).not.toBeEmpty();
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
});
