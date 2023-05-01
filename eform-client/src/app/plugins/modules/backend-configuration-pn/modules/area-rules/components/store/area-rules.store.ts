import {Injectable} from '@angular/core';
import {persistState, Store, StoreConfig} from '@datorama/akita';
import {CommonPaginationState} from 'src/app/common/models';

export interface AreaRulesState {
  pagination: CommonPaginationState;
}

function createInitialState(): AreaRulesState {
  return <AreaRulesState>{
    pagination: {
      sort: 'Id',
      isSortDsc: false,
    },
  };
}

const areaRulesPersistStorage = persistState({
  include: ['area-rules'],
  key: 'backendConfigurationPn',
  preStorageUpdate(storeName, state: AreaRulesState) {
    return {
      pagination: state.pagination,
    };
  },
});

@Injectable({providedIn: 'root'})
@StoreConfig({name: 'area-rules', resettable: true})
export class AreaRulesStore extends Store<AreaRulesState> {
  constructor() {
    super(createInitialState());
  }
}

export const areaRulesPersistProvider = {
  provide: 'persistStorage',
  useValue: areaRulesPersistStorage,
  multi: true,
};
