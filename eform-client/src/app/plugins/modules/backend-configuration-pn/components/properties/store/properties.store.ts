import { Injectable } from '@angular/core';
import { persistState, Store, StoreConfig } from '@datorama/akita';
import {
  FiltrationStateModel,
  CommonPaginationState,
} from 'src/app/common/models';

export interface PropertiesState {
  pagination: CommonPaginationState;
  filters: FiltrationStateModel;
  totalProperties: number;
}

function createInitialState(): PropertiesState {
  return <PropertiesState>{
    pagination: {
      pageSize: 10,
      sort: 'Id',
      isSortDsc: false,
      offset: 0,
    },
    filters: {
      nameFilter: '',
      // tagIds: [],
    },
    totalProperties: 0,
  };
}

const propertiesPersistStorage = persistState({
  include: ['properties'],
  key: 'backendConfigurationPn',
  preStorageUpdate(storeName, state: PropertiesState) {
    return {
      pagination: state.pagination,
      filters: state.filters,
    };
  },
});

@Injectable({ providedIn: 'root' })
@StoreConfig({ name: 'properties', resettable: true })
export class PropertiesStore extends Store<PropertiesState> {
  constructor() {
    super(createInitialState());
  }
}

export const propertiesPersistProvider = {
  provide: 'persistStorage',
  useValue: propertiesPersistStorage,
  multi: true,
};
