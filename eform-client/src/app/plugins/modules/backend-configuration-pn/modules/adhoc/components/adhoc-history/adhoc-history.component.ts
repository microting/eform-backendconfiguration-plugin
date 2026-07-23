import {Component, OnDestroy, OnInit} from '@angular/core';
import {Store} from '@ngrx/store';
import {MatDialog} from '@angular/material/dialog';
import {Overlay} from '@angular/cdk/overlay';
import {dialogConfigHelper} from 'src/app/common/helpers';
import {Subscription} from 'rxjs';
import {format, subDays, subMonths} from 'date-fns';
import {AutoUnsubscribe} from 'ngx-auto-unsubscribe';
import {PARSING_DATE_FORMAT} from 'src/app/common/const';
import {AdhocAreaModel, AdhocHistoryFiltersModel, AdhocTaskHistoryEventModel} from '../../../../models';
import {AdhocHistoryFiltrationModel, adhocInitialState, adhocUpdateHistoryFilters, selectAdhocHistoryFilters} from '../../../../state';
import {BackendConfigurationPnAdhocService} from '../../../../services';
import {AdhocStateService} from '../store';
import {AdhocTaskDrawerComponent} from '../adhoc-task-drawer/adhoc-task-drawer.component';
import {AdhocCopyModalComponent} from '../adhoc-copy-modal/adhoc-copy-modal.component';
import {AdhocDeleteModalComponent} from '../adhoc-delete-modal/adhoc-delete-modal.component';

interface AdhocHistoryDayGroup {
  dateKey: string;
  events: AdhocTaskHistoryEventModel[];
}

const PERIOD_LABEL_KEYS: Record<AdhocHistoryFiltrationModel['periodPreset'], string> = {
  '30d': 'Last 30 days',
  '60d': 'Last 60 days',
  '90d': 'Last 90 days',
  '6m': 'Last 6 months',
  '12m': 'Last 12 months',
  '24m': 'Last 24 months',
  custom: 'Custom period',
};

/**
 * "Historik" tab (M5/F8) - mockup `#history-view`: period chips (30d/60d/
 * 90d/6m/12m/24m + custom range) resolved client-side into
 * `AdhocHistoryFiltersModel.dateFrom/dateTo`, a collapsible filter-details
 * panel (property/area/tags - AND-only per `AdhocHistoryFiltrationModel`,
 * unlike the Overblik toolbar's toggleable AND/OR), and a timeline grouped
 * by day. `AdhocTaskHistoryEventModel` carries `taskTitle`/`propertyName`/
 * `areaName`/`actorName`/`tagNames` as plain strings already (unlike
 * `AdhocTaskModel`), so - unlike the table (F5) - no id->name resolution
 * is needed here.
 *
 * Row click opens the drawer in `view` mode (fetching the full
 * `AdhocTaskModel` via `getTask`, since the event row itself doesn't carry
 * one). Row menu: Kopier (same copy modal as Overblik -> opens the copy in
 * `edit` mode), Arkiver (only when the event's task is completed and not
 * yet archived), Slet.
 */
@AutoUnsubscribe()
@Component({
  selector: 'app-adhoc-history',
  templateUrl: './adhoc-history.component.html',
  styleUrls: ['./adhoc-history.component.scss'],
  standalone: false,
})
export class AdhocHistoryComponent implements OnInit, OnDestroy {
  periodPresets = Object.keys(PERIOD_LABEL_KEYS) as AdhocHistoryFiltrationModel['periodPreset'][];

  events: AdhocTaskHistoryEventModel[] = [];
  total = 0;
  pageIndex = 0;
  pageSize = 25;
  filterDetailsOpen = false;
  areas: AdhocAreaModel[] = [];
  customFrom: Date | null = null;
  customTo: Date | null = null;

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

  get groupedEvents(): AdhocHistoryDayGroup[] {
    const groups = new Map<string, AdhocTaskHistoryEventModel[]>();
    for (const event of this.events) {
      // occurredAt reaches the client as a Date, not a string: the host
      // frontend's global DateInterceptor converts every ISO-datetime string
      // in every response body (date.interceptor.ts). Calling a string
      // method on it throws inside this getter, which aborts every change-
      // detection pass and freezes the view on its last rendered state.
      const occurred = event.occurredAt;
      const key = occurred instanceof Date ? format(occurred, 'yyyy-MM-dd') : String(occurred || '').slice(0, 10);
      const bucket = groups.get(key) ?? [];
      bucket.push(event);
      groups.set(key, bucket);
    }
    return Array.from(groups.entries()).map(([dateKey, dayEvents]) => ({dateKey, events: dayEvents}));
  }

