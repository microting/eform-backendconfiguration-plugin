import {Component, OnDestroy, OnInit} from '@angular/core';
import {Store} from '@ngrx/store';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {TranslateService} from '@ngx-translate/core';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {Subscription} from 'rxjs';
import {format, parseISO, subDays, subMonths} from 'date-fns';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {PARSING_DATE_FORMAT} from 'src/app/common/const';
import {
  AdhocAreaModel,
  AdhocHistoryFiltersModel,
  AdhocTagModel,
  AdhocTaskHistoryRowModel,
  AdhocTaskStatusFilter,
} from '../../../../models';
import {AdhocHistoryFiltrationModel, adhocInitialState, adhocUpdateHistoryFilters, selectAdhocHistoryFilters} from '../../../../state';
import {BackendConfigurationPnAdhocService} from '../../../../services';
import {AdhocStateService} from '../store';
import {AdhocTaskDrawerComponent} from '../adhoc-task-drawer/adhoc-task-drawer.component';
import {AdhocCopyModalComponent} from '../adhoc-copy-modal/adhoc-copy-modal.component';
import {AdhocDeleteModalComponent} from '../adhoc-delete-modal/adhoc-delete-modal.component';

/** Display union derived from the wire `AdhocTaskStatusFilter` - only these two occur in Historik. */
export type AdhocTaskHistoryStatus = 'completed' | 'archived';

const STATUS_FROM_WIRE: Record<number, AdhocTaskHistoryStatus> = {
  [AdhocTaskStatusFilter.Completed]: 'completed',
  [AdhocTaskStatusFilter.Archived]: 'archived',
};

/** Mockup chip labels (#1095): "30 dage" … "12 måneder", "Egen periode" - no 24-month option. */
const PERIOD_CHIP_LABEL_KEYS: Record<AdhocHistoryFiltrationModel['periodPreset'], string> = {
  '30': '30 days',
  '60': '60 days',
  '90': '90 days',
  // Distinct from the pre-existing '6 months'/'12 months' keys (da "6 mdr."
  // - the calendar view's abbreviated labels); the mockup chips spell out
  // "6 måneder"/"12 måneder".
  '6m': '6 months period',
  '12m': '12 months period',
  custom: 'Custom period',
};

const DISPLAY_DATE_FORMAT = 'dd.MM.yyyy';

/** Slice-persisted custom-range format - date-only, like the mockup's native date inputs. */
const ISO_DATE_FORMAT = 'yyyy-MM-dd';

/**
 * "Historik" tab (#1095, dashboard-mockup parity - mockup `#history-view`):
 * a plain task TABLE (one row per Completed/"Løst" or Archived task, fixed
 * CompletedAt-desc sort, no sortable headers, no column picker) replacing
 * the old day-grouped per-event timeline. Around it: the collapsible
 * "Periode og filter" group (period chips 30/60/90 dage + 6/12 måneder +
 * Egen periode, Ejendom/Områder coupling, AND-only tag panel with search),
 * an aria-live "Aktiv periode" line, the six-paragraph help panel, and the
 * mockup pager (Forrige / Side X af Y · N hits / Næste; server paging with
 * the default page size 25).
 *
 * Period math mirrors the mockup's `getHistoryDateRange`: `to` is always
 * today; day presets count n calendar days inclusive of today; month
 * presets shift the same day-of-month back, clamped to the target month's
 * last day (`date-fns` `subMonths` clamps exactly like the mockup's
 * `addCalendarMonthsIso`); a custom range defaults a missing side to the
 * other and swaps when from > to. The resolved range is derived at fetch
 * time - only the preset + raw custom inputs persist in the ngrx slice.
 *
 * Row click opens the drawer in `view` mode (fetching the full
 * `AdhocTaskModel` via `getTask`, since the row itself doesn't carry one).
 * Row menu: Kopier (always), Arkiver (only while status is 'completed'),
 * Slet.
 */
@AutoUnsubscribe()
@Component({
  selector: 'app-adhoc-history',
  templateUrl: './adhoc-history.component.html',
  styleUrls: ['./adhoc-history.component.scss'],
  standalone: false,
})
export class AdhocHistoryComponent implements OnInit, OnDestroy {
  periodPresets: AdhocHistoryFiltrationModel['periodPreset'][] =
    Object.keys(PERIOD_CHIP_LABEL_KEYS) as AdhocHistoryFiltrationModel['periodPreset'][];

  rows: AdhocTaskHistoryRowModel[] = [];
  total = 0;
  pageIndex = 0;
  pageSize = 25;
  filterDetailsOpen = false;
  helpPanelOpen = false;
  tagSearchQuery = '';
  areas: AdhocAreaModel[] = [];

