import { Injectable } from '@angular/core';
import {Observable, zip} from 'rxjs';
import {DeviceUserRequestModel, OperationDataResult} from 'src/app/common/models';
import { updateTableSort, getOffset } from 'src/app/common/helpers';
import { map } from 'rxjs/operators';
import { BackendConfigurationPnPropertiesService } from '../../../../services';
import {DeviceUserModel} from 'src/app/plugins/modules/backend-configuration-pn/models/device-users';
import * as R from 'ramda';
import {Store} from '@ngrx/store';
import {
  selectPropertyWorkersFilters, selectPropertyWorkersPagination
} from '../../../../state/property-workers/property-workers.selector';

@Injectable({ providedIn: 'root' })
export class PropertyWorkersStateService {
  private selectPropertyWorkersFilters$ = this.store.select(selectPropertyWorkersFilters);
  private selectPropertyWorkersPagination$ = this.store.select(selectPropertyWorkersPagination);
  constructor(
      private store: Store,
    private service: BackendConfigurationPnPropertiesService,
    // private deviceUserService: DeviceUserService
  ) {}

  // getPageSize(): Observable<number> {
  //   return this.query.selectPageSize$;
  // }

  // getSort(): Observable<SortModel> {
  //   return this.query.selectSort$;
  // }

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

  getDeviceUsersFiltered(): Observable<OperationDataResult<DeviceUserModel[]>> {
    let deviceUserRequestModel = new DeviceUserRequestModel();
    zip(this.selectPropertyWorkersFilters$, this.selectPropertyWorkersPagination$).subscribe(
        ([filters, pagination]) => {
            deviceUserRequestModel = {
            ...deviceUserRequestModel,
            ...filters,
            ...pagination,
            };
        }
        ).unsubscribe();
    return this.service.getDeviceUsersFiltered(deviceUserRequestModel).pipe(
        map((response) => {
            if (response && response.success && response.model) {
                this.store.dispatch({
                    type: '[PropertyWorkers] Update PropertyWorkers Total PropertyWorkers', payload: {
                    pagination: {
                      total: response.model.length,
                    },
                    totalPropertyWorkers: response.model.length,
                  }
                });
            }
            return response;
        })
    );
    // return zip(this.selectPropertyWorkersFilters$, this.selectPropertyWorkersPagination$).pipe(
    //     map(([filters, pagination]) => {
    //       return this.service.getDeviceUsersFiltered({
    //         ...filters,
    //         ...pagination,
    //       });
    //       }
    //     }
    // return this.service
    //   .getDeviceUsersFiltered({
    //     ...this.query.pageSetting.filters,
    //     ...this.query.pageSetting.pagination,
    //   })
    //   .pipe(
    //     map((response) => {
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

  // getFiltersAsync(): Observable<PropertyWorkersFiltrationModel> {
  //   return this.query.selectFilters$;
  // }

  updatePropertyIds(propertyIds: number[]) {
    // if(!R.equals(this.store.getValue().filters.propertyIds, propertyIds)) {
    //   this.store.update((state) => ({
    //     filters: {
    //       ...state.filters,
    //       propertyIds: propertyIds,
    //     },
    //   }));
    // }
  }

  // updatePageSize(pageSize: number) {
  //   this.store.update((state) => ({
  //     pagination: {
  //       ...state.pagination,
  //       pageSize: pageSize,
  //     },
  //   }));
  //   this.checkOffset();
  // }
  //
  // changePage(offset: number) {
  //   this.store.update((state) => ({
  //     pagination: {
  //       ...state.pagination,
  //       offset: offset,
  //     },
  //   }));
  // }

  onDelete() {
    // this.store.update((state) => ({
    // }));
    // //this.checkOffset();
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

  // checkOffset() {
  //   const newOffset = getOffset(
  //     this.query.pageSetting.pagination.pageSize,
  //     this.query.pageSetting.pagination.offset,
  //     this.query.pageSetting.pagination.totalItems
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
