import {Injectable} from '@angular/core';
import {Observable, tap} from 'rxjs';
import {
  CommonPaginationState,
  OperationDataResult,
  Paged,
} from 'src/app/common/models';
import {updateTableSort} from 'src/app/common/helpers';
import {FilesModel,} from '../../../models';
import {BackendConfigurationPnFilesService,} from '../../../services';
import {Store} from '@ngrx/store';
import {
  FilesFiltrationModel,
  selectFilesFilters,
  selectFilesPagination,
  filesUpdatePaginationTotalItemsCount,
  filesUpdatePagination
} from '../../../state';

@Injectable({providedIn: 'root'})
export class FilesStateService {
  private selectFilesFilters$ = this.store.select(selectFilesFilters);
  private selectFilesPagination$ = this.store.select(selectFilesPagination);
  currentPagination: CommonPaginationState;
  currentFilters: FilesFiltrationModel;

  constructor(
    private store: Store,
    private service: BackendConfigurationPnFilesService,
  ) {
    this.selectFilesPagination$.subscribe(x => this.currentPagination = x);
    this.selectFilesFilters$.subscribe(x => this.currentFilters = x);
  }

  getFiles(): Observable<OperationDataResult<Paged<FilesModel>>> {
    return this.service.getAllFiles({
      propertyIds: this.currentFilters.propertyIds,
      nameFilter: this.currentFilters.nameFilter,
      tagIds: this.currentFilters.tagIds,
      dateFrom: this.currentFilters.dateRange.dateFrom,
      dateTo: this.currentFilters.dateRange.dateTo,
      sort: this.currentPagination.sort,
      isSortDsc: this.currentPagination.isSortDsc,
    }).pipe(
      tap((data) => {
        if (data && data.success) {
          this.store.dispatch(filesUpdatePaginationTotalItemsCount(data.model.total || 0));
        }
      })
    );
  }

  onSortTable(sort: string) {
    const localPageSettings = updateTableSort(
      sort,
      this.currentPagination.sort,
      this.currentPagination.isSortDsc
    );
    this.store.dispatch(filesUpdatePagination({...this.currentPagination, ...localPageSettings}));
  }
}
