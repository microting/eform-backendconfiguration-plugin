/**
 * Row action-menu helper for the backend-configuration Playwright suite.
 *
 * Angular Material projects `mat-menu` content into a CDK overlay attached to
 * `<body>`, so the menu items are NOT in the row's DOM subtree — scoping them to
 * the row finds nothing at all, and a page-wide `.first()` can resolve to
 * another row's stale, detached menu item that never becomes clickable. The
 * `action-items-<i>` id is the only reliable link between a row and its menu, so
 * we read that index off the row and address the exact item by it.
 */
import { Locator, Page } from '@playwright/test';
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
  const actionCell = row.locator('[id^="action-items"]').first();
  await actionCell.scrollIntoViewIfNeeded({ timeout: UI_TIMEOUT });
  const actionCellId = await actionCell.getAttribute('id', { timeout: UI_TIMEOUT });
  if (!actionCellId) {
    throw new Error(`${rowDescription} action cell has no id — cannot resolve its action-menu index`);
  }
  await actionCell.locator('#actionMenu').click({ timeout: UI_TIMEOUT });
  await page.locator('.mat-mdc-menu-panel').waitFor({ state: 'visible', timeout: UI_TIMEOUT });

  const rowIndex = actionCellId.replace('action-items-', '');
  return function menuItem(idPrefix: string): Locator {
    return page.locator('.cdk-overlay-container').locator(`#${idPrefix}-${rowIndex}`);
  };
}
