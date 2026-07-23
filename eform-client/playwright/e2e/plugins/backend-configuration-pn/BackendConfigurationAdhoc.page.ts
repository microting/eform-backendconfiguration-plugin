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
 *   - Toolbar filters (`adhoc-filters.component.html`): `#search` (500ms
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
 *     `n/calendar-default-board.spec.ts`). Three `mat-expansion-panel`
 *     sections: `#ny-sektion-ejendommen` (property/area/title/description/
 *     urgent/tags `#ny-sektion-tags`/photos+comments - the ONLY section
 *     expanded by default), `#ny-sektion-tildeling` (assignment - Kun
 *     tildelte/Alle `.btn-group` toggle, NO teams - collapsed by default),
 *     `#ny-sektion-deadline-paamindelse` (deadline/reminders - collapsed by
 *     default). Collapsed sections must be expanded (click their
 *     `mat-expansion-panel-header`) before their fields are interactable -
 *     `expandSection()` handles this.
 *   - Modals: delete (`#adhocDeleteConfirmBtn`/`-CancelBtn`), copy
 *     (`#adhocCopyWithCommentsBtn`/`-WithoutCommentsBtn`/`-CancelBtn`),
 *     complete (`#adhocCompletePerformerSelect` - optional, no PIN field per
 *     F8's documented deviation - `#adhocCompleteConfirmBtn`/`-CancelBtn`).
 *   - Historik (`adhoc-history.component.html`): `#history-view`,
 *     `#history-data-table` (`.history-row` per event, grouped by day). Row
 *     menu button ids start with the task id and end in per-event
 *     discriminators (`adhocHistoryActionMenu-{taskId}-{eventType}-{gi}-{ei}`,
 *     same shape for `adhocHistoryArchiveBtn-`/`-CopyBtn-`/`-DeleteBtn-`) so
 *     a multi-event task never repeats a DOM id - this page object only ever
 *     matches the stable prefixes via `id^=`. Archive is only offered when
 *     the task is completed and not yet archived. This is the ONLY place
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
    // Diagnostic (M5/T-fix-shard-z round 3): the "Historik" tab's own
    // `AdhocHistoryComponent.ngOnInit` fires its `history/index` POST
    // immediately on navigation, before any test code could otherwise
    // observe it - CI has twice shown this tab rendering an empty
    // "Ingen opgaver fundet" table for a task created seconds earlier, with
    // no visible cause in the ASP.NET console log (its controller action
    // wraps every failure into a caught `OperationDataResult(false, ...)`
    // that's never logged to stdout - only to Sentry). Logging the actual
    // response here (mirrors BackendConfigurationPropertyWorkers.page.ts's
    // create-device-user logging) surfaces the real success/model/message
    // in CI output instead of guessing blind.
    const historyResponsePromise = this.page.waitForResponse(
      (r) => r.url().includes('/api/backend-configuration-pn/adhoc/history/index') && r.request().method() === 'POST',
    );
    await this.viewHistoryBtn().click();
    const historyResponse = await historyResponsePromise;
    const resBody = await historyResponse.json().catch(() => null);
    console.log(
      `adhoc history/index: status=${historyResponse.status()}, success=${resBody?.success}, message=${resBody?.message}, total=${resBody?.model?.total}, entities=${resBody?.model?.entities?.length}`,
    );
    await this.historyView().waitFor({ state: 'visible', timeout: 15000 });
  }

  async openNewTask(): Promise<void> {
    await this.newTaskBtn().click();
    await this.drawerRoot().waitFor({ state: 'visible', timeout: 15000 });
  }

  // ----- Toolbar filters -----------------------------------------------------

  searchInput(): Locator {
    // Scoped under #main-list-view: the app shell's own left-nav also has an
    // unrelated `<a id="search">` (entity-search link) sharing this literal
    // id, so an unscoped `#search` locator is a Playwright strict-mode
    // violation (resolves to 2 elements) the moment the nav is rendered.
    return this.mainListView().locator('#search');
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

  /** Expands one of the drawer's three `mat-expansion-panel` sections (id = panel id) if not already open. */
  async expandSection(sectionId: string): Promise<void> {
    const panel = this.page.locator(`#${sectionId}`);
    const classAttr = (await panel.getAttribute('class')) ?? '';
    if (!classAttr.includes('mat-expanded')) {
      await panel.locator('mat-expansion-panel-header').click();
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
   * A task can have several Historik event rows (created/completed/archived
   * each emit their own row - `adhoc-history.component.ts`'s
   * `AdhocTaskHistoryEventModel` is per-EVENT, not per-task), all sharing
   * the same task title. `.first()` keeps this Playwright-strict-mode-safe
   * regardless of how many event rows currently exist for the task.
   */
  historyRow(taskTitle: string): Locator {
    return this.page.locator('#history-data-table .history-row').filter({ hasText: taskTitle }).first();
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
