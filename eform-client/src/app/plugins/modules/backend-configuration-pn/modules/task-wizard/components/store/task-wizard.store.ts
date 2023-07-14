import {Injectable} from '@angular/core';
import {persistState, Store, StoreConfig} from '@datorama/akita';
import {SortState} from 'src/app/common/models';
import {TaskWizardStatusesEnum} from '../../../../enums';

export interface TaskWizardFiltrationModel {
  propertyIds: number[];
  tagIds: number[];
  folderIds: number[];
  assignToIds: number[];
  status: TaskWizardStatusesEnum | null;
}

export interface TaskWizardPaginationModel extends SortState {
}

export interface TaskWizardState {
  filters: TaskWizardFiltrationModel;
  pagination: TaskWizardPaginationModel;
}

function createInitialState(): TaskWizardState {
  return <TaskWizardState>{
    filters: {
      propertyIds: [],
      tagIds: [],
      folderIds: [],
      assignToIds: [],
      status: null,
    },
    pagination: {
      sort: 'Id',
      isSortDsc: false,
    },
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