  // Defaulted (not left undefined until the store subscription's first
  // emission) - unlike AdhocFiltersComponent (Overview), whose
  // `currentFilters` getter delegates to AdhocStateService's own
  // constructor-time subscription (already hydrated well before any
  // component's ngOnInit runs), this component owns a fresh, local
  // subscription to the history filters slice set up inside its own
  // ngOnInit. That subscription callback is expected to fire synchronously,
  // but ngOnInit's very next line already reads `this.currentFilters`
  // unguarded (no `?.`, unlike this same field's template usage) - a
  // defaulted initial value removes any dependency on that ordering
  // entirely, so a task created moments earlier is never silently dropped
  // from the "just navigated to Historik" fetch.
  currentFilters: AdhocHistoryFiltrationModel = adhocInitialState.historyFilters;
  private selectAdhocHistoryFilters$ = this.store.select(selectAdhocHistoryFilters);

  filtersSub$: Subscription;
  getHistorySub$: Subscription;
  getTaskSub$: Subscription;
  copySub$: Subscription;
  deleteSub$: Subscription;
  archiveSub$: Subscription;
  areasSub$: Subscription;

  constructor(
    private store: Store,
    private adhocService: BackendConfigurationPnAdhocService,
    public adhocStateService: AdhocStateService,
    private dialog: MatDialog,
    private overlay: Overlay,
    private translate: TranslateService,
  ) {
  }

  ngOnInit(): void {
    this.filtersSub$ = this.selectAdhocHistoryFilters$.subscribe((f) => (this.currentFilters = f));
    if (this.currentFilters.propertyId != null) {
      this.loadAreas(this.currentFilters.propertyId);
    }
    this.updateTable();
  }

  ngOnDestroy(): void {
  }

  // -----------------------------------------------------------------
  // Period math (mockup getHistoryDateRange / addDaysIso / addCalendarMonthsIso)
  // -----------------------------------------------------------------

  private resolveRange(): {from: Date; to: Date} {
    const preset = this.currentFilters.periodPreset;
    const today = new Date();
    if (preset === 'custom') {
      let from = this.currentFilters.customFrom ? parseISO(this.currentFilters.customFrom) : null;
      let to = this.currentFilters.customTo ? parseISO(this.currentFilters.customTo) : null;
      if (!from && !to) {
        return {from: today, to: today};
      }
      from = from ?? to;
      to = to ?? from;
      if (from > to) {
        const swapped = from;
        from = to;
        to = swapped;
      }
      return {from, to};
    }
    if (preset === '6m' || preset === '12m') {
      // subMonths clamps to the target month's last day when the anchor's
      // day-of-month does not exist there (Mar 31 - 1m -> Feb 28/29) - the
      // exact rule of the mockup's addCalendarMonthsIso.
      return {from: subMonths(today, preset === '6m' ? 6 : 12), to: today};
    }
    const days = preset === '30' ? 30 : preset === '60' ? 60 : 90;
    // "n calendar days incl. today" - so back (n - 1) days.
    return {from: subDays(today, days - 1), to: today};
  }

  periodChipLabelKey(preset: AdhocHistoryFiltrationModel['periodPreset']): string {
    return PERIOD_CHIP_LABEL_KEYS[preset];
  }

  /** "Aktiv periode: DD.MM.YYYY — DD.MM.YYYY (inkl. begge dage)." - the aria-live line. */
  activePeriodSummaryLabel(): string {
    const range = this.resolveRange();
    const rangeText = this.translate.instant('{{from}} to {{to}} inclusive of both days', {
      from: format(range.from, DISPLAY_DATE_FORMAT),
      to: format(range.to, DISPLAY_DATE_FORMAT),
    });
    return `${this.translate.instant('Active period')}: ${rangeText}`;
  }

  /** Summary line A value: the chip label, or the resolved range for "Egen periode". */
  filterSummaryLineA(): string {
    if (this.currentFilters.periodPreset === 'custom') {
      const range = this.resolveRange();
      return `${format(range.from, DISPLAY_DATE_FORMAT)} — ${format(range.to, DISPLAY_DATE_FORMAT)}`;
    }
    return this.translate.instant(this.periodChipLabelKey(this.currentFilters.periodPreset));
  }

  /** Summary line B: "Ejendom: … · Områder: … · Tags i historik (OG): …". */
  filterSummaryLineB(): string {
    const propertyName = this.selectedPropertyName() ?? this.translate.instant('All properties');
    const areaName = this.selectedAreaName() ?? this.translate.instant('All areas');
    const tagNames = this.selectedTagNames();
    const tagsText = tagNames.length ? tagNames.join(', ') : this.translate.instant('None selected');
    return `${this.translate.instant('Property')}: ${propertyName}`
      + ` · ${this.translate.instant('Areas')}: ${areaName}`
      + ` · ${this.translate.instant('Tags in history (AND)')}: ${tagsText}`;
  }

