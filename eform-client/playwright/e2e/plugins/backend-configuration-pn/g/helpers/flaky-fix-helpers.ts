/**
 * Helpers to reduce flakiness in g/ Playwright specs.
 * See docs/superpowers/specs/2026-04-10-g-test-flakiness-design.md
 */

import { expect, Page, Locator, Response } from '@playwright/test';

/**
 * Registers `page.waitForResponse` for each URL pattern BEFORE invoking the
 * click function, then runs the click concurrently with the waits. This
 * eliminates the race where a click triggers a network request that completes
 * before a subsequent `waitForResponse` call starts listening.
 *
 * @param page         Playwright page
 * @param clickFn      Function that performs the click (or other action)
 * @param urlPatterns  Array of substrings or RegExps to match response URLs
 * @param timeout      Per-response timeout in ms (default 60000)
 * @returns            Array of matched Responses in the same order as urlPatterns
 */
export type ResponseMatcher =
  | string
  | RegExp
  | { url: string | RegExp; method?: string };

export async function clickAndWaitForResponses(
  page: Page,
  clickFn: () => Promise<void>,
  urlPatterns: ResponseMatcher[],
  timeout = 60000
): Promise<Response[]> {
  const matches = (r: Response, p: ResponseMatcher): boolean => {
    if (typeof p === 'string') return r.url().includes(p);
    if (p instanceof RegExp) return p.test(r.url());
    const urlOk =
      typeof p.url === 'string' ? r.url().includes(p.url) : p.url.test(r.url());
    const methodOk = p.method ? r.request().method() === p.method : true;
    return urlOk && methodOk;
  };
  const responses = urlPatterns.map((p) =>
    page.waitForResponse((r) => matches(r, p), { timeout })
  );
  const results = await Promise.all([...responses, clickFn()]);
  // Strip the trailing void from clickFn()
  return results.slice(0, urlPatterns.length) as Response[];
}

/**
 * Returns a Locator for a button whose id starts with `buttonIdPrefix`,
 * scoped to a table row containing `rowText`.
 *
 * When `inOverlay=true`, the button is searched inside `.cdk-overlay-container`
 * (used for mat-menu action buttons rendered in a detached CDK overlay).
 *
 * NOTE: When inOverlay=true, the menu must already be opened for the correct
 * row — this helper cannot scope to the row because the overlay is detached
 * from the row's DOM subtree.
 *
 * @param page            Playwright page
 * @param rowText         Text that uniquely identifies the target row
 * @param buttonIdPrefix  Prefix of the button's id attribute
 * @param inOverlay       If true, search inside .cdk-overlay-container
 */
export async function getActionButtonForRow(
  page: Page,
  rowText: string,
  buttonIdPrefix: string,
  inOverlay = false
): Promise<Locator> {
  if (inOverlay) {
    return page
      .locator('.cdk-overlay-container')
      .locator(`[id^="${buttonIdPrefix}"]`)
      .first();
  }
  return page
    .locator('tr')
    .filter({ hasText: rowText })
    .locator(`[id^="${buttonIdPrefix}"]`);
}

/**
 * Waits for an mtx-grid to finish loading:
 *   1. `mat-progress-bar` is hidden (if present on the page)
 *   2. If `expectedCount` is given, the grid has exactly that many body rows
 *      Otherwise, at least the first body row is visible
 *
 * @param page           Playwright page
 * @param expectedCount  Optional exact row count to wait for
 * @param timeout        Timeout in ms (default 30000)
 */
export async function waitForMtxGridRows(
  page: Page,
  expectedCount?: number,
  timeout = 30000
): Promise<void> {
  const progressBar = page.locator('mat-progress-bar');
  if ((await progressBar.count()) > 0) {
    await progressBar.first().waitFor({ state: 'hidden', timeout });
  }

  const rows = page.locator('mtx-grid tbody tr');
  if (expectedCount !== undefined) {
    await expect(rows).toHaveCount(expectedCount, { timeout });
  } else {
    await expect(rows.first()).toBeVisible({ timeout });
  }
}
