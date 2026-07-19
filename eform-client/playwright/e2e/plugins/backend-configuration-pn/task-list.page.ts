import { Page, Locator } from '@playwright/test';

/**
 * Page object for the admin-only Task list page
 * (`/plugins/backend-configuration-pn/task-list`). Mirrors the thin,
 * locator-only style of `calendar-ui-enhancements.page.ts` — helpers only
 * locate/interact; assertions live in the specs.
 *
 * DOM reference (host app source, read while writing this suite):
 *   - `#taskListGrid` — mtx-grid (MatTable-based: rows are `.mat-mdc-row`,
 *     header is `.mat-mdc-header-row`, per-column cells carry
 *     `.mat-column-<field>` — field ids: id, property, board, overskrift,
 *     title, eform, assignedTo, tags, taskDate, repeat, status, compliance).
 *   - `#taskListShowAllToggle` — custom toggle button; flips
 *     `[pageOnFront]`/`[showPaginator]`, which mounts/unmounts the grid's
 *     built-in `mat-paginator` entirely (not just CSS-hidden). NOTE: a
 *     SECOND "Vis alle" button belongs to mtx-grid's own paginator — always
 *     use `#taskListShowAllToggle` by id, never text.
 *   - `#taskListBatchAction` — mtx-select (single); options list is
 *     4-wide (changeEform/addTags/removeTags/delete) unless the property
 *     filter (`#taskListPropertyFilter`) has EXACTLY ONE property selected,
 *     in which case assign/reassign/addWorker/copy are also present (8-wide).
 *   - Batch modals share `#batchModalTaskList` (task summary), `#batchModalSubmit`
 *     (primary action) and — only the two-phase eForm-change modal —
 *     `#batchModalConfirm` (second step, after `#batchModalSubmit` flips the
 *     modal into a confirmation state).
 */
export class TaskListPage {
  constructor(private page: Page) {}

  // ----- Navigation ----------------------------------------------------------

  async goto(): Promise<void> {
    await this.page.goto('http://localhost:4200/plugins/backend-configuration-pn/task-list');
    await this.page.waitForTimeout(1500);
    await this.getGrid().waitFor({ state: 'visible', timeout: 30000 });
  }

  /**
   * Navigate via the sidebar menu (mirrors
   * `BackendConfigurationTaskTrackerPage.goToTaskTracker`): click the plugin
   * group button only if the sub-item isn't already visible, then the
   * `backend-configuration-pn-task-list` E2E-id sub-item.
   */
  async goToViaMenu(): Promise<void> {
    const menuBtn = this.menuButton();
    const isVisible = await menuBtn.isVisible();
    if (!isVisible) {
      await this.page.locator('#backend-configuration-pn').click();
    }
    await menuBtn.click();
    await this.page.waitForTimeout(1500);
    await this.getGrid().waitFor({ state: 'visible', timeout: 30000 });
  }

  menuButton(): Locator {
    return this.page.locator('#backend-configuration-pn-task-list');
  }

  // ----- Filters ---------------------------------------------------------------

  async selectProperty(name: string): Promise<void> {
    await this.page.locator('#taskListPropertyFilter').click();
    await this.page.locator('.ng-dropdown-panel .ng-option', { hasText: name }).first().click();
    // Multi-select stays open after picking an option — close it explicitly.
    await this.page.keyboard.press('Escape');
    await this.page.waitForTimeout(800);
  }

  async search(text: string): Promise<void> {
    await this.page.locator('#taskListSearch').fill(text);
    // TaskListFiltersComponent debounces search 300ms before emitting.
    await this.page.waitForTimeout(800);
  }

  // ----- Grid / rows -------------------------------------------------------------

  getGrid(): Locator {
    return this.page.locator('#taskListGrid');
  }

  row(taskName: string): Locator {
    return this.getGrid().locator('.mat-mdc-row').filter({ hasText: taskName });
  }

  rowCheckbox(taskName: string): Locator {
    return this.row(taskName).locator('mat-checkbox');
  }

  async selectRow(taskName: string): Promise<void> {
    await this.rowCheckbox(taskName).click();
    await this.page.waitForTimeout(300);
  }

  async selectAll(): Promise<void> {
    await this.getGrid().locator('.mat-mdc-header-row mat-checkbox').click();
    await this.page.waitForTimeout(300);
  }

  async rowCount(): Promise<number> {
    return this.getGrid().locator('.mat-mdc-row').count();
  }

  columnCell(taskName: string, field: string): Locator {
    return this.row(taskName).locator(`.mat-column-${field}`);
  }

  columnHeader(field: string): Locator {
    return this.getGrid().locator(`.mat-column-${field}`).first();
  }

  getPaginator(): Locator {
    return this.getGrid().locator('mat-paginator');
  }

  async toggleShowAll(): Promise<void> {
    await this.page.locator('#taskListShowAllToggle').click();
    await this.page.waitForTimeout(600);
  }

  // ----- Batch action dropdown + generic modal controls ---------------------------

  async pickBatchAction(labelRegex: RegExp): Promise<void> {
    await this.page.locator('#taskListBatchAction').click();
    await this.page.locator('.ng-dropdown-panel .ng-option', { hasText: labelRegex }).first().click();
    await this.page.waitForTimeout(500);
  }

