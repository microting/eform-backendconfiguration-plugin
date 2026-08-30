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
 *   - `#taskListBatchAction` — mtx-select (single); ALWAYS renders all 10
 *     options across 3 optgroups (`.ng-optgroup`), matching the mockup
 *     (opgaveliste.html #opgavelisteFilterHandling): "Medarbejdere"/Employees
 *     (assign/reassign/addWorker), "Opgaver"/Tasks (changeEform/addTags/
 *     removeTags/setCompliance/copy/changeStartDate), "Slet"/Delete (delete).
 *     assign/reassign/addWorker/copy
 *     are DISABLED (`.ng-option-disabled`, non-clickable) unless the property
 *     filter (`#taskListPropertyFilter`) has EXACTLY ONE property selected —
 *     they are never removed from the list, only grayed out.
 *   - `#taskListManageTagsBtn` — opens the SHARED tag-management dialogs
 *     (create / rename / delete / bulk-create). See the "Tag management"
 *     helper block below for the full id map and the two-reload contract.
 *   - Batch modals share `#batchModalTaskList` (task summary), `#batchModalSubmit`
 *     (primary action), `#batchModalCancel` (all seven modals — closes with no
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

  /**
   * Selects ONE property in `#taskListPropertyFilter` and waits for BOTH of
   * the independent async loads the change kicks off.
   *
   * 1. `calendar/tasks/index` — toggling a property option (ON or OFF) fires
   *    onFiltersChanged -> loadTasks(), an async tasks/index round-trip.
   *    Until the response lands, the PREVIOUS grid render (and any row
   *    selection) stays visible; on arrival mtx-grid rebinds [data] and
   *    loadTasks rebuilds `selection` EMPTY. A caller that clicks a row
   *    checkbox right after this method could otherwise race that rebind —
   *    the click lands on the stale render and the selection is wiped moments
   *    later, leaving the batch dropdown disabled (observed in CI as shard-y
   *    DG2's 120s timeout).
   *
   * 2. `calendar/boards/{propertyId}` — a SEPARATE, un-awaited subscription
   *    fired from `onPropertyChanged` (task-list-page.component.ts, which
   *    first resets `boards = []`, then calls `loadBoards`). This one is
   *    load-bearing for the EDIT MODAL, not for the grid: the page passes its
   *    `boards` array into MAT_DIALOG_DATA **at open time**, the modal's board
   *    row is `*ngIf="filteredBoards.length > 0"`, and
   *    `TaskCreateEditModalComponent.onSave()` returns early — toasting
   *    "Select a calendar" but NOT closing the dialog — when `boardControl`
   *    is null. So a spec that opens the edit modal before the boards
   *    response lands gets a modal with no `#calendarEventBoard` row whose
   *    Save button is a silent no-op, surfacing only as an opaque 30s
   *    "waiting for mat-dialog-container to be hidden" timeout inside
   *    `saveEditModal()`. Awaiting the boards response here closes the race
   *    for every current AND future caller.
   *
   *    NB: this race was NOT what failed CI shard-h CI2 on PR #1132, despite
   *    presenting with the same symptom. That was the `folderId: null`
   *    product defect documented on `saveEditModal()` below — the board row
   *    was present and correctly populated in the failure snapshot. The wait
   *    is kept because the race above is real, just latent.
   *
   * Both waits are individually `.catch(() => null)`-guarded so a missing or
   * duplicated call can never hang the helper for longer than its own
   * timeout.
   */
  async selectProperty(name: string): Promise<void> {
    const reload = this.page.waitForResponse(
      (r) => r.url().includes('/api/backend-configuration-pn/calendar/tasks/index'),
      { timeout: 15000 },
    ).catch(() => null);
    const boardsLoaded = this.page.waitForResponse(
      (r) => r.url().includes('/api/backend-configuration-pn/calendar/boards/'),
      { timeout: 15000 },
    ).catch(() => null);
    await this.page.locator('#taskListPropertyFilter').click();
    await this.page.locator('.ng-dropdown-panel .ng-option', { hasText: name }).first().click();
    // Multi-select stays open after picking an option — close it explicitly.
    await this.page.keyboard.press('Escape');
    await Promise.all([reload, boardsLoaded]);
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
    // Same reload-await rationale as selectProperty above. NOTE: deliberately
    // NO boards wait here — clearing takes `filters.propertyIds.length` away
    // from exactly 1, so TaskListFiltersComponent emits `propertyChanged(null)`
    // and `onPropertyChanged` merely empties `boards` WITHOUT issuing a
    // `calendar/boards/{id}` request. Waiting for one would just burn the full
    // timeout on every call.
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

  /**
   * The shared `#batchModalSubmit` primary button, for enabled/disabled
   * assertions. Six of the seven batch modals gate it behind their own
   * `[disabled]="!valid"`, so specs need to read its state and not only click
   * it. The change-start-date modal additionally gates it on a RESOLVED
   * preview (`previewState === 'resolved'`), so it stays disabled while a
   * preview is in flight even once a date is picked — see
   * `pickPastStartDate()` below.
   */
  batchModalSubmitButton(): Locator {
    return this.page.locator('#batchModalSubmit');
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
   * Clicks the shared `#batchModalCancel` button present on all seven batch
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
   * Labels of the batch-action options belonging to the optgroup whose header
   * matches `groupLabel`, in DOM order.
   *
   * ng-select renders group headers and options as FLAT SIBLINGS inside the
   * panel — one `<div>` per item, declared with a static `class="ng-option"`
   * plus `[class.ng-optgroup]="item.children"` and
   * `[class.ng-option]="!item.children"` (verified in
   * `node_modules/@ng-select/ng-select` 20.7.0). The two rendered classes are
   * mutually exclusive because Angular's class BINDING takes precedence over
   * the static attribute, so on a group header `[class.ng-option]="false"`
   * strips the statically declared `ng-option` again. There is
   * no DOM nesting to scope by, so group membership can only be read as
   * "options following this header, up to the next header" — which is what
   * this does. Needed because a spec that only asserts an option EXISTS can't
   * tell whether it landed in the intended optgroup.
   */
  async batchActionLabelsInGroup(groupLabel: string | RegExp): Promise<string[]> {
    const entries = this.page.locator('.ng-dropdown-panel .ng-optgroup, .ng-dropdown-panel .ng-option');
    const total = await entries.count();
    const labels: string[] = [];
    let inGroup = false;
    for (let i = 0; i < total; i++) {
      const entry = entries.nth(i);
      const cls = (await entry.getAttribute('class')) ?? '';
      const text = ((await entry.innerText()) ?? '').trim();
      if (cls.includes('ng-optgroup')) {
        inGroup = typeof groupLabel === 'string' ? text === groupLabel : groupLabel.test(text);
        continue;
      }
      if (inGroup) {
        labels.push(text);
      }
    }
    return labels;
  }

  /**
   * Native `<input type="radio">` behind one of the batch-compliance modal's
   * two `mat-radio-button`s, for `toBeChecked()` assertions.
   *
   * Unlike `mat-slide-toggle` — which in Angular Material 20 is a bare
   * `<button role="switch" aria-checked>` with NO input (see
   * `h/task-list-compliance-inactive.spec.ts` CI2) — `mat-radio-button` DOES
   * still render one: `radio.mjs`'s template has
   * `<input #input class="mdc-radio__native-control" type="radio" ...>`, and
   * the id we set lands on the HOST (`'[attr.id]': 'id'`) while the input gets
   * `id + '-input'`. So the input is addressable as a descendant of `#id`.
   */
  complianceRadioInput(complianceEnabled: boolean): Locator {
    const id = complianceEnabled ? 'batchComplianceOn' : 'batchComplianceOff';
    return this.page.locator(`#${id} input[type="radio"]`);
  }

  /**
   * Picks one of the batch-compliance modal's radio options and verifies it
   * took.
   *
   * The click targets the option's `<label class="mdc-label">`, NOT the
   * `mat-radio-button` host and not the native input. Material's radio
   * template (`radio.mjs`) is
   * `<div mat-internal-form-field><div class="mdc-radio">…<input
   * class="mdc-radio__native-control" [id]="inputId">…</div><label
   * class="mdc-label" [for]="inputId"><ng-content></ng-content></label></div>`
   * — exactly one label per button, carrying the visible text and wired to the
   * input by `for`, so clicking it toggles the radio the same way a real user
   * does. The input itself is unclickable (`opacity: 0`, covered by the
   * circle), and the HOST is the wrong target here: inside the modal's
   * `.d-flex.flex-column` group the host is blockified to the full dialog
   * width while the label only spans its own text, so Playwright's
   * centre-of-element click can land in empty space to the right of the text
   * as soon as a translation is short or the task-summary list makes the
   * dialog wide.
   */
  async pickComplianceOption(complianceEnabled: boolean): Promise<void> {
    const id = complianceEnabled ? 'batchComplianceOn' : 'batchComplianceOff';
    await this.page.locator(`#${id} label.mdc-label`).click();
    await this.page.waitForTimeout(300);
    if (!(await this.complianceRadioInput(complianceEnabled).isChecked().catch(() => false))) {
      throw new Error(`#${id} did not become checked after clicking it`);
    }
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

  // ----- Batch "change start date" modal (#1122) ------------------------------------

  /**
   * Opens `#batchStartDateInput`'s datepicker, steps BACK `monthsBack` months
   * and picks the 1st of that month — i.e. a date guaranteed to be in the
   * PAST, which is exactly what this modal exists to allow.
   *
   * `.mat-calendar-previous-button` is never disabled here because the input
   * carries NO `[min]` binding (deliberately — see
   * `BatchStartDateModalComponent`), which is also why this helper does not
   * filter on `:not(.mat-calendar-body-disabled)` the way
   * `pickFutureCopyDate()` above has to.
   *
   * The day cell is matched on its exact text via `/^1$/` on
   * `.mat-calendar-body-cell-content`; a plain `hasText: '1'` would also match
   * 10-19 and 21/31.
   *
   * Returns the picked date already formatted the way the grid's Start date
   * column renders it ("dd-MM-yyyy", see
   * `TaskListTableComponent.formatStartDate`), so a caller can assert the cell
   * without recomputing the format.
   */
  async pickPastStartDate(monthsBack: number): Promise<string> {
    await this.page.locator('mat-dialog-container mat-datepicker-toggle').click();
    await this.page.locator('.mat-datepicker-content').waitFor({ state: 'visible', timeout: 5000 });
    for (let i = 0; i < monthsBack; i++) {
      await this.page.locator('.mat-calendar-previous-button').click();
      await this.page.waitForTimeout(250);
    }
    await this.page.locator('.mat-calendar-body-cell-content')
      .filter({ hasText: /^1$/ })
      .first()
      .click();
    await this.page.locator('.mat-datepicker-content').waitFor({ state: 'hidden', timeout: 5000 })
      .catch(() => {});
    const now = new Date();
    const picked = new Date(now.getFullYear(), now.getMonth() - monthsBack, 1);
    const mm = (picked.getMonth() + 1).toString().padStart(2, '0');
    return `01-${mm}-${picked.getFullYear()}`;
  }

  /**
   * The preview panel. Its `data-state` attribute mirrors the component's
   * `previewState` (`idle` | `loading` | `resolved` | `failed`) — assert on
   * THAT, never on the panel's translated text.
   */
  startDatePreview(): Locator {
    return this.page.locator('#batchStartDatePreview');
  }

  async startDatePreviewState(): Promise<string> {
    return (await this.startDatePreview().getAttribute('data-state')) ?? '';
  }

  /**
   * Waits for a preview to RESOLVE, which is also what un-disables
   * `#batchModalSubmit` (`get valid()` requires `previewState === 'resolved'`).
   *
   * Generous timeout on purpose: the preview enumerates every occurrence of
   * every selected series server-side, and it only starts after the
   * component's own 400 ms debounce.
   */
  async waitForStartDatePreviewResolved(): Promise<void> {
    await this.page.locator('#batchStartDatePreview[data-state="resolved"]')
      .waitFor({ state: 'attached', timeout: 30000 });
  }

  /** The four resolved-preview count spans, addressed by id (never by text). */
  startDatePreviewCount(which: 'Tasks' | 'Retract' | 'Completed' | 'Overdue'): Locator {
    return this.page.locator(`#batchStartDatePreview${which}`);
  }

  // ----- Task edit modal (shared with the calendar) ---------------------------------

  /**
   * Opens the per-task edit modal by clicking the grid row's title link
   * (`titleTpl` renders `a.ctl-link`, wired to `onEditTask`). The task list
   * reuses the calendar's `TaskCreateEditModalComponent`, so all
   * `#calendarEvent*` ids from `calendar-ui-enhancements.page.ts` apply here
   * too.
   *
   * Deliberately does NOT assert the board row (`#calendarEventBoard`) is
   * present: opening is also legitimate for read-only/cancel flows, and a
   * property that genuinely owns no calendars renders no board row at all —
   * an assertion here would encode "every open must be saveable", which is
   * not intrinsic to opening. The board dependency is enforced where it
   * actually bites, in `saveEditModal()` below, and pre-empted upstream by
   * `selectProperty()`'s boards wait.
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
   *
   * The dialog closes only inside `doSave()`'s success handler (after the
   * update round-trip), so a slow save legitimately keeps it open for a
   * while — hence the full 30s budget is preserved for that case. But there
   * is one failure mode that is INSTANT and permanent: with no
   * `#calendarEventBoard` row the modal's `boardControl` is null,
   * `onSave()` toasts "Select a calendar" and returns WITHOUT closing, so no
   * amount of waiting helps. Detect exactly that (short grace, then probe for
   * the board row) and fail immediately with the cause spelled out, instead
   * of burning 30s on an opaque "waiting for mat-dialog-container to be
   * hidden" TimeoutError like CI shard-h CI2 did on PR #1132. The happy path
   * is untouched: the first `waitFor` resolves the moment the dialog detaches.
   *
   * CURRENTLY UNUSABLE — and it is the product, not this helper. Every save of
   * the edit modal *as opened from the task list* fails server-side:
   * `TaskListPageComponent.onEditTask` passes `folderId: null`
   * (`onEditTask` in task-list-page.component.ts), the modal forwards it verbatim
   * (`onSave` in task-create-edit-modal.component.ts) and
   * `BackendConfigurationTaskWizardService.UpdateTask` does
   * `areaRulePlanning.FolderId = (int)updateModel.FolderId` (:803), throwing
   * "Nullable object must have a value". The PUT answers 200 with
   * `{success:false, message:"ErrorWhileUpdatingCalendarTask"}` and `doSave()`
   * only closes on success. The PUT probe below turns that into an immediate,
   * quotable error instead of a 30s opaque hidden-state timeout (which is
   * exactly how it presented on CI shard h, PR #1132). Until the product bug
   * is fixed, persist edits through the calendar page's copy of the modal,
   * which is handed a real folder id.
   */
  async saveEditModal(): Promise<void> {
    const reload = this.page.waitForResponse(
      (r) => r.url().includes('/api/backend-configuration-pn/calendar/tasks/index'),
      { timeout: 30000 },
    ).catch(() => null);
    // Captured up front so the listener cannot miss a fast round-trip. Read
    // only on the not-closed branch, and `.catch`-guarded so a save that never
    // issues a PUT (the board guard below) can't hang the helper.
    const updatePut = this.page.waitForResponse(
      (r) => r.url().endsWith('/api/backend-configuration-pn/calendar/tasks')
        && r.request().method() === 'PUT',
      { timeout: 30000 },
    ).catch(() => null);
    const dialog = this.page.locator('mat-dialog-container');
    await this.page.locator('#calendarEventSaveBtn').click();
    const closedFast = await dialog.waitFor({ state: 'hidden', timeout: 2000 })
      .then(() => true)
      .catch(() => false);
    if (!closedFast) {
      const failed = await Promise.race([
        updatePut.then(async (r) => {
          if (!r) return null;
          const body = await r.json().catch(() => null);
          return body && body.success === false ? body : null;
        }),
        new Promise<null>((resolve) => setTimeout(() => resolve(null), 5000)),
      ]);
      if (failed) {
        throw new Error(
          'Save did not close the task edit modal because PUT '
          + '/api/backend-configuration-pn/calendar/tasks answered '
          + `success=false: "${failed.message}". TaskCreateEditModalComponent`
          + ".doSave() only calls close() on success, so the dialog stays open "
          + 'forever. Check the eFormAPI log for the underlying exception — a '
          + '`folderId: null` payload (which the task list always sends) throws '
          + '"Nullable object must have a value" in '
          + 'BackendConfigurationTaskWizardService.UpdateTask:803.');
      }
      if ((await this.page.locator('#calendarEventBoard').count()) === 0) {
        throw new Error(
          'Save did not close the task edit modal and the modal has no '
          + '#calendarEventBoard row: TaskCreateEditModalComponent.onSave() hit its '
          + '`boardId == null` guard, toasted "Select a calendar" and returned without '
          + 'closing the dialog. The board row is `*ngIf="filteredBoards.length > 0"` and '
          + '`boards` is handed to MAT_DIALOG_DATA at open time, so the modal was almost '
          + 'certainly opened before GET /api/backend-configuration-pn/calendar/boards/'
          + '{propertyId} landed — select the property via TaskListPage.selectProperty(), '
          + 'which awaits that response, and never open the edit modal with the property '
          + 'filter empty (no filter => no boards => unsaveable modal).');
      }
      // Board row present: an ordinary slow/failed save. Keep the original
      // budget so the caller still gets the familiar hidden-state timeout.
      await dialog.waitFor({ state: 'hidden', timeout: 30000 });
    }
    await reload;
    await this.page.waitForTimeout(800);
  }

  // ----- Tag management (#taskListManageTagsBtn) -------------------------------------

  /**
   * The tag-management dialogs are the SHARED ones from
   * `common/modules/eform-shared-tags` (the same ones the task wizard and the
   * items-planning plannings page open), so their ids are fixed and global:
   *   - list:        `#newTagBtn` (single create), `#newTagsBtn` (bulk create),
   *                  `#tagsModalCloseBtn`, one `#tagName` + `#editTagBtn` +
   *                  `#deleteTagBtn` per row (ids REPEAT per row — always
   *                  address them through `tagRow()`, never on their own).
   *   - create:      `#newTagName`, `#newTagSaveBtn`, `#newTagSaveCancelBtn`
   *   - bulk create: `#newTagsName` (textarea, ONE NAME PER LINE),
   *                  `#newTagsSaveBtn`, `#newTagsSaveCancelBtn`
   *   - rename:      `#tagNameEdit`, `#tagEditSaveBtn`, `#tagEditSaveCancelBtn`
   *   - delete:      `#tagDeleteSaveBtn`, `#tagDeleteSaveCancelBtn`
   *
   * Locked tags (`isLocked`) render NEITHER `#editTagBtn` nor `#deleteTagBtn`,
   * so only ever rename/delete a tag the spec created itself.
   */

  /**
   * Every successful tag mutation runs BOTH `loadTags()` (GET
   * `items-planning-pn/tags` — refills the list dialog, the filter bar and the
   * client-side "Report headline" column) and `loadTasks()` (POST
   * `calendar/tasks/index` — the ONLY thing that refreshes the grid's **Tags**
   * column, whose values are tag NAMES resolved server-side).
   * `TaskListPageComponent.onUpdateTags()` fires them in that order; wait for
   * both or a rename/delete assertion against the grid races the reload.
   *
   * Both waits are `.catch(() => null)`-guarded, in the same spirit as
   * `selectProperty()`, so a mutation that legitimately issues only one of
   * them can never hang a caller for longer than its own timeout.
   */
  private tagMutationReloads(): Promise<unknown> {
    const tagsReload = this.page.waitForResponse(
      (r) => r.url().includes('/api/items-planning-pn/tags') && r.request().method() === 'GET',
      { timeout: 20000 },
    ).catch(() => null);
    const tasksReload = this.page.waitForResponse(
      (r) => r.url().includes('/api/backend-configuration-pn/calendar/tasks/index'),
      { timeout: 20000 },
    ).catch(() => null);
    return Promise.all([tagsReload, tasksReload]);
  }

  manageTagsButton(): Locator {
    return this.page.locator('#taskListManageTagsBtn');
  }

  async openManageTagsDialog(): Promise<void> {
    await this.manageTagsButton().click();
    await this.page.locator('#tagsModalCloseBtn').waitFor({ state: 'visible', timeout: 10000 });
    await this.page.waitForTimeout(300);
  }

  async closeManageTagsDialog(): Promise<void> {
    await this.page.locator('#tagsModalCloseBtn').click();
    await this.page.locator('mat-dialog-container').waitFor({ state: 'hidden', timeout: 10000 });
  }

  /**
   * One row of the tag LIST dialog, matched on its exact `#tagName` text.
   * Scoped to `mat-dialog-container` so it can never collide with the task
   * grid's own `.mat-mdc-row`s underneath the overlay.
   */
  tagRow(name: string): Locator {
    return this.page.locator(`mat-dialog-container .mat-mdc-row:has(#tagName:text-is("${name}"))`);
  }

  async tagNames(): Promise<string[]> {
    return (await this.page.locator('mat-dialog-container #tagName').allInnerTexts())
      .map((t) => t.trim());
  }

  /** Creates ONE tag through `#newTagBtn`; the list dialog stays open. */
  async createTag(name: string): Promise<void> {
    const reloads = this.tagMutationReloads();
    await this.page.locator('#newTagBtn').click();
    await this.page.locator('#newTagName').waitFor({ state: 'visible', timeout: 10000 });
    await this.page.locator('#newTagName').fill(name);
    await this.page.locator('#newTagSaveBtn').click();
    await this.page.locator('#newTagName').waitFor({ state: 'hidden', timeout: 20000 });
    await reloads;
    await this.page.waitForTimeout(500);
  }

  bulkCreateSubmitButton(): Locator {
    return this.page.locator('#newTagsSaveBtn');
  }

  /** Opens the bulk-create dialog and types `rawText` verbatim into its textarea. */
  async openBulkCreateTags(rawText: string): Promise<void> {
    await this.page.locator('#newTagsBtn').click();
    await this.page.locator('#newTagsName').waitFor({ state: 'visible', timeout: 10000 });
    await this.page.locator('#newTagsName').fill(rawText);
    await this.page.waitForTimeout(300);
  }

  /**
   * Submits the bulk-create dialog. Deliberately separate from
   * `openBulkCreateTags` so a spec can assert the Save button's disabled state
   * for a blank/whitespace-only textarea in between — the guard that keeps a
   * trailing newline from posting a `""` name (`PlanningTag.Name` is
   * `[Required]`, and the bulk endpoint wraps its whole create loop in ONE
   * try/catch, so a late invalid name commits the earlier ones and still
   * answers `success = false`).
   */
  async submitBulkCreateTags(): Promise<void> {
    const reloads = this.tagMutationReloads();
    await this.bulkCreateSubmitButton().click();
    await this.page.locator('#newTagsName').waitFor({ state: 'hidden', timeout: 20000 });
    await reloads;
    await this.page.waitForTimeout(500);
  }

  /** Convenience: open + submit in one go (one name per line). */
  async bulkCreateTags(rawText: string): Promise<void> {
    await this.openBulkCreateTags(rawText);
    await this.submitBulkCreateTags();
  }

  async renameTag(from: string, to: string): Promise<void> {
    const reloads = this.tagMutationReloads();
    await this.tagRow(from).locator('#editTagBtn').click();
    await this.page.locator('#tagNameEdit').waitFor({ state: 'visible', timeout: 10000 });
    await this.page.locator('#tagNameEdit').fill(to);
    await this.page.locator('#tagEditSaveBtn').click();
    await this.page.locator('#tagNameEdit').waitFor({ state: 'hidden', timeout: 20000 });
    await reloads;
    await this.page.waitForTimeout(500);
  }

  async deleteTag(name: string): Promise<void> {
    const reloads = this.tagMutationReloads();
    await this.tagRow(name).locator('#deleteTagBtn').click();
    await this.page.locator('#tagDeleteSaveBtn').waitFor({ state: 'visible', timeout: 10000 });
    await this.page.locator('#tagDeleteSaveBtn').click();
    await this.page.locator('#tagDeleteSaveBtn').waitFor({ state: 'hidden', timeout: 20000 });
    await reloads;
    await this.page.waitForTimeout(500);
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
