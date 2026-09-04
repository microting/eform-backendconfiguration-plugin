import { test, expect, Locator, Page } from '@playwright/test';
import { LoginPage } from '../../../Page objects/Login.page';

/**
 * Standalone Compliance page — the IMAGE GALLERY behind the Rapport `Billeder`
 * cell (#1168).
 *
 * SCOPE, stated plainly, because half of this suite is conditional and that
 * needs justifying rather than hiding.
 *
 * The gallery renders a case's PICTURE ANSWERS — `FieldValue`s of field type
 * `Picture`, reached through the eForm the worker submitted. There is no
 * browser path that produces one: a picture answer arrives over the SDK's
 * device/gRPC channel, and the calendar's own attachment suite
 * (`w/calendar-attachments.spec.ts`) uploads EVENT files, which are a different
 * entity on a different endpoint and never appear here.
 * `m/calendar-task-card.spec.ts` records the same limitation for the same
 * component opened from the task card.
 *
 * So this spec asserts two things:
 *
 *  1. UNCONDITIONALLY — that no dialog is open before anything is clicked.
 *     That one holds on an empty installation. The second half of that test —
 *     that no images cell ever renders BOTH its static and its interactive
 *     form — needs at least one such cell to mean anything (with none, its sum
 *     is the vacuous `0 === 0`), so it says so in the run log instead of
 *     posing as coverage.
 *
 *  2. CONDITIONALLY — the whole gallery, whenever the installation this runs
 *     against does hold an answered case with a fetchable picture. Shard `s`
 *     seeds no SQL, so that is a real possibility rather than a dead branch,
 *     and when it fires it covers the header, the caption, the counter, the
 *     thumbnail strip, wrap-around, ArrowLeft/ArrowRight and Escape. When it
 *     does not, the test SKIPS with a reason rather than passing silently.
 *
 * Structural notes, each a trap this repo has already paid for:
 *
 *  - `page.goto` BEFORE `LoginPage.login()` — the login page object does not
 *    navigate.
 *  - The page is reached BY URL, never through the sidebar: the plugin's menu
 *    seeding only inserts `MenuItem` rows on a fresh install.
 *  - A regex `hasText` / `toHaveText` matches RAW text, so every anchored
 *    pattern below tolerates the surrounding whitespace Angular emits —
 *    `/^1$/` would never match `" 1 "`.
 *  - The image COUNT is always read from the figcaption (`imageCount()`), never
 *    from the thumbnail strip or the counter. Both of those are `*ngIf`-ed —
 *    the strip additionally on the opener having supplied `_300_` names — so
 *    counting them would let a regression that stops one rendering turn these
 *    tests into a SKIP instead of a failure.
 *  - No helper is added to the shared `Page objects/` directory: it is NOT
 *    synced by devgetchanges.sh and CI pins the frontend to `stable`, so a
 *    change there would need its own earlier frontend PR.
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
  await expect(page.locator('#complianceCasesRoot')).toHaveAttribute('aria-busy', 'false', {
    timeout: 60000,
  });
}

/**
 * The interactive Billeder cells — the ones whose case has at least one
 * FETCHABLE image. A cell with attachments whose file name could not be
 * derived stays a plain `role="img"` count and is deliberately absent here.
 */
function galleryButtons(page: Page): Locator {
  return page.locator('[id^="complianceReportImages-"]');
}

/**
 * Opens the gallery from the first interactive cell, or skips the test.
 * Returns the lightbox root.
 */
async function openFirstGallery(page: Page): Promise<Locator> {
  const buttons = galleryButtons(page);
  const count = await buttons.count();
  test.skip(
    count === 0,
    'No answered case with a fetchable picture answer on this installation — ' +
      'picture answers cannot be produced from a browser (see the file header).',
  );
  await buttons.first().click();
  const lightbox = page.locator('#calendarLightbox');
  await expect(lightbox).toBeVisible({ timeout: 30000 });
  return lightbox;
}

/**
 * How many images the open lightbox holds, read from the figcaption's
 * `Billede {i} af {n}`.
 *
 * The caption is the only count-bearing element that renders UNCONDITIONALLY —
 * its `<figure>` is `*ngIf`-ed on `count > 0` and nothing else. The counter,
 * the nav buttons and the thumbnail strip are all gated (`hasMultiple`, and for
 * the strip also on the opener having supplied `_300_` thumbnail names), and
 * every one of those gates is a thing these tests exist to check. Deriving the
 * count from one of them would mean a regression that stops it rendering
 * silently reduces the count to 0 or 1 and SKIPS the test that would have
 * caught it.
 */
async function imageCount(lightbox: Locator): Promise<number> {
  const caption = lightbox.locator('#calendarLightboxCaption');
  // RAW text, hence the tolerated whitespace.
  await expect(caption).toHaveText(/^\s*Billede\s+\d+\s+af\s+\d+\s*$/);
  const text = (await caption.textContent()) ?? '';
  const match = /af\s+(\d+)\s*$/.exec(text);
  expect(match, `Unparseable caption: ${JSON.stringify(text)}`).not.toBeNull();
  const count = Number(match![1]);
  expect(count).toBeGreaterThan(0);
  return count;
}

