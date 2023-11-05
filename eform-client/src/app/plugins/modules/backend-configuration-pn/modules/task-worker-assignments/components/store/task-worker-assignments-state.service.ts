import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {CommonPaginationState, OperationDataResult, Paged, SortModel} from 'src/app/common/models';
import { updateTableSort, getOffset } from 'src/app/common/helpers';
import { map } from 'rxjs/operators';
import {BackendConfigurationPnAreasService, } from '../../../../services';
import {TaskWorkerModel} from '../../../../models';
import {Store} from '@ngrx/store';
import {
  selectTaskWorkerAssignmentPagination
} from '../../../../state/task-worker-assignment/task-worker-assignment.selector';

@Injectable({ providedIn: 'root' })
export class TaskWorkerAssignmentsStateService {
  private selectTaskWorkerAssignmentPagination$ = this.store.select(selectTaskWorkerAssignmentPagination);
  constructor(
      private store: Store,
    private service: BackendConfigurationPnAreasService,
  ) {}

  private _siteId: number;

  public set siteId (value:number) {
    this._siteId = value;
  }

  public get siteId () {
    return this._siteId;
  }

  getTaskWorkerAssignments(): Observable<OperationDataResult<Paged<TaskWorkerModel>>> {
    let _pagination: CommonPaginationState;
    this.selectTaskWorkerAssignmentPagination$.subscribe((pagination) => {
      _pagination = pagination;
    }).unsubscribe();
    return this.service.getTaskWorkerAssignments(this._siteId, _pagination).pipe(
        map((response) => {
          return response;
        })
    );
    // return this.service.getTaskWorkerAssignments(this._siteId, this.query.pageSetting.pagination)
    //   .pipe(
    //     map((response) => {
    //       return response;
    //     })
    //   );
  }

  // getPageSize(): Observable<number> {
  //   return this.query.selectPageSize$;
  // }

  // getSort(): Observable<SortModel> {
  //   return this.query.selectSort$;
  // }

  // getNameFilter(): Observable<string> {
  //   return this.query.selectNameFilter$;
  // }

  // getDeviceUsersFiltered(): Observable<OperationDataResult<SiteDto[]>> {
  //   return this.deviceUserService
  //     .getDeviceUsersFiltered({
  //       ...this.query.pageSetting.filters,
  //       ...this.query.pageSetting.pagination,
  //     })
  //     .pipe(
  //       map((response) => {
  //         return response;
  //       })
  //     );
  // }

  // updateNameFilter(nameFilter: string) {
  //   this.store.update((state) => ({
  //     filters: {
  //       ...state.filters,
  //       nameFilter: nameFilter,
  //     },
  //     pagination: {
  //       ...state.pagination,
  //       offset: 0,
  //     },
  //   }));
  // }

  // updatePageSize(pageSize: number) {
  //   this.store.update((state) => ({
  //     pagination: {
  //       ...state.pagination,
  //       pageSize: pageSize,
  //     },
  //   }));
  //   this.checkOffset();
  // }

  // changePage(offset: number) {
  //   this.store.update((state) => ({
  //     pagination: {
  //       ...state.pagination,
  //       offset: offset,
  //     },
  //   }));
  // }

  // onDelete() {
  //   this.store.update((state) => ({
  //     totalProperties: state.totalProperties - 1,
  //   }));
  //   this.checkOffset();
  // }

  onSortTable(sort: string) {
    // const localPageSettings = updateTableSort(
    //   sort,
    //   this.query.pageSetting.pagination.sort,
    //   this.query.pageSetting.pagination.isSortDsc
    // );
    // this.store.update((state) => ({
    //   pagination: {
    //     ...state.pagination,
    //     isSortDsc: localPageSettings.isSortDsc,
    //     sort: localPageSettings.sort,
    //   },
    // }));
  }

  // checkOffset() {
  //   const newOffset = getOffset(
  //     this.query.pageSetting.pagination.pageSize,
  //     this.query.pageSetting.pagination.offset,
  //     this.query.pageSetting.totalProperties
  //   );
  //   if (newOffset !== this.query.pageSetting.pagination.offset) {
  //     this.store.update((state) => ({
  //       pagination: {
  //         ...state.pagination,
  //         offset: newOffset,
  //       },
  //     }));
  //   }
  // }

  // getActiveSort(): Observable<string> {
  //   return this.query.selectActiveSort$;
  // }
  //
  // getActiveSortDirection(): Observable<'asc' | 'desc'> {
  //   return this.query.selectActiveSortDirection$;
  // }

  // getPagination(): Observable<PaginationModel> {
  //   return this.query.selectPagination$;
  // }
}