  selectedPropertyName(): string | null {
    const id = this.currentFilters.propertyId;
    if (id == null) {
      return null;
    }
    return this.adhocStateService.properties.find((p) => p.id === id)?.name ?? null;
  }

  selectedAreaName(): string | null {
    const id = this.currentFilters.areaId;
    if (id == null) {
      return null;
    }
    return this.areas.find((a) => a.id === id)?.name ?? null;
  }

  /** Selected tag names, Danish-locale sorted (mockup historySelectedTagLabels). */
  selectedTagNames(): string[] {
    return this.adhocStateService.tags
      .filter((tag) => this.currentFilters.tagIds.includes(tag.id))
      .map((tag) => tag.name)
      .sort((a, b) => a.localeCompare(b, 'da'));
  }

  todayFormatted(): string {
    return format(new Date(), DISPLAY_DATE_FORMAT);
  }

  // -----------------------------------------------------------------
  // Data fetch
  // -----------------------------------------------------------------

  updateTable(): void {
    const range = this.resolveRange();
    const model: AdhocHistoryFiltersModel = {
      dateFrom: format(range.from, PARSING_DATE_FORMAT),
      dateTo: format(range.to, PARSING_DATE_FORMAT),
      propertyId: this.currentFilters.propertyId,
      areaId: this.currentFilters.areaId,
      tagIds: [...this.currentFilters.tagIds],
      pageNumber: this.pageIndex + 1,
      pageSize: this.pageSize,
    };
    this.getHistorySub$ = this.adhocService.getHistory(model).subscribe((res) => {
      if (res && res.success && res.model) {
        this.rows = res.model.entities;
        this.total = res.model.total;
      }
    });
  }

  /**
   * Every filter mutation resets to page 0 (mockup: `historyState.page = 0`
   * on every single filter mutator). The merged filters are also applied
   * locally before dispatching so `updateTable` never races the store
   * subscription's (synchronous, but unguaranteed) emission.
   */
  private applyFilters(partial: Partial<AdhocHistoryFiltrationModel>): void {
    this.currentFilters = {...this.currentFilters, ...partial};
    this.store.dispatch(adhocUpdateHistoryFilters(this.currentFilters));
    this.pageIndex = 0;
    this.updateTable();
  }

  onPeriodChange(preset: AdhocHistoryFiltrationModel['periodPreset']): void {
    this.applyFilters({periodPreset: preset});
  }

  get customFromDate(): Date | null {
    return this.currentFilters.customFrom ? parseISO(this.currentFilters.customFrom) : null;
  }

  get customToDate(): Date | null {
    return this.currentFilters.customTo ? parseISO(this.currentFilters.customTo) : null;
  }

  /** Picking either custom date force-switches the active chip to "Egen periode" (mockup behavior). */
  onCustomFromChange(date: Date | null): void {
    this.applyFilters({periodPreset: 'custom', customFrom: date ? format(date, ISO_DATE_FORMAT) : null});
  }

  onCustomToChange(date: Date | null): void {
    this.applyFilters({periodPreset: 'custom', customTo: date ? format(date, ISO_DATE_FORMAT) : null});
  }

  /**
   * Mirrors the mockup's fillHistoryOmraadeSelect: switching to "Alle
   * ejendomme" clears and disables Områder; switching to a concrete
   * property keeps the previously-selected area when it is still valid for
   * the new property, otherwise falls back to "Alle områder".
   */
  onPropertyChange(propertyId: number | null): void {
    if (propertyId == null) {
      this.areas = [];
      this.applyFilters({propertyId: null, areaId: null});
      return;
    }
    this.areasSub$ = this.adhocStateService.getAreasForProperty(propertyId).subscribe((areas) => {
      this.areas = areas;
      const previousAreaId = this.currentFilters.areaId;
      const keptAreaId = previousAreaId != null && areas.some((a) => a.id === previousAreaId) ? previousAreaId : null;
      this.applyFilters({propertyId, areaId: keptAreaId});
    });
  }

  onAreaChange(areaId: number | null): void {
    this.applyFilters({areaId});
  }

  isTagSelected(tagId: number): boolean {
    return this.currentFilters.tagIds.includes(tagId);
  }

  onToggleTag(tagId: number): void {
    const tagIds = this.currentFilters.tagIds.includes(tagId)
      ? this.currentFilters.tagIds.filter((id) => id !== tagId)
      : [...this.currentFilters.tagIds, tagId];
    this.applyFilters({tagIds});
  }

