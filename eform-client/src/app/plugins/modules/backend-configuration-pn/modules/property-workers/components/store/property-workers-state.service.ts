import { Injectable } from '@angular/core';
import { PropertyWorkersStore, PropertyWorkersQuery } from './';
import { Observable } from 'rxjs';
import { OperationDataResult, SiteDto, SortModel } from 'src/app/common/models';
import { updateTableSort, getOffset } from 'src/app/common/helpers';
import { map } from 'rxjs/operators';
import { BackendConfigurationPnPropertiesService } from '../../../../services';
import { DeviceUserService } from 'src/app/common/services';
import {DeviceUserModel} from 'src/app/plugins/modules/backend-configuration-pn/models/device-users';

@Injectable({ providedIn: 'root' })
export class PropertyWorkersStateService {
  constructor(
    private store: PropertyWorkersStore,
    private service: BackendConfigurationPnPropertiesService,
    private query: PropertyWorkersQuery,
    // private deviceUserService: DeviceUserService
  ) {}

  // getPageSize(): Observable<number> {
  //   return this.query.selectPageSize$;
  // }

  getSort(): Observable<SortModel> {
    return this.query.selectSort$;
  }

  getNameFilter(): Observable<string> {
    return this.query.selectNameFilter$;
  }

  getDeviceUsersFiltered(): Observable<OperationDataResult<DeviceUserModel[]>> {
    return this.service
      .getDeviceUsersFiltered({
        ...this.query.pageSetting.filters,
        ...this.query.pageSetting.pagination,
      })
      .pipe(
        map((response) => {
          return response;
        })
      );
  }

  updateNameFilter(nameFilter: string) {
    this.store.update((state) => ({
      filters: {
        ...state.filters,
        nameFilter: nameFilter,
      },
      pagination: {
        ...state.pagination,
        offset: 0,
      },
    }));
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
    this.store.update((state) => ({
      totalProperties: state.totalProperties - 1,
    }));
    this.checkOffset();
  }

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

  checkOffset() {
    const newOffset = getOffset(
      this.query.pageSetting.pagination.pageSize,
      this.query.pageSetting.pagination.offset,
      this.query.pageSetting.totalProperties
    );
    if (newOffset !== this.query.pageSetting.pagination.offset) {
      this.store.update((state) => ({
        pagination: {
          ...state.pagination,
          offset: newOffset,
        },
      }));
    }
  }

  // getPagination(): Observable<PaginationModel> {
  //   return this.query.selectPagination$;
  // }
}
