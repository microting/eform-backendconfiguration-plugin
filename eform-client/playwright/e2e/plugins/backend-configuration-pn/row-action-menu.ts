/**
 * Row action-menu helper for the backend-configuration Playwright suite.
 *
 * Angular Material projects `mat-menu` content into a CDK overlay attached to
 * `<body>`, so the menu items are NOT in the row's DOM subtree — scoping them to
 * the row finds nothing at all, and a page-wide `.first()` can resolve to
 * another row's stale, detached menu item that never becomes clickable. The
 * `action-items-<i>` id is the only reliable link between a row and its menu, so
 * we read that index off the row and address the exact item by it.
 *
 * The grid can replace its rows while we work on it: a list refresh is a
 * `forkJoin` of several calls, so a caller that waited for one of them (e.g.
 * `index-device-user`) can still see the old rows for a moment before the
 * table re-renders. Every step below therefore goes through a fresh locator and
 * nothing holds on to an element across awaits. In particular there is no
 * `scrollIntoViewIfNeeded`: it resolves the element once and throws "Element is
 * not attached to the DOM" when the row is swapped underneath it, whereas
 * `click` re-resolves on detach and scrolls by itself.
 */
import { expect, Locator, Page } from '@playwright/test';
import { UI_TIMEOUT } from './wait-helpers';

/** Resolves an item of the open action menu from its id prefix. */
export type ActionMenuItem = (idPrefix: string) => Locator;

/**
 * Opens the action menu of `row` and returns a lookup for its menu items.
 * `rowDescription` only appears in the failure message.
 */
export async function openRowActionMenu(
  page: Page,
  row: Locator,
  rowDescription: string
): Promise<ActionMenuItem> {
  const actionCells = row.locator('[id^="action-items"]');
  await actionCells.first().locator('[id^="actionMenu"]').click({ timeout: UI_TIMEOUT });
  await page.locator('.mat-mdc-menu-panel').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

  // Read the index off the cell whose trigger is open now, so it belongs to the
  // row that is actually rendered and owns the open menu — not to a row the
  // grid replaced between resolving it and clicking.
  const openActionCell = actionCells.filter({ has: page.locator('[id^="actionMenu"][aria-expanded="true"]') });
  await expect(openActionCell, `${rowDescription} action menu must be open on the rendered row`).toHaveCount(1, {
    timeout: UI_TIMEOUT,
  });
  const actionCellId = await openActionCell.getAttribute('id', { timeout: UI_TIMEOUT });
  if (!actionCellId) {
    throw new Error(`${rowDescription} action cell has no id — cannot resolve its action-menu index`);
  }

  const rowIndex = actionCellId.replace('action-items-', '');
  return function menuItem(idPrefix: string): Locator {
    return page.locator('.cdk-overlay-container').locator(`#${idPrefix}-${rowIndex}`);
  };
}
