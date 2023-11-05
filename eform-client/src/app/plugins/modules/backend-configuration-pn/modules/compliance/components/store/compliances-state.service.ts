import { Injectable } from '@angular/core';
// import { CompliancesStore, CompliancesQuery } from './';
import { Observable } from 'rxjs';
import {
  OperationDataResult,
  Paged,
  PaginationModel,
  SortModel,
} from 'src/app/common/models';
import { updateTableSort } from 'src/app/common/helpers';
import { getOffset } from 'src/app/common/helpers/pagination.helper';
import { map } from 'rxjs/operators';
import { ComplianceModel } from '../../../../models';
import { BackendConfigurationPnCompliancesService } from '../../../../services';

@Injectable({ providedIn: 'root' })
export class CompliancesStateService {
  constructor(
    // private store: CompliancesStore,
    private service: BackendConfigurationPnCompliancesService,
    // private query: CompliancesQuery
  ) {}
  //
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

  getAllCompliances(propertyId: number, thirtyDays: boolean = false):
    Observable<OperationDataResult<Paged<ComplianceModel>>> {
    return this.service
      .getAllCompliances({
        // ...this.query.pageSetting.pagination,
        // ...this.query.pageSetting.filters,
        pageIndex: 0,
        sort: 'Id',
        isSortDsc: false,
        offset: 0,
        pageSize: 100000,
        propertyId: propertyId,
        days: thirtyDays ? 30 : 0,
      })
      .pipe(
        map((response) => {
          // if (response && response.success && response.model) {
          //   this.store.update(() => ({
          //     total: response.model.total,
          //   }));
          // }
          return response;
        })
      );
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
  //
  // getPagination(): Observable<PaginationModel> {
  //   return this.query.selectPagination$;
  // }
}
