import {Injectable} from '@angular/core';
import {persistState, Store, StoreConfig} from '@datorama/akita';

export interface StatisticsModel {
  propertyId: number | null,
}

export interface StatisticsState {
  filters: StatisticsModel;
}

function createInitialState(): StatisticsState {
  return <StatisticsState>{
    filters: {
      propertyId: null,
    }
  };
}

const statisticsPersistStorage = persistState({
  include: ['statistics'],
  key: 'backendConfigurationPn',
  preStorageUpdate(storeName, state: StatisticsState) {
    return {
      filters: state.filters,
    };
  },
});

@Injectable({providedIn: 'root'})
@StoreConfig({name: 'statistics', resettable: true})
export class StatisticsStore extends Store<StatisticsState> {
  constructor() {
    super(createInitialState());
  }
}

export const statisticsPersistProvider = {
  provide: 'persistStorage',
  useValue: statisticsPersistStorage,
  multi: true,
};
