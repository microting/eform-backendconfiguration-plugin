import { Locator, Page } from '@playwright/test';

/**
 * The WORKER options of the open `#calendarEventAssignee` dropdown — never a team.
 *
 * Since #1295 the task modal's assignee picker is ONE grouped list: a "Teams"
 * group first (omitted when the property has no teams), then the workers group
 * LAST. ng-select renders headers and options as flat siblings; a header is
 * `.ng-optgroup` and does NOT carry the `ng-option` class, and the options that
 * follow it (up to the next header) belong to that group. So the worker options
 * are exactly the options with no group header after them. Header text is not
 * matched because it is translated (da: "Hold" / "Medarbejdere").
 *
 * Same rule as the shared `Page objects/BackendConfigurationCalendar.page.ts`
 * `pickAssigneeWorker`. Use `.first()` / `.nth(i)` / `.filter({hasText})` on the
 * result exactly as on the old `'.ng-dropdown-panel .ng-option'` locator; open the
 * dropdown first.
 */
export function assigneeWorkerOptions(page: Page): Locator {
  return page.locator('.ng-dropdown-panel').locator(
    'xpath=.//div[contains(concat(" ", normalize-space(@class), " "), " ng-option ")]'
      + '[not(following-sibling::div[contains(concat(" ", normalize-space(@class), " "), " ng-optgroup ")])]'
  );
}

/**
 * The TEAM options of the open `#calendarEventAssignee` dropdown: options followed
 * by a later group header (the workers header), i.e. the first group. Empty when
 * the property has no teams.
 */
export function assigneeTeamOptions(page: Page): Locator {
  return page.locator('.ng-dropdown-panel').locator(
    'xpath=.//div[contains(concat(" ", normalize-space(@class), " "), " ng-option ")]'
      + '[following-sibling::div[contains(concat(" ", normalize-space(@class), " "), " ng-optgroup ")]]'
  );
}
