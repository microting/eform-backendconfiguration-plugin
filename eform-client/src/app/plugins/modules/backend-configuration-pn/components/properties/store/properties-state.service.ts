import { Injectable } from '@angular/core';
import { PropertiesStore, PropertiesQuery } from './';
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
import { PropertyModel } from '../../../models';
import { BackendConfigurationPnPropertiesService } from '../../../services';
import { arrayToggle } from '@datorama/akita';

@Injectable({ providedIn: 'root' })
export class PropertiesStateService {
  constructor(
    private store: PropertiesStore,
    private service: BackendConfigurationPnPropertiesService,
    private query: PropertiesQuery
  ) {}

  // getOffset(): Observable<number> {
  //   return this.query.selectOffset$;
  // }

  getPageSize(): Observable<number> {
    return this.query.selectPageSize$;
  }

  getSort(): Observable<SortModel> {
    return this.query.selectSort$;
  }

  // getSort(): Observable<string> {
  //   return this.query.selectSort$;
  // }
  //
  // getIsSortDsc(): Observable<boolean> {
  //   return this.query.selectIsSortDsc$;
  // }

  getNameFilter(): Observable<string> {
    return this.query.selectNameFilter$;
  }

  getAllProperties(): Observable<OperationDataResult<Paged<PropertyModel>>> {
    return this.service
      .getAllProperties({
        ...this.query.pageSetting.pagination,
        ...this.query.pageSetting.filters,
        pageIndex: 0,
      })
      .pipe(
        map((response) => {
          if (response && response.success && response.model) {
            this.store.update(() => ({
              totalProperties: response.model.total,
            }));
          }
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

  updatePageSize(pageSize: number) {
    this.store.update((state) => ({
      pagination: {
        ...state.pagination,
        pageSize: pageSize,
      },
    }));
    this.checkOffset();
  }

  changePage(offset: number) {
    this.store.update((state) => ({
      pagination: {
        ...state.pagination,
        offset: offset,
      },
    }));
  }

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

  updateDescriptionFilter(newDescriptionFilter: string) {
    this.store.update((state) => ({
      filters: {
        ...state.filters,
        descriptionFilter: newDescriptionFilter,
      },
    }));
  }

  getPagination(): Observable<PaginationModel> {
    return this.query.selectPagination$;
  }
}
