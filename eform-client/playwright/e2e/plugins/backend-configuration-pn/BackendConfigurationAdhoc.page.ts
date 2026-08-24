import { Page, Locator } from '@playwright/test';
import { selectValueInNgSelector, selectDateOnNewDatePicker } from '../../helper-functions';

/**
 * Page object for the "Adhoc overblik" dashboard
 * (`/plugins/backend-configuration-pn/adhoc-tasks`) - covers the Overblik
 * table + toolbar filters (M5/F5-F6), the "Ny opgave"/"Vis opgave" side
 * drawer (F7), the delete/copy/complete modals (F8) and the Historik tab
 * (F8). DOM reference (source read while writing this suite, M5/T1 -
 * `eform-backendconfiguration-plugin/eform-client/src/app/.../modules/adhoc/`):
 *
 *   - Nav: `#backend-configuration-pn-adhoc` (E2EId seeded by
 *     `EformBackendConfigurationPlugin.cs`, M5/P1).
 *   - Top bar (`adhoc-container.component.html`): Overblik/Historik segment
 *     `#backend-configuration-pn-adhoc-view-list` / `-view-history`; "Ny
 *     opgave" `#backend-configuration-pn-adhoc-new-task`.
 *   - Toolbar filters (`adhoc-filters.component.html`, rendered inline in
 *     the sub-header card via the plugin's `.filters-inline` convention -
 *     NOT inside `#main-list-view`): `#search` (500ms
 *     debounce), `#ejendom` (property mtx-select), `#toolbar-omraade` (area,
 *     dependent on property), `#task-status-filter` (status - bindValue is
 *     the raw status, the VISIBLE label is `"<Åben/Løste/Arkiverede>
 *     (<count>)"`), tag popup `#tag-filter-btn`/`#tag-filter-panel` (OG/
 *     ELLER toggle + inline create/delete, `#toolbar-tag-ny`).
 *   - Table (`adhoc-table.component.html`): mtx-grid → `.mat-mdc-row` rows,
 *     `.mat-column-<field>` cells (fields: title, content, propertyName,
 *     areaName, tags, createdByName, assignedTo, deadline, completedByName,
 *     createdAt, status, actions). Column picker `#column-picker-btn`/
 *     `#column-picker-panel` (disabled unless status filter is `Åben`). Row
 *     `mat-menu` items carry BOTH an `id="adhoc<Action>Btn-{{i}}"` (row
 *     index, not task id) AND a stable class (`.adhocViewBtn`/
 *     `.adhocEditBtn`/`.adhocCopyBtn`/`.adhocDeleteBtn`) - this page object
 *     uses the class, since only one row's menu is open (and thus in the
 *     CDK overlay) at a time. NO archive action in the table - only in
 *     Historik. `.btn-complete-task` renders in the status cell on open
 *     (non-completed, non-archived) rows.
 *   - Drawer (`adhoc-task-drawer.component.html`): a right-anchored,
 *     full-height `MatDialog` (`.adhoc-drawer` root). Reactive-form fields
 *     have no ids but keep their `formControlName` as a literal
 *     `formcontrolname` DOM attribute (repo convention - see
 *     `n/calendar-default-board.spec.ts`). Three flat, always-visible
 *     sections (calendar-module "gcal" idiom - icon-led rows, NO expansion
 *     panels): `#ny-sektion-ejendommen` (property/area/title/description/
 *     urgent/tags `#ny-sektion-tags`/photos+comments),
 *     `#ny-sektion-tildeling` (assignment - Kun tildelte/Alle `.btn-group`
 *     toggle, NO teams), `#ny-sektion-deadline-paamindelse`
 *     (deadline/reminders). `expandSection()` is retained as a no-op-safe
 *     helper (it only clicks when a `mat-expansion-panel-header` actually
 *     exists) so specs keep passing against either markup.
 *   - Drawer photos (#1099/#1100): thumbnails carry
 *     `data-e2e="adhoc-photo-thumb"` (existing/server-side) /
 *     `"adhoc-photo-thumb-queued"` (create-mode previews), each with a
 *     `data-e2e="adhoc-photo-delete-btn"` delete-X (design-spec thumbnail
 *     Variant C). Clicking the X opens the "Slet billede?" confirm modal
 *     (`#adhocPhotoDeleteConfirmBtn`/`-CancelBtn`) - NOTHING is removed
 *     until confirm; cancel/ESC/scrim-click keeps the photo.
 *   - Modals: delete (`#adhocDeleteConfirmBtn`/`-CancelBtn`), copy
 *     (`#adhocCopyWithCommentsBtn`/`-WithoutCommentsBtn`/`-CancelBtn`),
 *     complete (`#adhocCompletePerformerSelect` - optional, no PIN field per
 *     F8's documented deviation - `#adhocCompleteConfirmBtn`/`-CancelBtn`).
 *   - Historik (`adhoc-history.component.html`, #1095 mockup parity):
 *     `#history-view`, `#history-data-table` - a plain 9-column task table,
 *     one `tr.history-row` per Completed/Archived task (the old per-event
 *     day-grouped timeline is gone). Row menu button ids are plain
 *     `adhocHistoryActionMenu-{taskId}` (same shape for
 *     `adhocHistoryArchiveBtn-`/`-CopyBtn-`/`-DeleteBtn-`) - one row per
 *     task means no per-event discriminators anymore; this page object only
 *     ever matches the stable prefixes via `id^=`. Archive is only offered
 *     while the row's status is "Løst" (completed). This is the ONLY place
 *     Archive lives (the table's row menu never offers it).
 */
