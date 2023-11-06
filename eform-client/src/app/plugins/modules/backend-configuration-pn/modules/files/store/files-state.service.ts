import { Injectable } from '@angular/core';
import {Observable, zip} from 'rxjs';
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
import {Store} from '@ngrx/store';
import {FilesFiltrationModel} from '../../../state/files/files.reducer';
import {
  selectFilesFilters,
  selectFilesPagination
} from '../../../state/files/files.selector';

@Injectable({ providedIn: 'root' })
export class FilesStateService {
  private selectFilesFilters$ = this.store.select(selectFilesFilters);
  private selectFilesPagination$ = this.store.select(selectFilesPagination);
  constructor(
      private store: Store,
    private service: BackendConfigurationPnFilesService,
  ) {}

  getFiles() : Observable<OperationDataResult<Paged<FilesModel>>> {
    let _filters: FilesRequestModel;
    zip(this.selectFilesFilters$, this.selectFilesPagination$).subscribe(
        ([filters, pagination]) => {
          _filters = {
            ..._filters,
            ...filters,
            ...pagination,
          };
        }
    ).unsubscribe();
    return this.service.getAllFiles(_filters).pipe(
        tap((data) => {
          if (data && data.success) {
            this.store.dispatch(
                {type: '[Files] Update Pagination Total Items Count', total: data.model.total ?? ''}
            )
            // this.store.update((state) => ({
            //   total: data.model.total,
            // }));
          }
        })
    );
    // this.selectFilesFilters$.subscribe((filters) => {
    //   _filters = ;
    // }).unsubscribe();
    //  FilesRequestModel = {
    //   sort: this.query.pageSetting.pagination.sort,
    //   isSortDsc: this.query.pageSetting.pagination.isSortDsc,
    //   nameFilter: this.query.pageSetting.filters.nameFilter ?? '',
    //   propertyIds: this.query.pageSetting.filters.propertyIds ?? null,
    //   tagIds: this.query.pageSetting.filters.tagIds ?? null,
    //   dateFrom: this.query.pageSetting.filters.dateRange.dateFrom,
    //   dateTo: this.query.pageSetting.filters.dateRange.dateTo,
    // };
    //
    // return this.service.getAllFiles(filters)
    //   .pipe(tap(model => {
    //     if(model.success && model.model) {
    //       this.store.update(() => ({
    //         total: model.model.total,
    //       }));
    //     }
    //   }));
  }

  // getFiltersAsync(): Observable<FilesFiltrationModel> {
  //   return this.query.selectFilters$;
  // }
  //
  // getActiveSort(): Observable<string> {
  //   return this.query.selectActiveSort$;
  // }
  //
  // getActiveSortDirection(): Observable<'asc' | 'desc'> {
  //   return this.query.selectActiveSortDirection$;
  // }

  updateFilters(filters: FilesFiltrationModel) {
    // this.store.update((state) => ({
    //   filters: {
    //     ...state.filters,
    //     ...filters,
    //   },
    //   pagination: {
    //     ...state.pagination,
    //     offset: 0,
    //   },
    // }));
  }

  changePage(offset: number) {
    // this.store.update((state) => ({
    //   pagination: {
    //     ...state.pagination,
    //     offset: offset,
    //   },
    // }));
  }

  onSortTable(sort: string) {
    let currentPagination;
    this.selectFilesPagination$.subscribe((pagination) => {
      currentPagination = pagination;
    }).unsubscribe();
    const localPageSettings = updateTableSort(
        sort,
        currentPagination.sort,
        currentPagination.isSortDsc
    );
    this.store.dispatch(
        {type: '[Files] Update Pagination', payload: localPageSettings}
    )
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
}