test.describe.configure({ mode: 'serial' });

test.describe('Compliance — image gallery', () => {
  test('the Billeder cell is never both a static count and a button', async ({ page }) => {
    await goToRapport(page);
    await fetchReport(page);

    // Nothing has been clicked, so no dialog may exist. This is the assertion
    // that would catch a gallery accidentally opened on render.
    await expect(page.locator('#calendarLightbox')).toHaveCount(0);

    // `.compliance-report__images` is carried by BOTH forms of the cell; the
    // interactive one additionally carries the id prefix. Every cell is
    // therefore exactly one of them, and the two counts must add up.
    const allCells = page.locator('.compliance-report__images');
    const staticCells = page.locator('.compliance-report__images[role="img"]');
    const buttons = galleryButtons(page);

    const [all, statics, interactive] = await Promise.all([
      allCells.count(),
      staticCells.count(),
      buttons.count(),
    ]);
    expect(statics + interactive).toBe(all);

    // HONEST SCOPE. With no report rows at all the assertion above is
    // `0 === 0` and the loop below never runs, so nothing about the cell was
    // actually exercised. Say that in the run log rather than let a green tick
    // imply coverage that was not there; the no-dialog check above is the only
    // part of this test that holds on an empty installation.
    if (all === 0) {
      test.info().annotations.push({
        type: 'coverage',
        description:
          'The report rendered no Billeder cell on this installation, so the ' +
          'static-vs-interactive check was vacuous. Only the ' +
          '"no dialog before a click" assertion carried weight.',
      });
      return;
    }

    // Past this point `all > 0`, so the sum above compared real numbers.
    // A static cell must not be focusable — it is a `<span>`, not a control.
    for (let i = 0; i < statics; i++) {
      await expect(staticCells.nth(i)).toHaveJSProperty('tagName', 'SPAN');
    }
  });

  test('opens with a header, a caption and an alt text tied to the case', async ({ page }) => {
    await goToRapport(page);
    await fetchReport(page);
    const lightbox = await openFirstGallery(page);

    // `Billeder · sag {n}`, or `1 billede · sag {n}` for a single image.
    await expect(lightbox.locator('#calendarLightboxTitle')).toHaveText(
      /^\s*(Billeder|1 billede)\s+·\s+sag\s+\d+\s*$/,
    );

    // The close button's accessible name is the gallery-specific one, not the
    // app-wide "Luk".
    await expect(page.locator('#calendarLightboxClose')).toHaveAttribute(
      'aria-label',
      'Luk galleri',
    );

    // `Billede {i} af {n}` under the image, and the same numbers in the alt.
    await expect(lightbox.locator('#calendarLightboxCaption')).toHaveText(
      /^\s*Billede\s+\d+\s+af\s+\d+\s*$/,
    );
    await expect(page.locator('#calendarLightboxImage')).toHaveAttribute(
      'alt',
      /^Billede \d+ af \d+ til sag \d+$/,
    );

    // Every image goes through the authImage pipe, so the resolved src is a
    // data: URI and never a bare /api/ URL that would 401.
    await expect(page.locator('#calendarLightboxImage')).toHaveAttribute('src', /^data:image\//);
    // …and it actually decoded.
    await expect
      .poll(
        () =>
          page.locator('#calendarLightboxImage').evaluate((img: HTMLImageElement) => img.naturalWidth),
        { timeout: 30000 },
      )
      .toBeGreaterThan(0);
  });

  test('one image hides prev, next, the counter and the thumb strip', async ({ page }) => {
    await goToRapport(page);
    await fetchReport(page);
    const lightbox = await openFirstGallery(page);

    // From the caption, NOT from the strip — see imageCount().
    const n = await imageCount(lightbox);
    const thumbs = lightbox.locator('#calendarLightboxThumbs [role="tab"]');

    if (n === 1) {
      // Exactly one image: all four controls are ABSENT FROM THE DOM, which is
      // what `toHaveCount(0)` asserts and `toBeHidden()` would not.
      await expect(lightbox.locator('#calendarLightboxPrev')).toHaveCount(0);
      await expect(lightbox.locator('#calendarLightboxNext')).toHaveCount(0);
      await expect(lightbox.locator('#calendarLightboxCounter')).toHaveCount(0);
      await expect(lightbox.locator('#calendarLightboxThumbs')).toHaveCount(0);
      await expect(lightbox.locator('#calendarLightboxTitle')).toHaveText(
        /^\s*1 billede\s+·\s+sag\s+\d+\s*$/,
      );
    } else {
      // Two or more: all four are present, and the strip is a real tablist.
      // The strip is asserted present because THIS opener — the compliance
      // report — supplies `_300_` names for every renderable image (both lists
      // come out of one filtered pass). An opener that supplies none, such as
      // the calendar's task card, deliberately gets no strip; that caller has
      // no e2e coverage here.
      await expect(lightbox.locator('#calendarLightboxPrev')).toBeVisible();
      await expect(lightbox.locator('#calendarLightboxNext')).toBeVisible();
      await expect(lightbox.locator('#calendarLightboxThumbs')).toHaveAttribute(
        'role',
        'tablist',
      );
      // One thumb per image — the strip is not allowed to lose or duplicate
      // entries, and this is the assertion the count must not be derived from.
      await expect(thumbs).toHaveCount(n);
      const counter = lightbox.locator('#calendarLightboxCounter');
      await expect(counter).toHaveAttribute('aria-live', 'polite');
      // RAW text: the counter is `{{ i }} / {{ n }}` with Angular's own
      // whitespace around each interpolation, so an anchored `/^1 \/ 2$/`
      // would never match.
      await expect(counter).toHaveText(new RegExp(`^\\s*1\\s*/\\s*${n}\\s*$`));
      // Exactly one thumb is selected, and it is the first.
      await expect(thumbs.first()).toHaveAttribute('aria-selected', 'true');
      await expect(
        lightbox.locator('#calendarLightboxThumbs [role="tab"][aria-selected="true"]'),
      ).toHaveCount(1);
    }
  });

  test('navigation wraps, the keyboard drives it, and Escape closes', async ({ page }) => {
    await goToRapport(page);
    await fetchReport(page);
    const lightbox = await openFirstGallery(page);

    // The count comes from the CAPTION, which renders unconditionally. Reading
    // it off the thumbnail strip (or the counter) would mean a regression that
    // stops either rendering silently SKIPS this whole test — wrap-around,
    // arrow keys, Escape and reopen included — instead of failing it. That is
    // doubly true now that the strip may legitimately be absent for an opener
    // that supplies no `_300_` names.
    const n = await imageCount(lightbox);
    test.skip(n < 2, 'The first case with pictures carries only one — nothing to navigate.');

    const thumbs = lightbox.locator('#calendarLightboxThumbs [role="tab"]');
    await expect(thumbs).toHaveCount(n);
    const counter = lightbox.locator('#calendarLightboxCounter');
    const at = (i: number) => new RegExp(`^\\s*${i}\\s*/\\s*${n}\\s*$`);

    // Previous from the FIRST image goes to the last.
    await lightbox.locator('#calendarLightboxPrev').click();
    await expect(counter).toHaveText(at(n));
    await expect(thumbs.nth(n - 1)).toHaveAttribute('aria-selected', 'true');

    // Next from the LAST image goes back to the first.
    await lightbox.locator('#calendarLightboxNext').click();
    await expect(counter).toHaveText(at(1));

    // ArrowRight / ArrowLeft while the dialog is open. The key goes to the
    // overlay, which is what MatDialogRef.keydownEvents() listens on.
    await page.keyboard.press('ArrowRight');
    await expect(counter).toHaveText(at(2));
    await page.keyboard.press('ArrowLeft');
    await expect(counter).toHaveText(at(1));

    // A thumbnail activates its own image without any layout shift — the
    // inactive thumbs already carry the same 2px border, transparent, so the
    // strip's geometry is identical before and after.
    //
    // LAYOUT geometry, never `boundingBox()`. The strip is `overflow-x: auto`
    // and Playwright scrolls its target into view before clicking it, so on a
    // case with enough attachments to overflow the strip the click legitimately
    // moves every thumb's VIEWPORT x — a difference that says nothing about a
    // layout shift and would fail this test for the wrong reason.
    // `offsetLeft`/`offsetTop` are measured against the offset parent and are
    // unaffected by any ancestor's scroll position, so a change in one is a
    // real reflow. Every thumb is measured, not just the first: a border that
    // grew on the ACTIVATED thumb would leave thumb 0 exactly where it was and
    // push the rest along.
    const stripGeometry = () =>
      lightbox
        .locator('#calendarLightboxThumbs')
        .evaluate((strip: HTMLElement) =>
          Array.from(strip.querySelectorAll<HTMLElement>('[role="tab"]')).map((thumb) => ({
            left: thumb.offsetLeft,
            top: thumb.offsetTop,
            width: thumb.offsetWidth,
            height: thumb.offsetHeight,
          })),
        );

    const before = await stripGeometry();
    await thumbs.nth(1).click();
    await expect(counter).toHaveText(at(2));
    const after = await stripGeometry();
    // Guards the guard: an empty strip would make the comparison below vacuous.
    expect(before.length).toBe(n);
    expect(after).toEqual(before);

    // Escape closes it — MatDialog's own default, which is why no `cancel`
    // handler was ported from the prototype.
    await page.keyboard.press('Escape');
    await expect(page.locator('#calendarLightbox')).toHaveCount(0);

    // Reopening starts from the requested index (0) with no residual state:
    // the component is constructed fresh and destroyed on close.
    await galleryButtons(page).first().click();
    await expect(page.locator('#calendarLightbox')).toBeVisible();
    await expect(page.locator('#calendarLightboxCounter')).toHaveText(at(1));
  });
});
