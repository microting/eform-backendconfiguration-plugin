// import { Injectable } from '@angular/core';
// import { Query } from '@datorama/akita';
// import { ChemicalsState, ChemicalsStore } from './chemicals.store';
// import { PaginationModel, SortModel } from 'src/app/common/models';
//
// @Injectable({ providedIn: 'root' })
// export class ChemicalsQuery extends Query<ChemicalsState> {
//   constructor(protected store: ChemicalsStore) {
//     super(store);
//   }
//
//   get pageSetting() {
//     return this.getValue();
//   }
//
//   selectTagIds$ = this.select((state) => state.filters.tagIds);
//   selectDeviceUsers$ = this.select((state) => state.filters.deviceUserIds);
//   selectDescriptionFilter$ = this.select(
//     (state) => state.filters.descriptionFilter
//   );
//   selectNameFilter$ = this.select((state) => state.filters.nameFilter);
//   selectPageSize$ = this.select((state) => state.pagination.pageSize);
//   // selectIsSortDsc$ = this.select((state) => state.pagination.isSortDsc);
//   // selectSort$ = this.select((state) => state.pagination.sort);
//   // selectOffset$ = this.select((state) => state.pagination.offset);
//   selectPagination$ = this.select(
//     (state) =>
//       new PaginationModel(
//         state.totalChemicals,
//         state.pagination.pageSize,
//         state.pagination.offset
//       )
//   );
//   selectSort$ = this.select(
//     (state) => new SortModel(state.pagination.sort, state.pagination.isSortDsc)
//   );
// }
