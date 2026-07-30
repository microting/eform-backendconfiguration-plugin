import {Injectable} from '@angular/core';
import {Store} from '@ngrx/store';
import {Observable, of} from 'rxjs';
import {map, switchMap, tap} from 'rxjs/operators';
import {updateTableSort} from 'src/app/common/helpers';
import {CommonPaginationState, OperationDataResult, PaginationModel} from 'src/app/common/models';
import {BackendConfigurationPnAdhocService} from '../../../../services';
import {
  AdhocAreaModel,
  AdhocPropertyModel,
  AdhocTagModel,
  AdhocTaskFiltersModel,
  AdhocTaskIndexResultModel,
  AdhocTaskStatusFilter,
  AdhocWorkerModel,
} from '../../../../models';
import {
  AdhocFiltrationModel,
  adhocUpdateFilters,
  adhocUpdateHiddenColumns,
  adhocUpdatePagination,
  selectAdhocFilters,
  selectAdhocHiddenColumns,
  selectAdhocPagination,
} from '../../../../state';

const STATUS_TO_WIRE: Record<AdhocFiltrationModel['status'], AdhocTaskStatusFilter> = {
  open: AdhocTaskStatusFilter.Open,
  completed: AdhocTaskStatusFilter.Completed,
  archived: AdhocTaskStatusFilter.Archived,
};

/**
 * State facade for the "Adhoc overblik" dashboard (M5/F5) - per
 * `TaskManagementStateService`/`PlanningsStateService`: subscribes to the
 * `adhocState` slice's selectors, composes the wire-level
 * `AdhocTaskFiltersModel` from the current UI filters + pagination, and
 * exposes the sort/page/filter/column mutators the table + filters + drawer
 * components call.
 *
 * Also owns the reference-data caches (M5/F5 deviation from the plan's F1
 * projection: `AdhocTaskModel` carries ids only - no
 * propertyName/areaName/createdByName/tag or worker names) - the table and
 * drawer resolve display names from `properties`/`tags`
 * (loaded once, global) and `getAreasForProperty`/`getWorkersForProperty`
 * (loaded lazily per property id, cached for the container's lifetime -
 * cleared via `resetReferenceData()` when the container initializes so a
 * fresh dashboard visit never serves stale data from an earlier session).
 */
@Injectable({providedIn: 'root'})
export class AdhocStateService {
  private selectAdhocFilters$ = this.store.select(selectAdhocFilters);
  private selectAdhocPagination$ = this.store.select(selectAdhocPagination);
  private selectAdhocHiddenColumns$ = this.store.select(selectAdhocHiddenColumns);

  currentFilters: AdhocFiltrationModel;
  currentPagination: CommonPaginationState;
  currentHiddenColumns: string[] = [];

  properties: AdhocPropertyModel[] = [];
  tags: AdhocTagModel[] = [];
  private areasByProperty = new Map<number, AdhocAreaModel[]>();
  private workersByProperty = new Map<number, AdhocWorkerModel[]>();

  constructor(
    private store: Store,
    private service: BackendConfigurationPnAdhocService,
  ) {
    this.selectAdhocFilters$.subscribe((x) => (this.currentFilters = x));
    this.selectAdhocPagination$.subscribe((x) => (this.currentPagination = x));
    this.selectAdhocHiddenColumns$.subscribe((x) => (this.currentHiddenColumns = x));
  }

  // -----------------------------------------------------------------
  // Reference data (properties/tags global, areas/workers per property)
  // -----------------------------------------------------------------

  resetReferenceData(): void {
    this.properties = [];
    this.tags = [];
    this.areasByProperty.clear();
    this.workersByProperty.clear();
  }

  loadProperties(): Observable<AdhocPropertyModel[]> {
    return this.service.getProperties().pipe(
      map((res) => (res && res.success && res.model) || []),
      tap((model) => (this.properties = model))
    );
  }

  loadTags(): Observable<AdhocTagModel[]> {
    return this.service.getTags().pipe(
      map((res) => (res && res.success && res.model) || []),
      tap((model) => (this.tags = model))
    );
  }

  getAreasForProperty(propertyId: number): Observable<AdhocAreaModel[]> {
    const cached = this.areasByProperty.get(propertyId);
    if (cached) {
      return of(cached);
    }
    return this.service.getAreas(propertyId).pipe(
      map((res) => (res && res.success && res.model) || []),
      tap((model) => this.areasByProperty.set(propertyId, model))
    );
  }

  getWorkersForProperty(propertyId: number): Observable<AdhocWorkerModel[]> {
    const cached = this.workersByProperty.get(propertyId);
    if (cached) {
      return of(cached);
    }
    return this.service.getWorkers(propertyId).pipe(
      map((res) => (res && res.success && res.model) || []),
      tap((model) => this.workersByProperty.set(propertyId, model))
    );
  }

  /** Synchronous read of whatever `getAreasForProperty` has already cached (empty until loaded). */
  getCachedAreas(propertyId: number | null | undefined): AdhocAreaModel[] {
    if (propertyId == null) {
      return [];
    }
    return this.areasByProperty.get(propertyId) ?? [];
  }

