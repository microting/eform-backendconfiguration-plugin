import { Injectable } from '@angular/core';
import {Observable, zip} from 'rxjs';
import {
  OperationDataResult,
  SortModel,
} from 'src/app/common/models';
import { updateTableSort } from 'src/app/common/helpers';
import { getOffset } from 'src/app/common/helpers/pagination.helper';
import { BackendConfigurationPnTaskManagementService} from '../../../../services';
import { WorkOrderCaseModel } from '../../../../models';
import {Store} from '@ngrx/store';
import {
  selectTaskManagementFilters,
  selectTaskManagementPagination
} from '../../../../state/task-management/task-management.selector';

@Injectable({ providedIn: 'root' })
export class TaskManagementStateService {
  private selectTaskManagementPagination$ = this.store.select(selectTaskManagementPagination);
  private selectTaskManagementFilters$ = this.store.select(selectTaskManagementFilters);
  constructor(
      private store: Store,
    private service: BackendConfigurationPnTaskManagementService,
  ) {}

  // getPageSize(): Observable<number> {
  //   return this.query.selectPageSize$;
  // }

  // getSort(): Observable<SortModel> {
  //   return this.query.selectSort$;
  // }

  // getNameFilter(): Observable<string> {
  //   return this.query.selectNameFilter$;
  // }

  // getActiveSort(): Observable<string> {
  //   return this.query.selectActiveSort$;
  // }
  //
  // getActiveSortDirection(): Observable<'asc' | 'desc'> {
  //   return this.query.selectActiveSortDirection$;
  // }

  getAllWorkOrderCases(delayed: boolean):
    Observable<OperationDataResult<WorkOrderCaseModel[]>> {
    let _filters:any;
    zip(this.selectTaskManagementFilters$, this.selectTaskManagementPagination$).subscribe(
        ([filters, pagination]) => {
          _filters = {
            ...filters,
            ...pagination,
          };
        }
    ).unsubscribe();
    return this.service.getWorkOrderCases({
      ..._filters,
      delayed: delayed,
    });
    // return this.service
    //   .getWorkOrderCases({
    //     ...this.query.pageSetting.pagination,
    //     ...this.query.pageSetting.filters,
    //     delayed: delayed,
    //   });
  }

  downloadWordReport() {
    // return this.service
    //   .downloadWordReport({
    //     ...this.query.pageSetting.pagination,
    //     ...this.query.pageSetting.filters,
    //   });
  }

  downloadExcelReport() {
    // return this.service
    //   .downloadExcelReport({
    //     ...this.query.pageSetting.pagination,
    //     ...this.query.pageSetting.filters,
    //   });
  }

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
  // this.checkOffset();
  // }

  changePage(offset: number) {
    // this.store.update((state) => ({
    //   pagination: {
    //     ...state.pagination,
    //     offset: offset,
    //   },
    // }));
  }

  // onDelete() {
  //   this.store.update((state) => ({
  //     total: state.total - 1,
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
  //     this.query.pageSetting.total
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

  // getFiltersAsync(): Observable<TaskManagementFiltrationModel> {
  //   return this.query.selectFilters$;
  // }

  /*updateFilters(taskManagementFiltrationModel :TaskManagementFiltrationModel){
    this.store.update((state) => ({
      filters: {
        ...state.filters,
        propertyId: taskManagementFiltrationModel.propertyId,
        areaId: taskManagementFiltrationModel.areaId,
        date: taskManagementFiltrationModel.date,
        status: taskManagementFiltrationModel.status,
        createdBy: taskManagementFiltrationModel.createdBy,
        lastAssignedTo: taskManagementFiltrationModel.lastAssignedTo,
      },
    }));
  }*/

  // getDisabledButtonsAsync() {
  //   return this.query.selectDisableButtons$;
  // }

  // getDisabledButtons() {
  //   const storeValue = this.store.getValue()
  //   return !storeValue.filters.propertyId;
  // }

  // getPagination(): Observable<PaginationModel> {
  //   return this.query.selectPagination$;
  // }
}
