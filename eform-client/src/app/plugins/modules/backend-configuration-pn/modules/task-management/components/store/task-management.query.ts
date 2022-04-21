import { Injectable } from '@angular/core';
import { Query } from '@datorama/akita';
import { TaskManagementState, TaskManagementStore } from './';
import { SortModel } from 'src/app/common/models';

@Injectable({ providedIn: 'root' })
export class TaskManagementQuery extends Query<TaskManagementState> {
  constructor(protected store: TaskManagementStore) {
    super(store);
  }

  get pageSetting() {
    return this.getValue();
  }

  // selectPageSize$ = this.select((state) => state.pagination.pageSize);
  // selectPagination$ = this.select(
  //   (state) =>
  //     new PaginationModel(
  //       state.total,
  //       state.pagination.pageSize,
  //       state.pagination.offset
  //     )
  // );
  selectSort$ = this.select(
    (state) => new SortModel(state.pagination.sort, state.pagination.isSortDsc)
  );
  selectFilters$ = this.select((state) => state.filters);
  selectDisableButtons$ = this.select((state) => !state.filters.propertyId);
}
