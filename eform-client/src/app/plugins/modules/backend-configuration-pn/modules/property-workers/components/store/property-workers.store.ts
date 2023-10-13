import { Injectable } from '@angular/core';
import { persistState, Store, StoreConfig } from '@datorama/akita';
import {
  FiltrationStateModel,
  CommonPaginationState,
} from 'src/app/common/models';

export interface PropertyWorkersFiltrationModel extends FiltrationStateModel {
  propertyIds: number[];
}

export interface PropertyWorkersState {
  pagination: CommonPaginationState;
  filters: PropertyWorkersFiltrationModel;
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
      propertyIds: [],
      nameFilter: '',
      // tagIds: [],
    }
  };
}

const propertyWorkersPersistStorage = persistState({
  include: ['property-workers'],
  key: 'backendConfigurationPnv2',
  preStorageUpdate(storeName, state: PropertyWorkersState) {
    return {
      pagination: state.pagination,
      filters: state.filters,
    };
  },
});

@Injectable({ providedIn: 'root' })
@StoreConfig({ name: 'property-workers', resettable: true })
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