export class BackendConfigurationAdhocPage {
  constructor(private page: Page) {}

  // ----- Navigation --------------------------------------------------------

  backendConfigurationPnButton(): Locator {
    return this.page.locator('#backend-configuration-pn');
  }

  backendConfigurationPnAdhocButton(): Locator {
    return this.page.locator('#backend-configuration-pn-adhoc');
  }

  async goToAdhoc(): Promise<void> {
    const adhocBtn = this.backendConfigurationPnAdhocButton();
    const isVisible = await adhocBtn.isVisible();
    if (!isVisible) {
      await this.backendConfigurationPnButton().click();
    }
    await adhocBtn.click();
    await this.mainListView().waitFor({ state: 'visible', timeout: 30000 });
  }

  mainListView(): Locator {
    return this.page.locator('#main-list-view');
  }

  viewListBtn(): Locator {
    return this.page.locator('#backend-configuration-pn-adhoc-view-list');
  }

  viewHistoryBtn(): Locator {
    return this.page.locator('#backend-configuration-pn-adhoc-view-history');
  }

  newTaskBtn(): Locator {
    return this.page.locator('#backend-configuration-pn-adhoc-new-task');
  }

  async goToOverview(): Promise<void> {
    await this.viewListBtn().click();
    await this.mainListView().waitFor({ state: 'visible', timeout: 15000 });
  }

  async goToHistory(): Promise<void> {
    // Await the `history/index` round-trip the tab fires on navigation
    // (AdhocHistoryComponent.ngOnInit) so callers see a populated table,
    // not a still-loading one - returning on #history-view visibility alone
    // would let a subsequent row lookup race the data fetch.
    const historyResponsePromise = this.page.waitForResponse(
      (r) => r.url().includes('/api/backend-configuration-pn/adhoc/history/index') && r.request().method() === 'POST',
    );
    await this.viewHistoryBtn().click();
    await historyResponsePromise;
    await this.historyView().waitFor({ state: 'visible', timeout: 15000 });
  }

  async openNewTask(): Promise<void> {
    await this.newTaskBtn().click();
    await this.drawerRoot().waitFor({ state: 'visible', timeout: 15000 });
  }

  // ----- Toolbar filters -----------------------------------------------------

  searchInput(): Locator {
    // Scoped under app-adhoc-filters (which now renders inside the
    // sub-header card, OUTSIDE #main-list-view): the app shell's own
    // left-nav also has an unrelated `<a id="search">` (entity-search link)
    // sharing this literal id, so an unscoped `#search` locator is a
    // Playwright strict-mode violation (resolves to 2 elements) the moment
    // the nav is rendered.
    return this.page.locator('app-adhoc-filters #search');
  }

  async search(text: string): Promise<void> {
    await this.searchInput().fill(text);
    // adhoc-filters.component.ts debounces search 500ms before dispatching.
    await this.page.waitForTimeout(800);
  }

