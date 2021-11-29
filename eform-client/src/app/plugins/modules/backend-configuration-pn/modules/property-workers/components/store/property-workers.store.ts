import { Injectable } from '@angular/core';
import { persistState, Store, StoreConfig } from '@datorama/akita';
import {
  FiltrationStateModel,
  CommonPaginationState,
} from 'src/app/common/models';

export interface PropertyWorkersState {
  pagination: CommonPaginationState;
  filters: FiltrationStateModel;
  totalProperties: number;
}

function createInitialState(): PropertyWorkersState {
  return <PropertyWorkersState>{
    pagination: {
      // pageSize: 10,
      sort: 'MicrotingUid',
      isSortDsc: false,
      // offset: 0,
    },
    filters: {
      nameFilter: '',
      // tagIds: [],
    },
    totalProperties: 0,
  };
}

const propertyWorkersPersistStorage = persistState({
  include: ['propertyWorkers'],
  key: 'backendConfigurationPn',
  preStorageUpdate(storeName, state: PropertyWorkersState) {
    return {
      pagination: state.pagination,
      filters: state.filters,
    };
  },
});

@Injectable({ providedIn: 'root' })
@StoreConfig({ name: 'propertyWorkers', resettable: true })
export class PropertyWorkersStore extends Store<PropertyWorkersState> {
  constructor() {
    super(createInitialState());
  }
}

export const propertyWorkersPersistProvider = {
  provide: 'persistStorage',
  useValue: propertyWorkersPersistStorage,
  multi: true,
};
