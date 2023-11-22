// import { Injectable } from '@angular/core';
// import { persistState, Store, StoreConfig } from '@datorama/akita';
// import {
//   // FiltrationStateModel,
//   CommonPaginationState,
// } from 'src/app/common/models';
//
// export interface CompliancesState {
//   pagination: CommonPaginationState;
//   // filters: FiltrationStateModel;
//   total: number;
// }
//
// function createInitialState(): CompliancesState {
//   return <CompliancesState>{
//     pagination: {
//       pageSize: 10,
//       sort: 'Deadline',
//       isSortDsc: false,
//       offset: 0,
//     },
//     // filters: {
//     //   nameFilter: '',
//     //   // tagIds: [],
//     // },
//     total: 0,
//   };
// }
//
// const compliancesPersistStorage = persistState({
//   include: ['compliances'],
//   key: 'backendConfigurationPn',
//   preStorageUpdate(storeName, state: CompliancesState) {
//     return {
//       pagination: state.pagination,
//       // filters: state.filters,
//     };
//   },
// });
//
// @Injectable({ providedIn: 'root' })
// @StoreConfig({ name: 'compliances', resettable: true })
// export class CompliancesStore extends Store<CompliancesState> {
//   constructor() {
//     super(createInitialState());
//   }
// }
//
// export const compliancesPersistProvider = {
//   provide: 'persistStorage',
//   useValue: compliancesPersistStorage,
//   multi: true,
// };
