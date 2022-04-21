import { Injectable } from '@angular/core';
import {
  TaskManagementStore,
  TaskManagementQuery,
  TaskManagementFiltrationModel,
} from './';
import { Observable } from 'rxjs';
import {
  OperationDataResult,
  SortModel,
} from 'src/app/common/models';
import { updateTableSort } from 'src/app/common/helpers';
import { getOffset } from 'src/app/common/helpers/pagination.helper';
import { BackendConfigurationPnTaskManagementService} from '../../../../services';
import { WorkOrderCaseModel } from '../../../../models';

@Injectable({ providedIn: 'root' })
export class TaskManagementStateService {
  constructor(
    public store: TaskManagementStore,
    private service: BackendConfigurationPnTaskManagementService,
    private query: TaskManagementQuery
  ) {}

  // getPageSize(): Observable<number> {
  //   return this.query.selectPageSize$;
  // }

  getSort(): Observable<SortModel> {
    return this.query.selectSort$;
  }

  // getNameFilter(): Observable<string> {
  //   return this.query.selectNameFilter$;
  // }

  getAllWorkOrderCases():
    Observable<OperationDataResult<WorkOrderCaseModel[]>> {
    return this.service
      .getWorkOrderCases({
        ...this.query.pageSetting.pagination,
        ...this.query.pageSetting.filters,
      });
  }

  downloadWordReport() {
    return this.service
      .downloadWordReport({
        ...this.query.pageSetting.pagination,
        ...this.query.pageSetting.filters,
      });
  }

  downloadExcelReport() {
    return this.service
      .downloadExcelReport({
        ...this.query.pageSetting.pagination,
        ...this.query.pageSetting.filters,
      });
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
    this.store.update((state) => ({
      pagination: {
        ...state.pagination,
        offset: offset,
      },
    }));
  }

  // onDelete() {
  //   this.store.update((state) => ({
  //     total: state.total - 1,
  //   }));
  //   this.checkOffset();
  // }

  onSortTable(sort: string) {
    const localPageSettings = updateTableSort(
      sort,
      this.query.pageSetting.pagination.sort,
      this.query.pageSetting.pagination.isSortDsc
    );
    this.store.update((state) => ({
      pagination: {
        ...state.pagination,
        isSortDsc: localPageSettings.isSortDsc,
        sort: localPageSettings.sort,
      },
    }));
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

  getFiltersAsync(): Observable<TaskManagementFiltrationModel> {
    return this.query.selectFilters$;
  }

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

  getDisabledButtonsAsync() {
    return this.query.selectDisableButtons$;
  }

  getDisabledButtons() {
    const storeValue = this.store.getValue()
    return !storeValue.filters.propertyId || !storeValue.filters.areaName;
  }

  // getPagination(): Observable<PaginationModel> {
  //   return this.query.selectPagination$;
  // }
}
