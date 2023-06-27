import { Injectable } from '@angular/core';
import {FilesFiltrationModel, FilesQuery, FilesStore} from './';
import { Observable } from 'rxjs';
import {
  OperationDataResult,
  Paged,
} from 'src/app/common/models';
import { updateTableSort } from 'src/app/common/helpers';
import {
  FilesModel,
  FilesRequestModel,
} from '../../../models';
import {
  BackendConfigurationPnFilesService,
} from '../../../services';
import {tap} from 'rxjs/operators';

@Injectable({ providedIn: 'root' })
export class FilesStateService {
  constructor(
    public store: FilesStore,
    private service: BackendConfigurationPnFilesService,
    private query: FilesQuery,
  ) {}

  getFiles() : Observable<OperationDataResult<Paged<FilesModel>>> {
    const filters: FilesRequestModel = {
      sort: this.query.pageSetting.pagination.sort,
      isSortDsc: this.query.pageSetting.pagination.isSortDsc,
      nameFilter: this.query.pageSetting.filters.nameFilter ?? '',
      propertyIds: this.query.pageSetting.filters.propertyIds ?? null,
      tagIds: this.query.pageSetting.filters.tagIds ?? null,
      dateFrom: this.query.pageSetting.filters.dateRange.dateFrom,
      dateTo: this.query.pageSetting.filters.dateRange.dateTo,
    };

    return this.service.getAllFiles(filters)
      .pipe(tap(model => {
        if(model.success && model.model) {
          this.store.update(() => ({
            total: model.model.total,
          }));
        }
      }));
  }

  getFiltersAsync(): Observable<FilesFiltrationModel> {
    return this.query.selectFilters$;
  }

  getActiveSort(): Observable<string> {
    return this.query.selectActiveSort$;
  }

  getActiveSortDirection(): Observable<'asc' | 'desc'> {
    return this.query.selectActiveSortDirection$;
  }

  updateFilters(filters: FilesFiltrationModel) {
    this.store.update((state) => ({
      filters: {
        ...state.filters,
        ...filters,
      },
      pagination: {
        ...state.pagination,
        offset: 0,
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
}
