import { Injectable } from '@angular/core';
import {Observable, zip} from 'rxjs';
import {
  OperationDataResult,
  Paged,
  PaginationModel,
  SortModel,
} from 'src/app/common/models';
import { updateTableSort } from 'src/app/common/helpers';
import { getOffset } from 'src/app/common/helpers/pagination.helper';
import { map } from 'rxjs/operators';
import {PropertiesRequestModel, PropertyModel} from '../../../models';
import { BackendConfigurationPnPropertiesService } from '../../../services';
import {Store} from '@ngrx/store';
import {
  selectPropertiesFilters, selectPropertiesPagination
} from '../../../state/properties/properties.selector';

@Injectable({ providedIn: 'root' })
export class PropertiesStateService {
  private selectPropertiesFilters$ = this.store.select(selectPropertiesFilters);
  private selectPropertiesPagination$ = this.store.select(selectPropertiesPagination);
  constructor(
    private store: Store,
    private service: BackendConfigurationPnPropertiesService,
  ) {}

  // getOffset(): Observable<number> {
  //   return this.query.selectOffset$;
  // }

  // getPageSize(): Observable<number> {
  //   return this.query.selectPageSize$;
  // }
  //
  // getActiveSort(): Observable<string> {
  //   return this.query.selectActiveSort$;
  // }
  //
  // getActiveSortDirection(): Observable<'asc' | 'desc'> {
  //   return this.query.selectActiveSortDirection$;
  // }
  //
  // getNameFilter(): Observable<string> {
  //   return this.query.selectNameFilter$;
  // }

  getAllProperties(): Observable<OperationDataResult<Paged<PropertyModel>>> {
    let propertiesRequestModel = new PropertiesRequestModel();
    zip(this.selectPropertiesFilters$, this.selectPropertiesPagination$).subscribe(
        ([filters, pagination]) => {
            propertiesRequestModel = {
            ...propertiesRequestModel,
            ...filters,
            ...pagination,
            };
        }
        ).unsubscribe();
    return this.service.getAllProperties(propertiesRequestModel).pipe(
        map((response) => {
            if (response && response.success && response.model) {
                this.store.dispatch({
                    type: '[Properties] Update Properties Total Properties', payload: {
                    pagination: {
                      total: response.model.total,
                    },
                    totalProperties: response.model.total,
                  }
                });
            }
            return response;
        })
    );
    // return this.service
    //   .getAllProperties({
    //     ...this.query.pageSetting.pagination,
    //     ...this.query.pageSetting.filters,
    //     pageIndex: 0,
    //   })
    //   .pipe(
    //     map((response) => {
    //       if (response && response.success && response.model) {
    //         this.store.update(() => ({
    //           totalProperties: response.model.total,
    //         }));
    //       }
    //       return response;
    //     })
    //   );
  }

  updateNameFilter(nameFilter: string) {
    // this.store.update((state) => ({
    //   filters: {
    //     ...state.filters,
    //     nameFilter: nameFilter,
    //   },
    //   pagination: {
    //     ...state.pagination,
    //     offset: 0,
    //   },
    // }));
  }

  updatePageSize(pageSize: number) {
    // this.store.update((state) => ({
    //   pagination: {
    //     ...state.pagination,
    //     pageSize: pageSize,
    //   },
    // }));
    // this.checkOffset();
  }

  changePage(offset: number) {
    // this.store.update((state) => ({
    //   pagination: {
    //     ...state.pagination,
    //     offset: offset,
    //   },
    // }));
  }

  onDelete() {
    // this.store.update((state) => ({
    //   totalProperties: state.totalProperties - 1,
    // }));
    // this.checkOffset();
  }

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
  //
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

  // getPagination(): Observable<PaginationModel> {
  //   return this.query.selectPagination$;
  // }
}
