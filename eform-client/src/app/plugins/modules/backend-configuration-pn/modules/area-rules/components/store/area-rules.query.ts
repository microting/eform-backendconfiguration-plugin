// import {Injectable} from '@angular/core';
// import {Query} from '@datorama/akita';
// import {AreaRulesState, AreaRulesStore} from './';
//
// @Injectable({providedIn: 'root'})
// export class AreaRulesQuery extends Query<AreaRulesState> {
//   constructor(protected store: AreaRulesStore) {
//     super(store);
//   }
//
//   get pageSetting() {
//     return this.getValue();
//   }
//
//   selectActiveSort$ = this.select((state) => state.pagination.sort);
//   selectActiveSortDirection$ = this.select((state) => state.pagination.isSortDsc ? 'desc' : 'asc');
// }