  periodLabel(preset: AdhocHistoryFiltrationModel['periodPreset']): string {
    return PERIOD_LABEL_KEYS[preset];
  }

  private resolveRange(): {dateFrom: string | null; dateTo: string | null} {
    const preset = this.currentFilters.periodPreset;
    if (preset === 'custom') {
      return {
        dateFrom: this.customFrom ? format(this.customFrom, PARSING_DATE_FORMAT) : null,
        dateTo: this.customTo ? format(this.customTo, PARSING_DATE_FORMAT) : null,
      };
    }
    const now = new Date();
    const daysByPreset: {[key: string]: number} = {'30d': 30, '60d': 60, '90d': 90};
    const monthsByPreset: {[key: string]: number} = {'6m': 6, '12m': 12, '24m': 24};
    if (daysByPreset[preset]) {
      return {dateFrom: format(subDays(now, daysByPreset[preset]), PARSING_DATE_FORMAT), dateTo: null};
    }
    if (monthsByPreset[preset]) {
      return {dateFrom: format(subMonths(now, monthsByPreset[preset]), PARSING_DATE_FORMAT), dateTo: null};
    }
    return {dateFrom: null, dateTo: null};
  }

  updateTable(): void {
    const range = this.resolveRange();
    const model: AdhocHistoryFiltersModel = {
      dateFrom: range.dateFrom,
      dateTo: range.dateTo,
      propertyId: this.currentFilters.propertyId,
      areaId: this.currentFilters.areaId,
      tagIds: [...this.currentFilters.tagIds],
      pageNumber: this.pageIndex + 1,
      pageSize: this.pageSize,
    };
    this.getHistorySub$ = this.adhocService.getHistory(model).subscribe((res) => {
      if (res && res.success && res.model) {
        this.events = res.model.entities;
        this.total = res.model.total;
      }
    });
  }

  private applyFilters(partial: Partial<AdhocHistoryFiltrationModel>): void {
    this.store.dispatch(adhocUpdateHistoryFilters({...this.currentFilters, ...partial}));
    this.pageIndex = 0;
    this.updateTable();
  }

  onPeriodChange(preset: AdhocHistoryFiltrationModel['periodPreset']): void {
    this.applyFilters({periodPreset: preset});
  }

  onCustomRangeChange(): void {
    if (this.currentFilters.periodPreset === 'custom') {
      this.updateTable();
    }
  }

  onPropertyChange(propertyId: number | null): void {
    this.applyFilters({propertyId, areaId: null});
    if (propertyId != null) {
      this.loadAreas(propertyId);
    } else {
      this.areas = [];
    }
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

  toggleFilterDetails(): void {
    this.filterDetailsOpen = !this.filterDetailsOpen;
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

  onRowClick(event: AdhocTaskHistoryEventModel): void {
    this.getTaskSub$ = this.adhocService.getTask(event.taskId).subscribe((res) => {
      if (res && res.success && res.model) {
        this.openDrawerFor(res.model, 'view');
      }
    });
  }

  onCopy(event: AdhocTaskHistoryEventModel): void {
    this.copySub$ = this.dialog
      .open(AdhocCopyModalComponent, dialogConfigHelper(this.overlay, {id: event.taskId, title: event.taskTitle}))
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.openDrawerFor(result, 'edit');
        }
      });
  }

  onDelete(event: AdhocTaskHistoryEventModel): void {
    this.deleteSub$ = this.dialog
      .open(AdhocDeleteModalComponent, dialogConfigHelper(this.overlay, {id: event.taskId, title: event.taskTitle}))
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.updateTable();
        }
      });
  }

  onArchive(event: AdhocTaskHistoryEventModel): void {
    this.archiveSub$ = this.adhocService.archiveTask(event.taskId).subscribe((res) => {
      if (res && res.success) {
        this.updateTable();
      }
    });
  }
}
