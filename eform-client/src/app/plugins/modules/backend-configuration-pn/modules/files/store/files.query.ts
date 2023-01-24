import {Injectable} from '@angular/core';
import {Query} from '@datorama/akita';
import {
  FilesState,
  FilesStore
} from './';
import {PaginationModel} from 'src/app/common/models';

@Injectable({providedIn: 'root'})
export class FilesQuery extends Query<FilesState> {
  constructor(protected store: FilesStore) {
    super(store);
  }

  get pageSetting() {
    return this.getValue();
  }

  selectNameFilter$ = this.select((state) => state.filters.nameFilter);
  selectPagination$ = this.select(
    (state) =>
      new PaginationModel(
        state.total,
        state.pagination.pageSize,
        state.pagination.offset
      )
  );
  selectActiveSort$ = this.select((state) => state.pagination.sort);
  selectActiveSortDirection$ = this.select((state) => state.pagination.isSortDsc ? 'desc' : 'asc');
  selectFilters$ = this.select((state) => state.filters);
}