  async selectPropertyFilter(propertyName: string): Promise<void> {
    await selectValueInNgSelector(this.page, '#ejendom', propertyName);
  }

  async selectAreaFilter(areaName: string): Promise<void> {
    await selectValueInNgSelector(this.page, '#toolbar-omraade', areaName);
  }

  /**
   * Opens the toolbar area filter (`#toolbar-omraade`, an `mtx-select` -
   * itself a thin wrapper around `@ng-select/ng-select`'s `NgSelectComponent`,
   * per `MtxSelect`'s template in `@ng-matero/extensions/fesm2022/mtxSelect.mjs`),
   * scrapes the visible option labels straight from the real ng-select
   * dropdown markup (`ng-dropdown-panel` > `.ng-option` - verified against
   * `@ng-select/ng-select/fesm2022/*.mjs`, and the same tokens sibling specs
   * already assert against, e.g. `k/time-registration-dashboard-visibility.spec.ts`'s
   * `getAvailableSiteNames()` and `y/task-list-modal-validation.spec.ts`), then
   * closes it with Escape (that same sibling helper's close idiom) - leaving
   * the current selection untouched, so callers don't need a `''` "clear"
   * step that has no precedent elsewhere in this suite.
   */
  async areaFilterOptions(): Promise<string[]> {
    const filterSelect = this.page.locator('#toolbar-omraade');
    await filterSelect.click();
    const dropdownPanel = this.page.locator('ng-dropdown-panel');
    await dropdownPanel.waitFor({ state: 'visible', timeout: 10000 });
    const names = await dropdownPanel.locator('.ng-option').allInnerTexts();
    await this.page.keyboard.press('Escape');
    await dropdownPanel.waitFor({ state: 'hidden', timeout: 5000 });
    return names.map((n) => n.trim());
  }

  async openAreaCreateModal(): Promise<void> {
    await this.page.locator('#adhocToolbarAreaCreateBtn').click();
    await this.page.locator('#adhocAreaCreateTextarea').waitFor({state: 'visible'});
  }

  async createAreas(names: string[]): Promise<void> {
    await this.openAreaCreateModal();
    await this.page.locator('#adhocAreaCreateTextarea').fill(names.join('\n'));
    await this.page.locator('#adhocAreaCreateSaveBtn').click();
    await this.page.locator('#adhocAreaCreateTextarea').waitFor({state: 'hidden'});
  }

  async openAreaAdminModal(): Promise<void> {
    await this.page.locator('#adhocToolbarAreaManageBtn').click();
    await this.page.locator('#adhocAreaAdminCloseBtn').waitFor({state: 'visible'});
  }

  /** The admin-modal `.area-row` currently displaying `areaName`. */
  private areaRowInAdminModal(areaName: string): Locator {
    return this.page.locator('.area-row', {hasText: areaName});
  }

  /**
   * Renames the area currently shown as `areaName` to `newName`.
   *
   * The row's rename button and its `adhocAreaRenameInput-{id}`/
   * `adhocAreaRenameSaveBtn-{id}` counterparts all key off the SAME
   * `area.id` (component template - frozen), so the id is read straight off
   * this row's own button `id` attribute immediately before clicking it,
   * rather than trusting a value resolved earlier in the test and
   * re-interpolated into a fresh locator afterwards. The id is resolved
   * ONCE, up front, while the name is still plain text, because once the
   * click swaps the row's `<span class="area-name">` for the input,
   * `hasText: areaName` (an ngModel VALUE, not text content) no longer
   * matches that row.
   *
   * History: the CI runs that got stuck waiting on `#adhocAreaRenameInput-2`
   * were ultimately caused by the component template binding `[attr.id]` on
   * the matInput - MatInput's own host binding (`'[id]': 'id'`, defaulting
   * to `mat-input-N`) overwrote it, so the id never existed in the DOM. The
   * template now binds `[id]` (MatInput's @Input) instead.
   */
  async renameAreaInAdminModal(areaName: string, newName: string): Promise<void> {
    const renameBtn = this.areaRowInAdminModal(areaName).locator('button[id^="adhocAreaRenameBtn-"]');
    const btnId = await renameBtn.getAttribute('id');
    const areaId = btnId!.replace('adhocAreaRenameBtn-', '');
    await renameBtn.click();
    const input = this.page.locator(`#adhocAreaRenameInput-${areaId}`);
    await input.waitFor({state: 'visible', timeout: 15000});
    await input.fill(newName);
    await this.page.locator(`#adhocAreaRenameSaveBtn-${areaId}`).click();
  }

