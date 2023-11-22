// import { Injectable } from '@angular/core';
// import { persistState, Store, StoreConfig } from '@datorama/akita';
// import {
//   FiltrationStateModel,
//   CommonPaginationState,
// } from 'src/app/common/models';
//
// export interface ChemicalsState {
//   pagination: CommonPaginationState;
//   filters: ChemicalsFiltrationState;
//   totalChemicals: number;
// }
//
// export class ChemicalsFiltrationState extends FiltrationStateModel {
//   deviceUserIds: number[];
//   descriptionFilter: string;
// }
//
// function createInitialState(): ChemicalsState {
//   return <ChemicalsState>{
//     pagination: {
//       pageSize: 10,
//       sort: 'Id',
//       isSortDsc: false,
//       offset: 0,
//     },
//     filters: {
//       descriptionFilter: '',
//       deviceUserIds: [],
//       nameFilter: '',
//       tagIds: [],
//     },
//     totalChemicals: 0,
//   };
// }
//
// const chemicalsPersistStorage = persistState({
//   include: ['chemicals'],
//   key: 'itemsChemicalPn',
//   preStorageUpdate(storeName, state: ChemicalsState) {
//     return {
//       pagination: state.pagination,
//       filters: state.filters,
//     };
//   },
// });
//
// @Injectable({ providedIn: 'root' })
// @StoreConfig({ name: 'chemicals', resettable: true })
// export class ChemicalsStore extends Store<ChemicalsState> {
//   constructor() {
//     super(createInitialState());
//   }
// }
//
// export const chemicalsPersistProvider = {
//   provide: 'persistStorage',
//   useValue: chemicalsPersistStorage,
//   multi: true,
// };