  getModalTaskList(): Locator {
    return this.page.locator('#batchModalTaskList');
  }

  async submitModal(): Promise<void> {
    await this.page.locator('#batchModalSubmit').click();
    await this.page.waitForTimeout(500);
  }

  async confirmModal(): Promise<void> {
    await this.page.locator('#batchModalConfirm').click();
    await this.page.waitForTimeout(500);
  }

  /**
   * Waits for the mat-dialog host to actually detach. Use after whichever
   * of `submitModal()`/`confirmModal()` is the LAST step for a given batch
   * action (single-phase actions close on submit; changeEform's two-phase
   * modal only closes on confirm) — more robust than a fixed timeout for
   * actions that round-trip a full task Update/Create server-side.
   */
  async waitForModalClosed(): Promise<void> {
    await this.page.locator('mat-dialog-container').waitFor({ state: 'hidden', timeout: 20000 });
  }

  /**
   * Waits for any currently-open ng-select panel to fully detach. Selecting
   * an option normally auto-closes ng-select's panel, but under CI load the
   * close can lag a tick behind the option click's resolution — when a
   * modal chains TWO+ appendTo="body" selects back to back (e.g. the
   * reassign modal's `batchWorkerFromSelect` -> `batchWorkerToSelect`), a
   * not-yet-closed panel from the FIRST select is a body-level sibling that
   * can visually/functionally overlap and intercept pointer events aimed at
   * the SECOND select's trigger. Called after every option pick below so
   * callers never have to reason about this themselves.
   */
  private async waitForDropdownPanelClosed(): Promise<void> {
    await this.page.locator('.ng-dropdown-panel').waitFor({ state: 'hidden', timeout: 5000 }).catch(() => {});
  }

  /**
   * Generic ng-select/mtx-select-by-id helper for the fields inside batch
   * modals (`batchWorkerSelect`, `batchWorkerFromSelect`, `batchWorkerToSelect`,
   * `batchEformSelect`, `batchTagsSelect`, `batchCopyPropertySelect`,
   * `batchCopyBoardSelect`, `batchCopyWorkerSelect`). All are single-select
   * (or, for `batchTagsSelect`, multi but the panel closes on outside click
   * which callers don't need here) and close automatically on option pick.
   */
  async selectModalOption(selectId: string, labelOrRegex: string | RegExp): Promise<void> {
    await this.page.locator(`#${selectId}`).click();
    await this.page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await this.page.locator('.ng-dropdown-panel .ng-option', { hasText: labelOrRegex }).first().click();
    await this.waitForDropdownPanelClosed();
    await this.page.waitForTimeout(400);
  }

  /**
   * Picks the first (and, for callers like the copy modal's board select
   * where the target property is known to auto-provision exactly one
   * "Default" board, ONLY) option. Only use where the list is known to
   * contain a single deterministic entry — not a stand-in for label-based
   * selection against a multi-entry list.
   */
  async selectModalOptionFirst(selectId: string): Promise<void> {
    await this.page.locator(`#${selectId}`).click();
    await this.page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    await this.page.locator('.ng-dropdown-panel .ng-option').first().click();
    await this.waitForDropdownPanelClosed();
    await this.page.waitForTimeout(400);
  }

  /**
   * Picks any option whose label is NOT `excludeText` — content-driven, not
   * index-driven (never assume a fixed position within a DYNAMIC/seeded
   * data list such as the eForms picker; see the project convention against
   * index-based ng-select selection). Returns the picked label. Used where
   * the goal is merely "something different from the current value", not a
   * specific known entity.
   */
  async selectAnyOptionExcept(selectId: string, excludeText: string): Promise<string> {
    await this.page.locator(`#${selectId}`).click();
    await this.page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
    const options = this.page.locator('.ng-dropdown-panel .ng-option');
    const count = await options.count();
    for (let i = 0; i < count; i++) {
      const text = ((await options.nth(i).innerText()) ?? '').trim();
      if (text && text !== excludeText.trim()) {
        await options.nth(i).click();
        await this.waitForDropdownPanelClosed();
        await this.page.waitForTimeout(400);
        return text;
      }
    }
    throw new Error(`No option other than "${excludeText}" found in #${selectId}`);
  }

  /**
   * Opens the standard Angular Material datepicker on `#batchCopyDateInput`
   * and picks the 1st of NEXT month — a date guaranteed to be in the future
   * regardless of what time of day the suite runs (sidesteps the source
   * task's StartHour vs "today" collision documented in
   * BackendConfigurationTaskListService.Copy).
   */
  async pickFutureCopyDate(): Promise<void> {
    await this.page.locator('mat-dialog-container mat-datepicker-toggle').click();
    await this.page.locator('.mat-datepicker-content').waitFor({ state: 'visible', timeout: 5000 });
    await this.page.locator('.mat-calendar-next-button').click();
    await this.page.waitForTimeout(300);
    await this.page.locator('.mat-calendar-body-cell:not(.mat-calendar-body-disabled)').first().click();
    await this.page.waitForTimeout(300);
  }

  // ----- CSV export -----------------------------------------------------------------

  async exportCsvAndGetFilename(): Promise<string> {
    const downloadPromise = this.page.waitForEvent('download');
    await this.page.locator('#taskListCsvExportBtn').click();
    const download = await downloadPromise;
    return download.suggestedFilename();
  }
}