  /**
   * Deletes the area currently shown as `areaName`. Unlike rename, delete
   * never needs the row's numeric id at all - `adhocAreaDeleteBtn-{id}` is
   * clicked directly off this row (scoped by the row locator, not
   * reconstructed from a separately-resolved id), and the confirm button is
   * a single global id shared by whichever area is pending deletion.
   */
  async deleteAreaInAdminModal(areaName: string): Promise<void> {
    await this.areaRowInAdminModal(areaName).locator('button[id^="adhocAreaDeleteBtn-"]').click();
    await this.page.locator('#adhocAreaDeleteConfirmBtn').click();
  }

  async closeAreaAdminModal(): Promise<void> {
    await this.page.locator('#adhocAreaAdminCloseBtn').click();
  }

  /** `labelSubstring` matches the visible "<Label> (<count>)" option text - e.g. "Løste". */
  async selectStatusFilter(labelSubstring: string): Promise<void> {
    await selectValueInNgSelector(this.page, '#task-status-filter', labelSubstring);
  }

  tagFilterBtn(): Locator {
    return this.page.locator('#tag-filter-btn');
  }

  tagFilterPanel(): Locator {
    return this.page.locator('#tag-filter-panel');
  }

  async openTagFilterPanel(): Promise<void> {
    if (!(await this.tagFilterPanel().isVisible())) {
      await this.tagFilterBtn().click();
    }
    await this.tagFilterPanel().waitFor({ state: 'visible', timeout: 5000 });
  }

  tagFilterCheckbox(tagName: string): Locator {
    return this.page.locator('#tag-filter-rows .tag-filter-row').filter({ hasText: tagName }).locator('mat-checkbox');
  }

  async toggleTagFilter(tagName: string): Promise<void> {
    await this.openTagFilterPanel();
    await this.tagFilterCheckbox(tagName).click();
    await this.page.waitForTimeout(500);
  }

  async setTagLogic(logic: 'and' | 'or'): Promise<void> {
    await this.openTagFilterPanel();
    const label = logic === 'and' ? 'OG' : 'ELLER';
    await this.tagFilterPanel().locator('.tag-logic-toggle button', { hasText: label }).click();
  }

  async createTagInFilter(tagName: string): Promise<void> {
    await this.openTagFilterPanel();
    await this.page.locator('#toolbar-tag-ny input').fill(tagName);
    await this.page.locator('#toolbar-tag-ny button').click();
    await this.page.waitForTimeout(800);
  }

  // ----- Table ---------------------------------------------------------------

  grid(): Locator {
    return this.page.locator('mtx-grid');
  }

  row(taskTitle: string): Locator {
    return this.grid().locator('.mat-mdc-row').filter({ hasText: taskTitle });
  }

  async rowCount(): Promise<number> {
    return this.grid().locator('.mat-mdc-row').count();
  }

  columnCell(taskTitle: string, field: string): Locator {
    return this.row(taskTitle).locator(`.mat-column-${field}`);
  }

  columnHeader(field: string): Locator {
    return this.grid().locator(`.mat-column-${field}`).first();
  }

  columnPickerBtn(): Locator {
    return this.page.locator('#column-picker-btn');
  }

  columnPickerPanel(): Locator {
    return this.page.locator('#column-picker-panel');
  }

  async openColumnPicker(): Promise<void> {
    await this.columnPickerBtn().click();
    await this.columnPickerPanel().waitFor({ state: 'visible', timeout: 5000 });
  }

  async toggleColumnByLabel(label: string): Promise<void> {
    await this.columnPickerPanel()
      .locator('.column-picker-row')
      .filter({ hasText: label })
      .locator('mat-checkbox')
      .click();
  }

  async closeColumnPicker(): Promise<void> {
    // adhoc-table.component.html's picker only closes via its own "Luk"
    // button (never on outside click), so this must be an explicit click.
    await this.columnPickerPanel().locator('button', { hasText: 'Luk' }).click();
  }