  private loadAreas(propertyId: number): void {
    this.areasSub$ = this.adhocStateService.getAreasForProperty(propertyId).subscribe((areas) => (this.areas = areas));
  }

  // -----------------------------------------------------------------
  // Tag panel (search + selected pills)
  // -----------------------------------------------------------------

  /** Live case-insensitive substring filter (mockup #history-tags-search). */
  filteredTags(): AdhocTagModel[] {
    const query = this.tagSearchQuery.trim().toLowerCase();
    if (!query) {
      return this.adhocStateService.tags;
    }
    return this.adhocStateService.tags.filter((tag) => tag.name.toLowerCase().includes(query));
  }

  selectedTags(): AdhocTagModel[] {
    return this.adhocStateService.tags.filter((tag) => this.currentFilters.tagIds.includes(tag.id));
  }

  // -----------------------------------------------------------------
  // Panel toggles
  // -----------------------------------------------------------------

  toggleFilterDetails(): void {
    this.filterDetailsOpen = !this.filterDetailsOpen;
  }

  toggleHelpPanel(): void {
    this.helpPanelOpen = !this.helpPanelOpen;
  }

  // -----------------------------------------------------------------
  // Status chip derivation (pure, testable without TestBed)
  // -----------------------------------------------------------------

  statusOf(row: AdhocTaskHistoryRowModel): AdhocTaskHistoryStatus {
    return STATUS_FROM_WIRE[row.status] ?? 'completed';
  }

  statusLabelKey(row: AdhocTaskHistoryRowModel): string {
    // 'archived' is the existing event-type key (da "Arkiveret") reused as
    // the chip label; "Løst" needs its own key ('Task resolved status')
    // since the existing 'completed' key is da "Udført" (the column header).
    return this.statusOf(row) === 'archived' ? 'archived' : 'Task resolved status';
  }

  statusChipClass(row: AdhocTaskHistoryRowModel): string {
    return this.statusOf(row) === 'archived' ? 'status-chip status-chip--archived' : 'status-chip status-chip--completed';
  }

  canArchive(row: AdhocTaskHistoryRowModel): boolean {
    return this.statusOf(row) === 'completed';
  }

  // -----------------------------------------------------------------
  // Pager (mockup history-pager: Forrige / Side X af Y · N hits / Næste)
  // -----------------------------------------------------------------

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.total / this.pageSize));
  }

  pagerInfoLabel(): string {
    const pageText = this.translate.instant('Page {{page}} of {{pages}}', {
      page: this.pageIndex + 1,
      pages: this.totalPages,
    });
    const hitsText = this.translate.instant(this.total === 1 ? 'hit' : 'hits');
    return `${pageText} · ${this.total} ${hitsText}`;
  }

  onPagerPrev(): void {
    if (this.pageIndex > 0) {
      this.pageIndex -= 1;
      this.updateTable();
    }
  }

  onPagerNext(): void {
    if (this.pageIndex < this.totalPages - 1) {
      this.pageIndex += 1;
      this.updateTable();
    }
  }

  private openDrawerFor(task: any, mode: 'view' | 'edit'): void {
    this.dialog
      .open(AdhocTaskDrawerComponent, {
        ...dialogConfigHelper(this.overlay, {mode, task}),
        position: {top: '0', right: '0'},
        height: '100vh',
        maxHeight: '100vh',
        panelClass: 'adhoc-drawer-panel',
      })
      .afterClosed()
      .subscribe(() => this.updateTable());
  }

  // -----------------------------------------------------------------
  // Row actions
  // -----------------------------------------------------------------

  onRowClick(row: AdhocTaskHistoryRowModel): void {
    this.getTaskSub$ = this.adhocService.getTask(row.taskId).subscribe((res) => {
      if (res && res.success && res.model) {
        this.openDrawerFor(res.model, 'view');
      }
    });
  }

  onCopy(row: AdhocTaskHistoryRowModel): void {
    this.copySub$ = this.dialog
      .open(AdhocCopyModalComponent, dialogConfigHelper(this.overlay, {id: row.taskId, title: row.taskTitle}))
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.openDrawerFor(result, 'edit');
        }
      });
  }

  onDelete(row: AdhocTaskHistoryRowModel): void {
    this.deleteSub$ = this.dialog
      .open(AdhocDeleteModalComponent, dialogConfigHelper(this.overlay, {id: row.taskId, title: row.taskTitle}))
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.updateTable();
        }
      });
  }

  onArchive(row: AdhocTaskHistoryRowModel): void {
    this.archiveSub$ = this.adhocService.archiveTask(row.taskId).subscribe((res) => {
      if (res && res.success) {
        this.updateTable();
      }
    });
  }
}