  /** Synchronous read of whatever `getWorkersForProperty` has already cached (empty until loaded). */
  getCachedWorkers(propertyId: number | null | undefined): AdhocWorkerModel[] {
    if (propertyId == null) {
      return [];
    }
    return this.workersByProperty.get(propertyId) ?? [];
  }

  // -----------------------------------------------------------------
  // Area mutations (area-management spec 2026-07-30). Each one ACTIVELY
  // refreshes the property's cache entry: adhoc-filters and the task
  // drawer hold one-shot-subscribed copies, so invalidate-only would
  // leave stale dropdowns (spec, Angular design).
  // -----------------------------------------------------------------

  /**
   * Only overwrites the cache when the create actually succeeded - unlike
   * `refreshAreas`, this maps a raw create response, so a `success: false`
   * reply must not poison the cache with `[]` (mirrors renameArea/deleteArea,
   * which never write a failure result into the cache either).
   */
  createAreas(propertyId: number, names: string[]): Observable<AdhocAreaModel[]> {
    return this.service.createAreas(propertyId, names).pipe(
      map((res) => (res && res.success && res.model) || null),
      tap((areas) => {
        if (areas) {
          this.areasByProperty.set(propertyId, areas);
        }
      }),
      map((areas) => areas || [])
    );
  }

  renameArea(propertyId: number, areaId: number, name: string): Observable<boolean> {
    return this.service.renameArea(areaId, name).pipe(
      map((res) => !!(res && res.success)),
      switchMap((success) => this.refreshAreas(propertyId).pipe(map(() => success)))
    );
  }

  deleteArea(propertyId: number, areaId: number): Observable<boolean> {
    return this.service.deleteArea(areaId).pipe(
      map((res) => !!(res && res.success)),
      switchMap((success) => this.refreshAreas(propertyId).pipe(map(() => success)))
    );
  }

  /** Unconditional re-fetch that overwrites the cache entry (unlike `getAreasForProperty`, which returns the cache when present). */
  private refreshAreas(propertyId: number): Observable<AdhocAreaModel[]> {
    return this.service.getAreas(propertyId).pipe(
      map((res) => (res && res.success && res.model) || []),
      tap((areas) => this.areasByProperty.set(propertyId, areas))
    );
  }

  // -----------------------------------------------------------------
  // Tasks
  // -----------------------------------------------------------------

  getTasks(): Observable<OperationDataResult<AdhocTaskIndexResultModel>> {
    return this.service.getTasks(this.buildFiltersModel()).pipe(
      tap((res) => {
        // Propagate the fetched total into the pagination slice - the
        // container binds `[pagination]` (including `total`) straight into
        // eform-pagination, so without this dispatch `[length]` stays 0 and
        // pages past 1 are unreachable (mirrors Historik's local `total`).
        if (res && res.success && res.model) {
          this.store.dispatch(
            adhocUpdatePagination({
              ...this.currentPagination,
              total: res.model.total,
            })
          );
        }
      })
    );
  }

  private buildFiltersModel(): AdhocTaskFiltersModel {
    const f = this.currentFilters;
    const p = this.currentPagination;
    return {
      propertyId: f.propertyId,
      areaId: f.areaId,
      status: STATUS_TO_WIRE[f.status],
      tagIds: [...f.tagIds],
      tagsMatchAll: f.tagLogic === 'and',
      searchText: f.search || null,
      pageNumber: p.pageIndex + 1,
      pageSize: p.pageSize,
      sortColumn: p.sort,
      sortAscending: !p.isSortDsc,
    };
  }

  onSortTable(sort: string): void {
    const localPageSettings = updateTableSort(sort, this.currentPagination.sort, this.currentPagination.isSortDsc);
    this.store.dispatch(
      adhocUpdatePagination({
        ...this.currentPagination,
        ...localPageSettings,
        pageIndex: 0,
        offset: 0,
      })
    );
  }

  changePage(pagination: PaginationModel): void {
    const pageIndex = pagination.pageSize > 0 ? Math.floor(pagination.offset / pagination.pageSize) : 0;
    this.store.dispatch(
      adhocUpdatePagination({
        ...this.currentPagination,
        ...pagination,
        pageIndex,
      })
    );
  }

  updateFilters(partial: Partial<AdhocFiltrationModel>): void {
    const merged: AdhocFiltrationModel = {...this.currentFilters, ...partial};
    if (JSON.stringify(merged) === JSON.stringify(this.currentFilters)) {
      return;
    }
    this.store.dispatch(adhocUpdateFilters(merged));
    // A filter change can shrink `total` out from under the current page -
    // always snap back to page 1 (mirrors the plannings/task-management
    // facades' own re-fetch-from-page-1-on-filter-change behavior).
    this.store.dispatch(
      adhocUpdatePagination({
        ...this.currentPagination,
        pageIndex: 0,
        offset: 0,
      })
    );
  }

  updateHiddenColumns(columns: string[]): void {
    this.store.dispatch(adhocUpdateHiddenColumns([...columns]));
  }
}