  completeButtonInRow(taskTitle: string): Locator {
    return this.row(taskTitle).locator('.btn-complete-task');
  }

  statusChip(taskTitle: string): Locator {
    return this.row(taskTitle).locator('.cell-status mat-chip');
  }

  async openRowMenu(taskTitle: string): Promise<void> {
    await this.row(taskTitle).locator('button[id^="adhocActionMenu-"]').click();
    await this.page.locator('.mat-mdc-menu-panel').waitFor({ state: 'visible', timeout: 5000 });
  }

  viewMenuItem(): Locator {
    return this.page.locator('.adhocViewBtn');
  }

  editMenuItem(): Locator {
    return this.page.locator('.adhocEditBtn');
  }

  copyMenuItem(): Locator {
    return this.page.locator('.adhocCopyBtn');
  }

  deleteMenuItem(): Locator {
    return this.page.locator('.adhocDeleteBtn');
  }

  // ----- Drawer ----------------------------------------------------------------

  drawerRoot(): Locator {
    return this.page.locator('.adhoc-drawer');
  }

  async closeDrawer(): Promise<void> {
    await this.page.locator('#adhoc-drawer-close').click();
    await this.drawerRoot().waitFor({ state: 'detached', timeout: 10000 });
  }

  drawerCompleteBtn(): Locator {
    return this.page.locator('#adhoc-drawer-complete');
  }

  /**
   * Ensures one of the drawer's three `ny-sektion-*` sections is interactable.
   * Since the drawer's calendar-idiom restyle the sections are flat
   * always-visible blocks (no `mat-expansion-panel`), so this is a no-op
   * unless a collapsible header actually exists inside the section - kept
   * expand-capable so specs calling it stay valid against either markup.
   */
  async expandSection(sectionId: string): Promise<void> {
    const panel = this.page.locator(`#${sectionId}`);
    const header = panel.locator('mat-expansion-panel-header');
    if ((await header.count()) === 0) {
      return; // flat section - always visible, nothing to expand
    }
    const classAttr = (await panel.getAttribute('class')) ?? '';
    if (!classAttr.includes('mat-expanded')) {
      await header.click();
      await this.page.waitForTimeout(400);
    }
  }

  async selectDrawerProperty(propertyName: string): Promise<void> {
    await selectValueInNgSelector(this.page, '.adhoc-drawer mtx-select[formcontrolname="propertyId"]', propertyName);
  }

  async selectDrawerArea(areaName: string): Promise<void> {
    await selectValueInNgSelector(this.page, '.adhoc-drawer mtx-select[formcontrolname="areaId"]', areaName);
  }

  drawerTitleInput(): Locator {
    return this.page.locator('.adhoc-drawer input[formcontrolname="title"]');
  }

  drawerDescriptionTextarea(): Locator {
    return this.page.locator('.adhoc-drawer textarea[formcontrolname="description"]');
  }

  drawerUrgentCheckbox(): Locator {
    return this.page.locator('.adhoc-drawer mat-checkbox[formcontrolname="urgent"]');
  }

  drawerSelectedTagChips(): Locator {
    return this.page.locator('#ny-tags-valgt-wrap mat-chip');
  }

  drawerTagCheckbox(tagName: string): Locator {
    return this.page.locator('#ny-sektion-tags mat-checkbox').filter({ hasText: tagName });
  }

  async selectDrawerTag(tagName: string): Promise<void> {
    await this.drawerTagCheckbox(tagName).click();
  }

  async createDrawerTag(tagName: string): Promise<void> {
    await this.page.locator('#ny-sektion-tags input').fill(tagName);
    await this.page.locator('#ny-sektion-tags button', { hasText: 'Opret tag' }).click();
    await this.page.waitForTimeout(800);
  }

  async setExecutionRule(rule: 'assignedOnly' | 'everyone'): Promise<void> {
    await this.expandSection('ny-sektion-tildeling');
    const label = rule === 'assignedOnly' ? 'Kun tildelte' : 'Alle';
    await this.page.locator('#ny-sektion-tildeling .btn-group button', { hasText: label }).click();
  }

