import { Injectable } from '@angular/core';
import { Query } from '@datorama/akita';
import { PaginationModel, SortModel } from 'src/app/common/models';
import {
  DocumentsState,
  DocumentsStore
} from 'src/app/plugins/modules/backend-configuration-pn/modules/documents/store/documents.store';

@Injectable({ providedIn: 'root' })
export class DocumentsQuery extends Query<DocumentsState> {
  constructor(protected store: DocumentsStore) {
    super(store);
  }

  get pageSetting() {
    return this.getValue();
  }

//  selectNameFilter$ = this.select((state) => state.filters.nameFilter);
//  selectPageSize$ = this.select((state) => state.pagination.pageSize);
//   selectPagination$ = this.select(
//     (state) =>
//       new PaginationModel(
//         state.totalProperties,
//         state.pagination.pageSize,
//         state.pagination.offset
//       )
//   );
//   selectSort$ = this.select(
//     (state) => new SortModel(state.pagination.sort, state.pagination.isSortDsc)
//   );
  selectSort$ = this.select(
    (state) => new SortModel(state.pagination.sort, state.pagination.isSortDsc)
  );
  selectFilters$ = this.select((state) => state.filters);
  selectDisableButtons$ = this.select((state) => !state.filters.propertyId);
}
