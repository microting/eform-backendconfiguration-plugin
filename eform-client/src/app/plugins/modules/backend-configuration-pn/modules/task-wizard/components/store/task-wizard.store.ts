import {Injectable} from '@angular/core';
import {persistState, Store, StoreConfig} from '@datorama/akita';
import {CommonPaginationState} from 'src/app/common/models';

export interface TaskWizardFiltrationModel {
  propertyIds: number[];
  tagIds: number[];
  folderIds: number[];
  assignToIds: number[];
  statuses: number[];
}

export interface TaskWizardState {
  filters: TaskWizardFiltrationModel;
  pagination: CommonPaginationState;
  total: number;
}

function createInitialState(): TaskWizardState {
  return <TaskWizardState>{
    filters: {
      propertyIds: [],
      tagIds: [],
      folderIds: [],
      assignToIds: [],
      statuses: [],
    },
    pagination: {
      pageSize: 10,
      sort: 'Id',
      isSortDsc: false,
      offset: 0,
      pageIndex: 0,
    },
    total: 0,
  };
}

const taskWizardPersistStorage = persistState({
  include: ['task-wizard'],
  key: 'backendConfigurationPn',
  preStorageUpdate(storeName, state: TaskWizardState) {
    return {
      filters: state.filters,
      pagination: state.pagination,
    };
  },
});

@Injectable({providedIn: 'root'})
@StoreConfig({name: 'task-wizard', resettable: false})
export class TaskWizardStore extends Store<TaskWizardState> {
  constructor() {
    super(createInitialState());
  }
}

export const taskWizardPersistProvider = {
  provide: 'persistStorage',
  useValue: taskWizardPersistStorage,
  multi: true,
};