  drawerWorkerCheckbox(workerName: string): Locator {
    return this.page.locator('#ny-sektion-tildeling mat-checkbox').filter({ hasText: workerName });
  }

  async assignDrawerWorker(workerName: string): Promise<void> {
    await this.expandSection('ny-sektion-tildeling');
    await this.drawerWorkerCheckbox(workerName).click();
  }

  deadlineInput(): Locator {
    return this.page.locator('.adhoc-drawer input[formcontrolname="deadline"]');
  }

  async pickDrawerDeadline(year: number, month: number, day: number): Promise<void> {
    await this.expandSection('ny-sektion-deadline-paamindelse');
    await this.page
      .locator('.adhoc-drawer mat-form-field:has(input[formcontrolname="deadline"]) mat-datepicker-toggle')
      .click();
    await this.page.locator('.mat-datepicker-content').waitFor({ state: 'visible', timeout: 5000 });
    await selectDateOnNewDatePicker(this.page, year, month, day);
  }

  // ----- Drawer photos (#1099/#1100) -----------------------------------------

  /**
   * The drawer's hidden photo picker - the `<input type="file">` inside
   * `label.photo-upload-btn` (`adhoc-task-drawer.component.html`).
   * `setInputFiles` works on a hidden input; same idiom as
   * `w/calendar-attachments.spec.ts` with `#calendarEventAttachInput`.
   * In EDIT mode the selection uploads immediately
   * (`onFilesSelected` -> `POST .../adhoc/{id}/photos`), so the round-trip
   * can be awaited; in CREATE mode it only queues a preview
   * (`queuedPhotoThumbs()`).
   */
  photoUploadInput(): Locator {
    return this.page.locator('.adhoc-drawer .photo-upload-btn input[type="file"]');
  }

  /** Existing (server-side) photo thumbnails in the open drawer. */
  photoThumbs(): Locator {
    return this.page.locator('.adhoc-drawer [data-e2e="adhoc-photo-thumb"]');
  }

  /** Queued (create-mode, not yet uploaded) photo preview thumbnails. */
  queuedPhotoThumbs(): Locator {
    return this.page.locator('.adhoc-drawer [data-e2e="adhoc-photo-thumb-queued"]');
  }

  /** The always-visible delete-X on the `index`-th existing photo thumbnail. */
  photoDeleteBtn(index = 0): Locator {
    return this.photoThumbs().nth(index).locator('[data-e2e="adhoc-photo-delete-btn"]');
  }

  /** The delete-X on the `index`-th queued (create-mode) preview thumbnail. */
  queuedPhotoDeleteBtn(index = 0): Locator {
    return this.queuedPhotoThumbs().nth(index).locator('[data-e2e="adhoc-photo-delete-btn"]');
  }

  /** "Slet" in the "Slet billede?" confirm modal. */
  photoDeleteConfirmBtn(): Locator {
    return this.page.locator('#adhocPhotoDeleteConfirmBtn');
  }

  /** "Annuller" in the "Slet billede?" confirm modal. */
  photoDeleteCancelBtn(): Locator {
    return this.page.locator('#adhocPhotoDeleteCancelBtn');
  }

  /**
   * Clicks the delete-X on a photo thumbnail and confirms the "Slet
   * billede?" modal. Existing-photo removal is only STAGED here - it is
   * applied server-side when the drawer is saved (`saveDrawer()`); queued
   * previews disappear immediately.
   */
  async deletePhoto(index = 0, kind: 'existing' | 'queued' = 'existing'): Promise<void> {
    const deleteBtn = kind === 'queued' ? this.queuedPhotoDeleteBtn(index) : this.photoDeleteBtn(index);
    await deleteBtn.click();
    await this.photoDeleteConfirmBtn().waitFor({ state: 'visible', timeout: 5000 });
    await this.photoDeleteConfirmBtn().click();
    await this.photoDeleteConfirmBtn().waitFor({ state: 'detached', timeout: 5000 });
  }

