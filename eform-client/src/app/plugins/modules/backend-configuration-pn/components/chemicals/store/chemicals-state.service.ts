import { Injectable } from '@angular/core';
import { ChemicalsStore, ChemicalsQuery } from './index';
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
import { arrayToggle } from '@datorama/akita';
import {
  BackendConfigurationPnChemicalsService
} from 'src/app/plugins/modules/backend-configuration-pn/services/backend-configuration-pn-chemicals.service';
import {ChemicalModel} from 'src/app/plugins/modules/backend-configuration-pn/models/chemicals';

@Injectable({ providedIn: 'root' })
export class ChemicalsStateService {
  constructor(
    private store: ChemicalsStore,
    private service: BackendConfigurationPnChemicalsService,
    private query: ChemicalsQuery
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

  getDescriptionFilter(): Observable<string> {
    return this.query.selectDescriptionFilter$;
  }

  getTagIds(): Observable<number[]> {
    return this.query.selectTagIds$;
  }

  getDeviceUserIds(): Observable<number[]> {
    return this.query.selectDeviceUsers$;
  }

  getAllChemicals(): Observable<OperationDataResult<Paged<ChemicalModel>>> {
    return this.service
      .getAllChemicals({
        ...this.query.pageSetting.pagination,
        ...this.query.pageSetting.filters,
        pageIndex: 0,
      })
      .pipe(
        map((response) => {
          if (response && response.success && response.model) {
            this.store.update(() => ({
              totalChemicals: response.model.total,
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

  addOrRemoveTagIds(id: number) {
    this.store.update((state) => ({
      filters: {
        ...state.filters,
        tagIds: arrayToggle(state.filters.tagIds, id),
      },
    }));
  }

  addOrRemoveDeviceUserIds(id: number) {
    this.store.update((state) => ({
      filters: {
        ...state.filters,
        deviceUserIds: arrayToggle(state.filters.deviceUserIds, id),
      },
    }));
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
      totalChemicals: state.totalChemicals - 1,
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
      this.query.pageSetting.totalChemicals
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
