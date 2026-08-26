import { Page, Locator } from '@playwright/test';
import { readFileSync } from 'fs';

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
 *     `[pageOnFront]`/`[showPaginator]`. NOTE: mtx-grid keeps its
 *     `mat-paginator` mounted permanently and only CSS-hides it when
 *     `[showPaginator]` is false (`.mat-paginator-hidden`, `display:none` —
 *     `mtxGrid.mjs` template), so specs must assert VISIBILITY, never
 *     element count. NOTE: a SECOND "Vis alle" button belongs to mtx-grid's
 *     own paginator — always use `#taskListShowAllToggle` by id, never text.
 *   - `#taskListBatchAction` — mtx-select (single); ALWAYS renders all 8
 *     options across 3 optgroups (`.ng-optgroup`), matching the mockup
 *     (opgaveliste.html #opgavelisteFilterHandling): "Medarbejdere"/Employees
 *     (assign/reassign/addWorker), "Opgaver"/Tasks (changeEform/addTags/
 *     removeTags/copy), "Slet"/Delete (delete). assign/reassign/addWorker/copy
 *     are DISABLED (`.ng-option-disabled`, non-clickable) unless the property
 *     filter (`#taskListPropertyFilter`) has EXACTLY ONE property selected —
 *     they are never removed from the list, only grayed out.
 *   - Batch modals share `#batchModalTaskList` (task summary), `#batchModalSubmit`
 *     (primary action), `#batchModalCancel` (all five modals — closes with no
 *     result, so selection/grid stay untouched) and — only the two-phase
 *     eForm-change modal — `#batchModalConfirm` (second step, after
 *     `#batchModalSubmit` flips the modal into a confirmation state).
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
    // Toggling a property option (ON or OFF) fires onFiltersChanged ->
    // loadTasks(), an async tasks/index round-trip. Until the response
    // lands, the PREVIOUS grid render (and any row selection) stays
    // visible; on arrival mtx-grid rebinds [data] and loadTasks rebuilds
    // `selection` EMPTY. A caller that clicks a row checkbox right after
    // this method could otherwise race that rebind — the click lands on the
    // stale render and the selection is wiped moments later, leaving the
    // batch dropdown disabled (observed in CI as shard-y DG2's 120s
    // timeout). Await the reload response before returning so the grid the
    // caller sees is the fresh one.
    const reload = this.page.waitForResponse(
      (r) => r.url().includes('/api/backend-configuration-pn/calendar/tasks/index'),
      { timeout: 15000 },
    ).catch(() => null);
    await this.page.locator('#taskListPropertyFilter').click();
    await this.page.locator('.ng-dropdown-panel .ng-option', { hasText: name }).first().click();
    // Multi-select stays open after picking an option — close it explicitly.
    await this.page.keyboard.press('Escape');
    await reload;
    await this.page.waitForTimeout(800);
  }

  /**
   * Clears ALL selected properties via ng-select's clear-all (×) button.
   * Do NOT try to clear by re-clicking the selected option in the panel:
   * CI evidence (shard-y DG2 rounds 1-2 failure snapshots) shows the
   * reclick leaves the chip/value in place in this ng-select build while
   * STILL firing a filters change + tasks reload — the worst of both
   * worlds (filter kept, selection wiped).
   */
  async clearPropertyFilter(): Promise<void> {
    // Same reload-await rationale as selectProperty above.
    const reload = this.page.waitForResponse(
      (r) => r.url().includes('/api/backend-configuration-pn/calendar/tasks/index'),
      { timeout: 15000 },
    ).catch(() => null);
    await this.page.locator('#taskListPropertyFilter .ng-clear-wrapper').click();
    await reload;
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

  /**
   * Ensures the row is SELECTED (every caller means "select", never
   * "toggle"). Verifies the checkbox actually ended up checked and retries:
   * a tasks/index reload rebinding the grid around the click can swallow
   * (or invert) the toggle — CI shard-y DG2 rounds 1-2 both ended with the
   * clicked checkbox focused-but-UNCHECKED and the batch dropdown disabled
   * for the remaining ~100s of the test budget.
   */
  async selectRow(taskName: string): Promise<void> {
    const nativeCheckbox = this.rowCheckbox(taskName).locator('input[type="checkbox"]');
    for (let attempt = 1; attempt <= 3; attempt++) {
      await this.rowCheckbox(taskName).click();
      await this.page.waitForTimeout(300);
      if (await nativeCheckbox.isChecked().catch(() => false)) {
        return;
      }
      await this.page.waitForTimeout(500);
    }
    throw new Error(`Row checkbox for "${taskName}" did not become checked after 3 attempts`);
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

  async openBatchActionPanel(): Promise<void> {
    await this.page.locator('#taskListBatchAction').click();
    await this.page.locator('.ng-dropdown-panel').waitFor({ state: 'visible', timeout: 5000 });
  }

  batchActionOptions(): Locator {
    return this.page.locator('.ng-dropdown-panel .ng-option');
  }

  batchActionGroups(): Locator {
    return this.page.locator('.ng-dropdown-panel .ng-optgroup');
  }

  batchActionOption(labelRegex: RegExp): Locator {
    return this.batchActionOptions().filter({ hasText: labelRegex }).first();
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
   * Clicks the shared `#batchModalCancel` button present on all five batch
   * modals (`btn-cancel` in every modal template; the id was added
   * specifically so cancel-flow specs don't have to fall back to a
   * class/text selector). Calls `hide()` -> `dialogRef.close()` with no
   * result, which `openBatchModal`'s `afterClosed()` subscriber treats as
   * falsy — selection is NOT cleared and `loadTasks()` is NOT re-run (see
   * `task-list-page.component.ts`), so callers should expect the grid and
   * `#taskListSelectionCount` to be exactly as they were before the modal
   * opened.
   */
  async cancelModal(): Promise<void> {
    await this.page.locator('#batchModalCancel').click();
    await this.page.waitForTimeout(500);
  }

  /**
   * Counts `.ng-option-disabled` entries in the currently-open batch-action
   * panel — shared by the gating specs (`x/task-list-page.spec.ts` PP9 and
   * `y/task-list-dropdown-gating.spec.ts`) instead of each redefining the
   * same class-scan loop.
   */
  async countDisabledBatchActions(): Promise<number> {
    const options = this.batchActionOptions();
    const total = await options.count();
    let disabled = 0;
    for (let i = 0; i < total; i++) {
      const cls = (await options.nth(i).getAttribute('class')) ?? '';
      if (cls.includes('ng-option-disabled')) {
        disabled++;
      }
    }
    return disabled;
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
   * Opens the panel of the given in-modal select IDEMPOTENTLY.
   *
   * Why not just `.click()` the trigger: ng-select opens on MOUSEDOWN
   * (`handleMousedown` -> `open()`), its options select on CLICK, and modals
   * here stack multiple `appendTo="body"` selects vertically (reassign:
   * `batchWorkerFromSelect` directly above `batchWorkerToSelect`), with the
   * open panel of one select physically overlapping the trigger of the
   * next — CI rounds 1/2 saw a lingering panel intercept the next trigger
   * click for the full 120s test timeout (WK3).
   *
   * Open-state detection MUST use the inner ng-select's `ng-select-opened`
   * host class, NOT `aria-expanded` on the `mtx-select` host: MtxSelect
   * binds that attribute to `panelOpen` = `!!this.ngSelect.isOpen`, and in
   * the signals-based ng-select v20 `isOpen` is the signal FUNCTION itself
   * (always truthy), so the host's `aria-expanded` is permanently `"true"`
   * whether open or closed (verified in `mtxSelect.mjs` — `get panelOpen()
   * { return !!this.ngSelect.isOpen; }` vs ng-select's own correctly-
   * invoked `'[class.ng-select-opened]': 'isOpen()'` host binding; CI
   * round 3 failed deterministically on every first in-modal select by
   * trusting `aria-expanded`).
   *
   * So: if the target select is genuinely open, reuse its panel. If some
   * OTHER select's panel lingers, dismiss it with a neutral click on the
   * dialog title — NOT Escape, because the surrounding mat-dialog also
   * closes on Escape — then open the target and wait for its panel.
   */
  private async openModalSelect(selectId: string): Promise<void> {
    const select = this.page.locator(`#${selectId}`);
    const opened = this.page.locator(`#${selectId} ng-select.ng-select-opened`);
    const panel = this.page.locator('.ng-dropdown-panel');
    if ((await opened.count()) > 0) {
      await panel.waitFor({ state: 'visible', timeout: 5000 });
      return;
    }
    if ((await panel.count()) > 0) {
      // A different select's panel is open — ng-select closes on outside
      // click; the dialog title is a safe, always-present neutral target
      // (top of the dialog, never covered by the below-field panels).
      await this.page.locator('mat-dialog-container [mat-dialog-title]').click({ position: { x: 5, y: 5 } });
      await panel.waitFor({ state: 'hidden', timeout: 5000 }).catch(() => {});
    }
    await select.click();
    await panel.waitFor({ state: 'visible', timeout: 5000 });
  }

  /**
   * Post-pick settle: single-selects auto-close their panel on option pick;
   * give that a short grace (residual open state is handled by the next
   * `openModalSelect` anyway) plus the usual model-propagation pause.
   */
  private async settleAfterOptionPick(): Promise<void> {
    await this.page.locator('.ng-dropdown-panel').waitFor({ state: 'hidden', timeout: 2000 }).catch(() => {});
    await this.page.waitForTimeout(400);
  }

  /**
   * Generic ng-select/mtx-select-by-id helper for the fields inside batch
   * modals (`batchWorkerSelect`, `batchWorkerFromSelect`, `batchWorkerToSelect`,
   * `batchEformSelect`, `batchTagsSelect`, `batchCopyPropertySelect`,
   * `batchCopyBoardSelect`, `batchCopyWorkerSelect`). All are single-select
   * (or, for `batchTagsSelect`, multi but the panel closes on outside click
   * which callers don't need here) and close automatically on option pick.
   *
   * Verifies the picked value actually landed in the select's
   * `.ng-value-label` (text-only child — `.ng-value` itself includes the ×
   * clear-icon glyph) and retries once if not, so a pointer-race dropped
   * pick fails loudly at the pick site instead of obscurely at submit.
   */
  async selectModalOption(selectId: string, labelOrRegex: string | RegExp): Promise<void> {
    for (let attempt = 1; attempt <= 2; attempt++) {
      await this.openModalSelect(selectId);
      await this.page.locator('.ng-dropdown-panel .ng-option', { hasText: labelOrRegex }).first().click();
      await this.settleAfterOptionPick();
      const valueText = ((await this.page.locator(`#${selectId} .ng-value-label`).first()
        .innerText().catch(() => '')) ?? '').trim();
      const picked = typeof labelOrRegex === 'string'
        ? valueText.includes(labelOrRegex)
        : labelOrRegex.test(valueText);
      if (picked) {
        return;
      }
    }
    throw new Error(`Picking "${labelOrRegex}" in #${selectId} did not stick after 2 attempts`);
  }

  /**
   * Picks the first (and, for callers like the copy modal's board select
   * where the target property is known to auto-provision exactly one
   * "Default" board, ONLY) option. Only use where the list is known to
   * contain a single deterministic entry — not a stand-in for label-based
   * selection against a multi-entry list.
   */
  async selectModalOptionFirst(selectId: string): Promise<void> {
    await this.openModalSelect(selectId);
    await this.page.locator('.ng-dropdown-panel .ng-option').first().click();
    await this.settleAfterOptionPick();
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
    await this.openModalSelect(selectId);
    const options = this.page.locator('.ng-dropdown-panel .ng-option');
    const count = await options.count();
    for (let i = 0; i < count; i++) {
      const text = ((await options.nth(i).innerText()) ?? '').trim();
      if (text && text !== excludeText.trim()) {
        await options.nth(i).click();
        await this.settleAfterOptionPick();
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

  // ----- Task edit modal (shared with the calendar) ---------------------------------

  /**
   * Opens the per-task edit modal by clicking the grid row's title link
   * (`titleTpl` renders `a.ctl-link`, wired to `onEditTask`). The task list
   * reuses the calendar's `TaskCreateEditModalComponent`, so all
   * `#calendarEvent*` ids from `calendar-ui-enhancements.page.ts` apply here
   * too.
   */
  async openEditModal(taskName: string): Promise<void> {
    await this.row(taskName).locator('a.ctl-link').click();
    await this.page.locator('mat-dialog-container').waitFor({ state: 'visible', timeout: 20000 });
    await this.page.waitForTimeout(800);
  }

  /**
   * Saves the task edit modal and waits for the grid to be repopulated.
   * `onEditTask`'s `afterClosed()` subscriber calls `loadTasks()` when the
   * modal closes with a result, so the tasks/index round-trip — not the
   * dialog detaching — is what makes the new values visible in the grid.
   */
  async saveEditModal(): Promise<void> {
    const reload = this.page.waitForResponse(
      (r) => r.url().includes('/api/backend-configuration-pn/calendar/tasks/index'),
      { timeout: 30000 },
    ).catch(() => null);
    await this.page.locator('#calendarEventSaveBtn').click();
    await this.page.locator('mat-dialog-container').waitFor({ state: 'hidden', timeout: 30000 });
    await reload;
    await this.page.waitForTimeout(800);
  }

  // ----- CSV export -----------------------------------------------------------------

  /**
   * Triggers the CSV export and returns the downloaded file's lines with the
   * UTF-8 BOM stripped. The export is `;`-separated with RFC-4180-style
   * quoting; callers that only need the trailing Active/Compliance columns
   * can safely `split(';').slice(-2)` because those two are always plain
   * `Ja`/`Nej`/`--` tokens and nothing follows them on the line.
   */
  async exportCsvAndReadLines(): Promise<string[]> {
    const downloadPromise = this.page.waitForEvent('download');
    await this.page.locator('#taskListCsvExportBtn').click();
    const download = await downloadPromise;
    const filePath = await download.path();
    if (!filePath) {
      throw new Error('CSV export produced no downloadable file');
    }
    return readFileSync(filePath, 'utf8').replace(/^\uFEFF/, '').split('\n');
  }

  async exportCsvAndGetFilename(): Promise<string> {
    const downloadPromise = this.page.waitForEvent('download');
    await this.page.locator('#taskListCsvExportBtn').click();
    const download = await downloadPromise;
    return download.suggestedFilename();
  }
}