  /** Clicks the delete-X but cancels the confirm modal - the photo must survive. */
  async cancelPhotoDelete(index = 0, kind: 'existing' | 'queued' = 'existing'): Promise<void> {
    const deleteBtn = kind === 'queued' ? this.queuedPhotoDeleteBtn(index) : this.photoDeleteBtn(index);
    await deleteBtn.click();
    await this.photoDeleteCancelBtn().waitFor({ state: 'visible', timeout: 5000 });
    await this.photoDeleteCancelBtn().click();
    await this.photoDeleteCancelBtn().waitFor({ state: 'detached', timeout: 5000 });
  }

  drawerSaveBtn(): Locator {
    return this.page.locator('#adhocDrawerSaveBtn');
  }

  /** Saves the drawer; `waitForCreate` awaits the `POST .../adhoc/` create round-trip (create mode only). */
  async saveDrawer(waitForCreate = false): Promise<void> {
    if (waitForCreate) {
      await Promise.all([
        this.page.waitForResponse(
          (r) => r.url().endsWith('/api/backend-configuration-pn/adhoc/') && r.request().method() === 'POST',
        ),
        this.drawerSaveBtn().click(),
      ]);
    } else {
      await this.drawerSaveBtn().click();
    }
    await this.drawerRoot().waitFor({ state: 'detached', timeout: 15000 });
  }

  // ----- Modals: delete / copy / complete ---------------------------------------

  deleteConfirmBtn(): Locator {
    return this.page.locator('#adhocDeleteConfirmBtn');
  }

  deleteCancelBtn(): Locator {
    return this.page.locator('#adhocDeleteCancelBtn');
  }

  async confirmDelete(): Promise<void> {
    await Promise.all([
      this.page.waitForResponse(
        (r) => r.url().includes('/api/backend-configuration-pn/adhoc/') && r.request().method() === 'DELETE',
      ),
      this.deleteConfirmBtn().click(),
    ]);
  }

  copyWithCommentsBtn(): Locator {
    return this.page.locator('#adhocCopyWithCommentsBtn');
  }

  copyWithoutCommentsBtn(): Locator {
    return this.page.locator('#adhocCopyWithoutCommentsBtn');
  }

  copyCancelBtn(): Locator {
    return this.page.locator('#adhocCopyCancelBtn');
  }

  async selectCompletePerformer(workerName: string): Promise<void> {
    await selectValueInNgSelector(this.page, '#adhocCompletePerformerSelect', workerName);
  }

  completeConfirmBtn(): Locator {
    return this.page.locator('#adhocCompleteConfirmBtn');
  }

  completeCancelBtn(): Locator {
    return this.page.locator('#adhocCompleteCancelBtn');
  }

  async confirmComplete(): Promise<void> {
    await Promise.all([
      this.page.waitForResponse((r) => r.url().includes('/completed') && r.request().method() === 'POST'),
      this.completeConfirmBtn().click(),
    ]);
  }

  // ----- Historik ----------------------------------------------------------------

  historyView(): Locator {
    return this.page.locator('#history-view');
  }

  /**
   * One `tr.history-row` per task (#1095 - `AdhocTaskHistoryRowModel` is
   * per-task, so a title matches at most one row per page). `.first()` is
   * kept purely as strict-mode belt-and-braces against titles that are
   * substrings of one another.
   */
  historyRow(taskTitle: string): Locator {
    return this.page.locator('#history-data-table tr.history-row').filter({ hasText: taskTitle }).first();
  }

  async openHistoryRowMenu(taskTitle: string): Promise<void> {
    await this.historyRow(taskTitle).locator('button[id^="adhocHistoryActionMenu-"]').click();
    await this.page.locator('.mat-mdc-menu-panel').waitFor({ state: 'visible', timeout: 5000 });
  }

  historyArchiveMenuItem(): Locator {
    return this.page.locator('[id^="adhocHistoryArchiveBtn-"]');
  }

  historyCopyMenuItem(): Locator {
    return this.page.locator('[id^="adhocHistoryCopyBtn-"]');
  }

  historyDeleteMenuItem(): Locator {
    return this.page.locator('[id^="adhocHistoryDeleteBtn-"]');
  }

  async archiveFromHistory(taskTitle: string): Promise<void> {
    await this.openHistoryRowMenu(taskTitle);
    await Promise.all([
      this.page.waitForResponse((r) => r.url().includes('/archive') && r.request().method() === 'POST'),
      this.historyArchiveMenuItem().click(),
    ]);
  }
}
