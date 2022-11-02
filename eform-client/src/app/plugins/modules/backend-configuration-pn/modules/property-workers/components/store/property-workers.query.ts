import { Injectable } from '@angular/core';
import { Query } from '@datorama/akita';
import { PropertyWorkersState, PropertyWorkersStore } from './';
import { PaginationModel } from 'src/app/common/models';

@Injectable({ providedIn: 'root' })
export class PropertyWorkersQuery extends Query<PropertyWorkersState> {
  constructor(protected store: PropertyWorkersStore) {
    super(store);
  }

  get pageSetting() {
    return this.getValue();
  }

  selectNameFilter$ = this.select((state) => state.filters.nameFilter);
  selectPageSize$ = this.select((state) => state.pagination.pageSize);
  selectPagination$ = this.select(
    (state) =>
      new PaginationModel(
        state.totalProperties,
        state.pagination.pageSize,
        state.pagination.offset
      )
  );
  // selectSort$ = this.select(
  //   (state) => new SortModel(state.pagination.sort, state.pagination.isSortDsc)
  // );

  selectActiveSort$ = this.select((state) => state.pagination.sort);
  selectActiveSortDirection$ = this.select((state) => state.pagination.isSortDsc ? 'desc' : 'asc');
}
